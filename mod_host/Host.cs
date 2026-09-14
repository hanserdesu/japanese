// WCP Host — 统一接入宿主（阶段 2，v0.2.0）
//
// 职责边界（这是整个重构的核心约定）:
//   宿主负责机制: 读语言包清单 / 认身份 / 作用域门 / 资源路由 / 日后接管-还原。
//   语言包负责数据: packs/<lang>/{manifest.json, books, db, audio}。
//   语言策略负责行为: ILanguageStrategy（每语言一个 ~150 行的类）。
//
// 当前版本在身份门通过后接管固定的游戏补丁点；语言差异只来自
// ILanguageStrategy，队列和 UI 状态由宿主统一还原。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace WcpHost
{
    [BepInPlugin("dev.hanserdesu.wcphost", "WCP Host", "0.2.0")]
    public class WcpHostPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private static WcpHostPlugin _instance;

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<string> _packsRootOverride;

        private ResourceRouter _router;
        private BookRegistry _registry;
        private StrategyRegistry _strategies;
        private HostRuntime _runtime;
        private Harmony _harmony;
        private float _nextProbe;
        private string _lastReported = "";
        private const float ProbeInterval = 1.0f;

        internal static WcpHostPlugin Instance { get { return _instance; } }
        internal ResourceRouter Router { get { return _router; } }
        internal BookRegistry Registry { get { return _registry; } }
        internal HostRuntime Runtime { get { return _runtime; } }
        // 补丁协调器从这里取得当前策略；身份层未激活时始终为 null。
        internal ILanguageStrategy ActiveStrategy
        {
            get
            {
                return _runtime == null ? null : _runtime.ActiveStrategy;
            }
        }

        private void Awake()
        {
            _instance = this;
            Log = Logger;

            _enabled = Config.Bind("General", "Enabled", true,
                "总开关。关闭时宿主只加载注册表、不做任何判定（补丁接入后=完全停用）。");
            _packsRootOverride = Config.Bind("General", "PacksRoot", "",
                "语言包根目录。留空 = <persistentDataPath 的父目录>/packs，" +
                "即 %USERPROFILE%\\AppData\\LocalLow\\WCP\\packs");

            try
            {
                string root = ResolvePacksRoot();
                _registry = BookRegistry.Load(root);
                _router = new ResourceRouter(_registry);
                _strategies = StrategyRegistry.Load(_registry);
                _runtime = new HostRuntime(this, _registry, _router, _strategies);
                ReportRegistry(root);
                _harmony = new Harmony("dev.hanserdesu.wcphost");
                HostPatches.Install(_harmony);
                Log.LogInfo("WcpHost: 固定 Harmony 接线已安装");
            }
            catch (Exception e)
            {
                Log.LogError("WcpHost: 加载语言包失败，宿主进入禁用状态: " + e);
                _enabled.Value = false;
            }
        }

        private string ResolvePacksRoot()
        {
            string custom = _packsRootOverride.Value;
            if (!string.IsNullOrEmpty(custom)) return custom;
            string pdp = Application.persistentDataPath;      // ...\AppData\LocalLow\WCP\wcp
            string parent = Path.GetDirectoryName(pdp);       // ...\AppData\LocalLow\WCP
            return Path.Combine(parent, "packs");
        }

        private void ReportRegistry(string root)
        {
            Log.LogInfo("WcpHost: packs 根 = " + root);
            Log.LogInfo("WcpHost: 加载到 " + _registry.Manifests.Count + " 个语言包");
            for (int i = 0; i < _registry.Manifests.Count; i++)
            {
                LanguageManifest m = _registry.Manifests[i];
                Log.LogInfo(string.Format(
                    "WcpHost:   [{0}] {1} {2} 词={3} 槽={4} 指纹={5}",
                    m.Profile.Language, m.Profile.Id, m.Profile.DisplayName,
                    m.Profile.WordCount, m.Profile.ObservedSlot,
                    m.Profile.Fingerprint.Substring(0, 16)));
            }
            string detail;
            bool ok = _registry.SlotBudgetOk(out detail);
            Log.LogInfo("WcpHost: 槽位预算 " + detail + (ok ? " — 通过" : " — 超限（游戏硬上限 4）"));
            Log.LogInfo("WcpHost: 策略装载 " + _strategies.LoadedCount + "/" +
                        _registry.Manifests.Count + "（缺失策略时仅保留身份层）");
            for (int i = 0; i < _registry.Errors.Count; i++)
                Log.LogWarning("WcpHost: 语言包加载告警: " + _registry.Errors[i]);
            for (int i = 0; i < _strategies.Errors.Count; i++)
                Log.LogWarning("WcpHost: 策略加载告警: " + _strategies.Errors[i]);
        }

        private void Update()
        {
            if (!_enabled.Value || _router == null)
            {
                if (_runtime != null) _runtime.OnDisabled();
                return;
            }
            if (Time.realtimeSinceStartup < _nextProbe) return;
            _nextProbe = Time.realtimeSinceStartup + ProbeInterval;

            string state;
            try
            {
                state = Evaluate();
                if (_runtime != null) _runtime.Tick();
            }
            catch (Exception e)
            {
                // fail-closed: 判定出错一律取消激活，绝不猜测
                _router.SetActive(null);
                if (_runtime != null) _runtime.SetInactive();
                state = "ERR " + e.GetType().Name + ": " + e.Message;
            }
            if (state != _lastReported)
            {
                _lastReported = state;
                Log.LogInfo("WcpHost: " + state);
            }
        }

        internal void RefreshIdentity()
        {
            if (!_enabled.Value || _router == null) return;
            try
            {
                string state = Evaluate();
                if (state != _lastReported)
                {
                    _lastReported = state;
                    Log.LogInfo("WcpHost: " + state);
                }
            }
            catch (Exception e)
            {
                _router.SetActive(null);
                if (_runtime != null) _runtime.SetInactive();
                Log.LogWarning("WcpHost: 刷新身份失败，已关闭接管: " + e.Message);
            }
        }

        // 身份判定：内存词表 / 内存书名 / 落盘书名 / 选中槽位词表四者一致，且指纹命中注册表，才激活。
        // 任一环节读不到或对不上 → 返回"未激活"。这条门是从现有插件的 fail-closed 门搬来的。
        private string Evaluate()
        {
            object rawList = GameAdapter.StaticField("MyParameters", "ChosenBook_List");
            string memName = GameAdapter.StaticField("MyParameters", "ChosenBook_Para") as string;
            IList<string> words = GameAdapter.ToWordList(rawList);
            string diskName = GameAdapter.DiskBookName();

            if (words == null || words.Count == 0)
            {
                if (_runtime != null) _runtime.SetInactive();
                else _router.SetActive(null);
                return "未激活 · 内存词表为空";
            }
            if (string.IsNullOrEmpty(memName))
            {
                if (_runtime != null) _runtime.SetInactive();
                else _router.SetActive(null);
                return "未激活 · 内存书名为空";
            }
            if (string.IsNullOrEmpty(diskName))
            {
                if (_runtime != null) _runtime.SetInactive();
                else _router.SetActive(null);
                return "未激活 · 落盘书名读不到（读档中？）";
            }
            if (diskName != memName)
            {
                if (_runtime != null) _runtime.SetInactive();
                else _router.SetActive(null);
                return "未激活 · 内存书名(" + memName + ") ≠ 落盘书名(" + diskName + ") — 正在切书";
            }

            int slot = GameAdapter.SlotOfBookName(memName);
            if (slot <= 0)
            {
                if (_runtime != null) _runtime.SetInactive();
                else _router.SetActive(null);
                return "未激活 · 书名不是受支持的自定义槽位: " + memName;
            }

            BookProfile p = _registry.Match(words);
            if (p == null)
            {
                if (_runtime != null) _runtime.SetInactive();
                else _router.SetActive(null);
                return "未激活 · 词表 " + words.Count + " 条未命中任何语言包 — 非受管词书";
            }

            IList<string> slotWords = GameAdapter.SlotWords(slot);
            BookProfile slotProfile = _registry.Match(slotWords);
            if (slotProfile == null || slotProfile.Id != p.Id)
            {
                if (_runtime != null) _runtime.SetInactive();
                else _router.SetActive(null);
                return "未激活 · 内存词表与持久化槽位词表指纹不一致 — 正在读档/切书";
            }

            LanguageManifest manifest = _registry.ByProfileId(p.Id);
            if (manifest == null)
            {
                if (_runtime != null) _runtime.SetInactive();
                else _router.SetActive(null);
                return "未激活 · 注册表没有 profile: " + p.Id;
            }

            if (_runtime != null) _runtime.SetIdentity(manifest, words);
            else _router.SetActive(p.Id);

            if (_router.ActiveProfileId != p.Id)
            {
                _router.SetActive(p.Id);
                return "已激活 · " + p.Language + " / " + p.Id +
                       "（" + words.Count + " 词，槽 " + p.ObservedSlot + "）";
            }
            return "已激活 · " + p.Language + " / " + p.Id +
                   "（" + words.Count + " 词，槽 " + slot +
                   (ActiveStrategy == null ? "，策略缺失" : "，策略已载入") + "）";
        }

        private void OnDestroy()
        {
            try
            {
                if (_runtime != null) _runtime.OnDisabled();
                if (_harmony != null) _harmony.UnpatchSelf();
            }
            catch (Exception) { }
            if (_instance == this) _instance = null;
        }
    }
}
