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
using System.Collections.Generic;
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
        private ConfigEntry<bool> _singleJp;
        private readonly Dictionary<TMP_Text, string> _labelBackup =
            new Dictionary<TMP_Text, string>();
        private readonly List<GameObject> _hiddenNodes = new List<GameObject>();
        private bool _lastJpBook;
        private bool _groupLogged;

        // 只有当前在学「自定义词书(日语)」时才做单词旁发音按钮的改造,
        // 英语词书里 UK/US 是真实存在的区分, 不能动。
        internal static bool JapaneseBookSelected()
        {
            try
            {
                var s = MyParameters.ChosenBook_Para;
                if (string.IsNullOrEmpty(s)) return false;
                return s.StartsWith("自定义词书", StringComparison.Ordinal)
                    || s.StartsWith("日语词书", StringComparison.Ordinal);
            }
            catch (Exception) { return false; }
        }
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
            _singleJp = Config.Bind("General", "SingleJpBesideWord", true,
                "单词旁原本成对的英/美发音按钮只保留一个, 显示为 JP。");
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
            bool jpBook = JapaneseBookSelected();
            _lastJpBook = jpBook;
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
                if (_jpLabels.Value && jpBook && IsAccentLabel(s)
                    && LooksLikeAccentNode(t))
                {
                    if (!_labelBackup.ContainsKey(t))
                        _labelBackup[t] = s;
                    t.text = "JP";
                    Log.LogInfo("Label: " + s + " -> JP");
                }
            }
            if (jpBook)
            {
                if (_singleJp.Value) EnforceSingleWordJp();
            }
            else RestoreWordSide();
        }

        // 离开日语词书(切到英语词书)时, 把我们改过的东西还原,
        // 避免影响其它词书里 UK/US 本身的含义。
        private void RestoreWordSide()
        {
            if (_labelBackup.Count > 0)
            {
                var keys = new List<TMP_Text>(_labelBackup.Keys);
                for (int i = 0; i < keys.Count; i++)
                {
                    var t = keys[i];
                    if (t != null && t.text == "JP") t.text = _labelBackup[t];
                }
                _labelBackup.Clear();
            }
            if (_hiddenNodes.Count > 0)
            {
                for (int i = 0; i < _hiddenNodes.Count; i++)
                {
                    var go = _hiddenNodes[i];
                    if (go != null && !go.activeSelf) go.SetActive(true);
                }
                _hiddenNodes.Clear();
            }
        }

        // 单词旁是「英/美」成对的两个发音按钮 (Canvas-scrollMeaningSquare/
        // all/Voice-UK|Voice-US)。词条已全部改用本地日语发音, 两个按钮放
        // 同一段音频, 留一个就够了 —— 保留第一个, 隐藏另一个。
        private void EnforceSingleWordJp()
        {
            var all = Resources.FindObjectsOfTypeAll(typeof(TMP_Text));
            Dictionary<Transform, List<TMP_Text>> groups = null;
            var order = new List<Transform>();
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i] as TMP_Text;
                if (t == null || !IsWordSideAccentNode(t)) continue;
                if (!IsAccentLabel(t.text) && t.text.Trim() != "JP") continue;
                var key = t.transform.parent == null
                    ? null : t.transform.parent.parent;
                if (key == null) continue;
                if (groups == null)
                    groups = new Dictionary<Transform, List<TMP_Text>>();
                List<TMP_Text> lst;
                if (!groups.TryGetValue(key, out lst))
                {
                    lst = new List<TMP_Text>();
                    groups[key] = lst;
                    order.Add(key);
                }
                lst.Add(t);
            }
            if (groups == null) return;
            if (!_groupLogged)
            {
                _groupLogged = true;
                for (int g = 0; g < order.Count; g++)
                {
                    var lst0 = groups[order[g]];
                    Log.LogInfo("WordAccent group " + g + " key="
                        + (order[g] == null ? "<null>" : order[g].name)
                        + " count=" + lst0.Count);
                    for (int i = 0; i < lst0.Count; i++)
                    {
                        var b0 = FindButtonUp(lst0[i].transform);
                        Log.LogInfo("   node " + lst0[i].name + " btn="
                            + (b0 == null ? "<none>" : b0.name)
                            + " active=" + (b0 != null && b0.gameObject.activeSelf));
                    }
                }
            }
            for (int g = 0; g < order.Count; g++)
            {
                var lst = groups[order[g]];
                if (lst.Count < 2) continue;
                // 按层级顺序稳定排序, 保证每次都留下同一个
                lst.Sort(delegate(TMP_Text a, TMP_Text b)
                {
                    return a.transform.GetSiblingIndex()
                        .CompareTo(b.transform.GetSiblingIndex());
                });
                // 这对图标本身不是 Button(纯显示容器), 直接把多余的那个
                // 容器节点停用即可。保留第一个, 隐藏其余。
                for (int i = 0; i < lst.Count; i++)
                {
                    var node = lst[i].transform.parent;
                    if (node == null) continue;
                    bool keep = i == 0;
                    if (node.gameObject.activeSelf != keep)
                    {
                        node.gameObject.SetActive(keep);
                        Log.LogInfo("WordAccent: " + node.name
                            + (keep ? " kept" : " hidden"));
                    }
                    if (!keep && !_hiddenNodes.Contains(node.gameObject))
                        _hiddenNodes.Add(node.gameObject);
                }
            }
        }

        private static Button FindButtonUp(Transform t)
        {
            for (int d = 0; d < 4 && t != null; d++)
            {
                var b = t.GetComponent<Button>();
                if (b != null) return b;
                t = t.parent;
            }
            return null;
        }

        // 只认单词释义区(Canvas-scrollMeaningSquare)里那一对:
        // .../all/Voice-UK/t-uk 与 .../all/Voice-US/t-us。
        // 它们本身不是 Button(纯图标容器), 所以隐藏时直接停用父节点。
        private static bool IsWordSideAccentNode(TMP_Text t)
        {
            var p = t.transform.parent;
            if (p == null) return false;
            var pn = p.name.ToLowerInvariant();
            var tn = t.name.ToLowerInvariant();
            bool named = pn.StartsWith("voice", StringComparison.Ordinal)
                && (pn.EndsWith("uk", StringComparison.Ordinal)
                    || pn.EndsWith("us", StringComparison.Ordinal)
                    || tn == "t-uk" || tn == "t-us");
            if (!named) return false;
            var tr = t.transform;
            for (int d = 0; d < 6 && tr != null; d++)
            {
                if (tr.name.IndexOf("scrollMeaningSquare",
                        StringComparison.OrdinalIgnoreCase) >= 0) return true;
                tr = tr.parent;
            }
            return false;
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
