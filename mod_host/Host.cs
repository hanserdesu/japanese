// WCP Host — 统一接入宿主（阶段 2 骨架，v0.1.0）
//
// 职责边界（这是整个重构的核心约定）:
//   宿主负责机制: 读语言包清单 / 认身份 / 作用域门 / 资源路由 / 日后接管-还原。
//   语言包负责数据: packs/<lang>/{manifest.json, books, db, audio}。
//   语言策略负责行为: ILanguageStrategy（每语言一个 ~150 行的类）。
//
// v0.1.0 只做**只读**的三件事，不补丁游戏、不写任何文件:
//   1. 扫描 packs/*/manifest.json，打印注册表与槽位预算；
//   2. 每秒（节流）读一次内存词表 / 书名字段，按 SHA-256 指纹判定当前语言；
//   3. 身份不一致（内存 vs 落盘 vs 注册表）时 fail-closed，取消激活并记日志。
//
// 这样先能在游戏里验证"身份识别 + 路由"这一层是对的，再往上接补丁。
// 把补丁接进来之前，本 DLL 对游戏是零影响的。
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
    [BepInPlugin("dev.hanserdesu.wcphost", "WCP Host", "0.1.0")]
    public class WcpHostPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private static WcpHostPlugin _instance;

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<string> _packsRootOverride;

        private ResourceRouter _router;
        private BookRegistry _registry;
        private float _nextProbe;
        private string _lastReported = "";
        private const float ProbeInterval = 1.0f;

        internal static WcpHostPlugin Instance { get { return _instance; } }
        internal ResourceRouter Router { get { return _router; } }

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
                ReportRegistry(root);
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
            for (int i = 0; i < _registry.Errors.Count; i++)
                Log.LogWarning("WcpHost: 语言包加载告警: " + _registry.Errors[i]);
        }

        private void Update()
        {
            if (!_enabled.Value || _router == null) return;
            if (Time.realtimeSinceStartup < _nextProbe) return;
            _nextProbe = Time.realtimeSinceStartup + ProbeInterval;

            string state;
            try
            {
                state = Evaluate();
            }
            catch (Exception e)
            {
                // fail-closed: 判定出错一律取消激活，绝不猜测
                _router.SetActive(null);
                state = "ERR " + e.GetType().Name + ": " + e.Message;
            }
            if (state != _lastReported)
            {
                _lastReported = state;
                Log.LogInfo("WcpHost: " + state);
            }
        }

        // 身份判定：内存词表 / 内存书名 / 落盘书名 三者一致，且指纹命中注册表，才激活。
        // 任一环节读不到或对不上 → 返回"未激活"。这条门是从现有插件的 fail-closed 门搬来的。
        private string Evaluate()
        {
            object rawList = GameAdapter.StaticField("MyParameters", "ChosenBook_List");
            string memName = GameAdapter.StaticField("MyParameters", "ChosenBook_Para") as string;
            IList<string> words = GameAdapter.ToWordList(rawList);
            string diskName = GameAdapter.DiskBookName();

            if (words == null || words.Count == 0)
            {
                _router.SetActive(null);
                return "未激活 · 内存词表为空";
            }
            if (string.IsNullOrEmpty(memName))
            {
                _router.SetActive(null);
                return "未激活 · 内存书名为空";
            }
            if (string.IsNullOrEmpty(diskName))
            {
                _router.SetActive(null);
                return "未激活 · 落盘书名读不到（读档中？）";
            }
            if (diskName != memName)
            {
                _router.SetActive(null);
                return "未激活 · 内存书名(" + memName + ") ≠ 落盘书名(" + diskName + ") — 正在切书";
            }

            BookProfile p = _registry.Match(words);
            if (p == null)
            {
                _router.SetActive(null);
                return "未激活 · 词表 " + words.Count + " 条未命中任何语言包 — 非受管词书";
            }

            if (_router.ActiveProfileId != p.Id)
            {
                _router.SetActive(p.Id);
                return "已激活 · " + p.Language + " / " + p.Id +
                       "（" + words.Count + " 词，槽 " + p.ObservedSlot + "）";
            }
            return _lastReported;
        }
    }
}
