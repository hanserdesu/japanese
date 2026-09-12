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
using UnityEngine.UI;

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

        // 游戏里这些组件会在 Start / 切换时把标签写回 美/英/UK/US,
        // 打补丁强制显示 JP
        internal static void ForceJpLabels(Component c)
        {
            if (c == null) return;
            var texts = c.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                var s = texts[i].text;
                if (string.IsNullOrEmpty(s)) continue;
                if (!BookNamePlugin.IsAccentLabelPublic(s)) continue;
                if (!HasButtonAncestorPublic(texts[i])) continue;
                texts[i].text = "JP";
            }
        }

        internal static bool IsAccentLabelPublic(string s)
        {
            s = (s ?? string.Empty).Trim();
            return s == "US" || s == "UK" || s == "美" || s == "英";
        }

        internal static bool HasButtonAncestorPublic(TMP_Text t)
        {
            var tr = t.transform;
            for (int d = 0; d < 4 && tr != null; d++)
            {
                if (tr.GetComponent<Button>() != null) return true;
                tr = tr.parent;
            }
            return false;
        }

        // 发音按钮的标签节点可能不是 Button 的直接子级(例如
        // Canvas-.../Button-soundUS-otherPart (1)/Text (TMP) us),
        // 因此再用节点名兜一层。
        internal static bool LooksLikeAccentNode(TMP_Text t)
        {
            if (HasButtonAncestorPublic(t)) return true;
            var p = t.transform.parent;
            string n = ((t.name ?? "") + "|" + (p == null ? "" : p.name))
                .ToLowerInvariant();
            return n.Contains("sound") || n.Contains("voice")
                || n.Contains("uk") || n.Contains("us");
        }

        private const float ScanInterval = 1f;
        private ConfigEntry<bool> _enabled;
        private ConfigEntry<bool> _jpLabels;
        private float _nextScan;
        private float _diagAt2;
        private int _rewrites;

        void Awake()
        {
            Log = Logger;
            _enabled = Config.Bind("General", "Enabled", true,
                "把书单里的「自定义词书N（昵称）」显示成「日语词书N（昵称）」。");
            _jpLabels = Config.Bind("General", "JpSoundLabels", true,
                "把单词发音按钮的英式/美式标记(UK/US)显示成 JP。");
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
            // 诊断: 只要 BepInEx/diag.on 存在, 每 12s 导一次现场
            // (用户删掉该文件即停)。只在抓到界面实例时才写, 覆盖旧内容。
            if (Time.unscaledTime >= _diagAt2)
            {
                _diagAt2 = Time.unscaledTime + 12f;
                var don = System.IO.Path.Combine(Paths.BepInExRootPath, "diag.on");
                if (System.IO.File.Exists(don))
                {
                    Diag.Dump();
                }
            }
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
                if (s.IndexOf("自定义词书", StringComparison.Ordinal) >= 0)
                {
                    if (!_patched && Names.LooksLikeSlotLabel(s)) continue;
                    var n = Names.ToCosmetic(s);
                    if (n == s) continue;
                    t.text = n;
                    _rewrites++;
                    if (_rewrites <= 10)
                        Log.LogInfo("BookName: " + s + " -> " + n);
                    continue;
                }
                // 单词旁的发音按钮: 本游戏词条已全部改用本地日语发音,
                // 英文的 UK/US 标记没有意义 -> 显示成 JP
                if (_jpLabels.Value && IsAccentLabel(s) && LooksLikeAccentNode(t))
                {
                    t.text = "JP";
                    Log.LogInfo("Label: " + s + " -> JP");
                }
            }
        }

        private static bool IsAccentLabel(string s)
        {
            return IsAccentLabelPublic(s);
        }

        private static bool HasButtonAncestor(TMP_Text t)
        {
            return HasButtonAncestorPublic(t);
        }
    }

    // 游戏自带脚本会把发音按钮写成 美/英(UK/US), 打补丁强制显示 JP
    internal static class AccentLabelPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(changePreferredSoundS8), "Start")]
        private static void A1(changePreferredSoundS8 __instance)
        { BookNamePlugin.ForceJpLabels(__instance); }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(changePreferredSoundS8), "ToggleSoundUSIf_Para")]
        private static void A2(changePreferredSoundS8 __instance)
        { BookNamePlugin.ForceJpLabels(__instance); }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(changePreferredSoundS9), "Start")]
        private static void A3(changePreferredSoundS9 __instance)
        { BookNamePlugin.ForceJpLabels(__instance); }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(changePreferredSoundS9), "ToggleSoundUSIf_Para")]
        private static void A4(changePreferredSoundS9 __instance)
        { BookNamePlugin.ForceJpLabels(__instance); }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ChangeLocalVoiceIf), "Start")]
        private static void A5(ChangeLocalVoiceIf __instance)
        { BookNamePlugin.ForceJpLabels(__instance); }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ChangeLocalVoiceIf), "ToggleLocalIf")]
        private static void A6(ChangeLocalVoiceIf __instance)
        { BookNamePlugin.ForceJpLabels(__instance); }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SoundLocalManager), "Start")]
        private static void A7(SoundLocalManager __instance)
        { BookNamePlugin.ForceJpLabels(__instance); }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SoundLocalManager), "Switch_SoundLocalIf")]
        private static void A8(SoundLocalManager __instance)
        { BookNamePlugin.ForceJpLabels(__instance); }
    }
}
