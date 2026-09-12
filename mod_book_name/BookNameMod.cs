// WCP Book Name — BepInEx 5 插件
// 功能: 书单里把「自定义词书一（猫条）」显示成「日语词书一（猫条）」。
//
// 关键约束 (逆向自 Assembly-CSharp.dll, 2026-09-12):
//   1. 前缀「自定义词书一」是硬编码字面量, 只出现在两处显示赋值:
//        AddElementsToScrollView.AddNewElements()  (每日学习书单, 点击按下标)
//        WordChooseButtonS10                         (选书按钮标签)
//      括号里的昵称来自 SaveFile.es3 的 SelfBookName1..4 —— 玩家能改的只有这段。
//   2. WordChooseButtonS10.SonBookChoose(int) 会**读回按钮标签文字**,
//      取「（」之前那一段当作书名 (tem_ChosenBook -> ChosenBook_Para)。
//      所以改显示必须同时兜住这个回读, 否则选书会失效。
//   因此本插件: 平时把标签显示成「日语词书N（昵称）」, 在该方法执行前后
//   临时换回/换回显示, 保证游戏读到的仍是「自定义词书N」。
//
// 设计: 只改 TMP 显示文本与那一个方法的读入时机, 不碰任何数据;
//   反射扫描 + 全局开关, 游戏更新导致类型变化时自动降级不显示。
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace WcpBookName
{
    internal static class Names
    {
        internal static readonly string[] Canon =
        {
            "自定义词书一", "自定义词书二", "自定义词书三", "自定义词书四"
        };

        internal static readonly string[] Cosmetic =
        {
            "日语词书一", "日语词书二", "日语词书三", "日语词书四"
        };

        internal static string ToCosmetic(string s)
        {
            for (int i = 0; i < Canon.Length; i++)
                s = s.Replace(Canon[i], Cosmetic[i]);
            return s;
        }

        internal static string ToCanonical(string s)
        {
            for (int i = 0; i < Cosmetic.Length; i++)
                s = s.Replace(Cosmetic[i], Canon[i]);
            return s;
        }

        // 「自定义词书N…」= 选书按钮标签(游戏会回读), 未打上补丁时不动它
        internal static bool LooksLikeSlotLabel(string s)
        {
            for (int i = 0; i < Canon.Length; i++)
                if (s.StartsWith(Canon[i], StringComparison.Ordinal)) return true;
            return false;
        }
    }

    // 让 SonBookChoose 读到的仍是游戏认识的书名
    [HarmonyPatch(typeof(WordChooseButtonS10), "SonBookChoose")]
    internal static class SonBookChoosePatch
    {
        private static void Prefix(WordChooseButtonS10 __instance, int num)
        {
            Apply(__instance, num, true);
        }

        private static void Postfix(WordChooseButtonS10 __instance, int num)
        {
            Apply(__instance, num, false);
        }

        private static void Apply(WordChooseButtonS10 inst, int num,
                                  bool toCanonical)
        {
            try
            {
                if (inst == null || inst.BookNameText == null) return;
                if (num < 0 || num >= inst.BookNameText.Length) return;
                var label = inst.BookNameText[num];
                if (label == null) return;
                var s = label.text;
                if (string.IsNullOrEmpty(s)) return;
                var n = toCanonical ? Names.ToCanonical(s) : Names.ToCosmetic(s);
                if (string.IsNullOrEmpty(n) || n == s) return;
                label.text = n;
            }
            catch (Exception e)
            {
                if (BookNamePlugin.Log != null)
                    BookNamePlugin.Log.LogError("BookName patch: " + e.Message);
            }
        }
    }

    [BepInPlugin("dev.hanserdesu.bookname", "WCP Book Name", "1.0.0")]
    public class BookNamePlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private static bool _patched;

        private const float ScanInterval = 1f;
        private ConfigEntry<bool> _enabled;
        private float _nextScan;
        private int _rewrites;

        void Awake()
        {
            Log = Logger;
            _enabled = Config.Bind("General", "Enabled", true,
                "把书单里的「自定义词书N（昵称）」显示成「日语词书N（昵称）」。");
            try
            {
                new Harmony("dev.hanserdesu.bookname")
                    .PatchAll(typeof(BookNamePlugin).Assembly);
                _patched = true;
                Log.LogInfo("BookName: Harmony patch OK (SonBookChoose guarded)");
            }
            catch (Exception e)
            {
                _patched = false;
                Log.LogError("BookName: Harmony patch failed, "
                    + "本次只改非选书标签: " + e.Message);
            }
        }

        void Update()
        {
            if (!_enabled.Value) return;
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + ScanInterval;
            try { Scan(); }
            catch (Exception e) { Log.LogError("BookName scan: " + e.Message); }
        }

        private void Scan()
        {
            var all = Resources.FindObjectsOfTypeAll(typeof(TMP_Text));
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i] as TMP_Text;
                if (t == null) continue;
                var s = t.text;
                if (string.IsNullOrEmpty(s)) continue;
                if (s.IndexOf("自定义词书", StringComparison.Ordinal) < 0) continue;
                if (!_patched && Names.LooksLikeSlotLabel(s)) continue;
                var n = Names.ToCosmetic(s);
                if (n == s) continue;
                t.text = n;
                _rewrites++;
                if (_rewrites <= 10)
                    Log.LogInfo("BookName: " + s + " -> " + n);
            }
        }
    }
}

