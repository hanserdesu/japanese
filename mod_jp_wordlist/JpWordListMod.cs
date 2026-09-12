// WCP JP Word List — BepInEx 5 插件
// 目的: 选中「日语词书」(= 游戏里的 自定义词书N) 时, 战斗与小游戏的词表
//       只从该词书的日语词里取, 不再混入以前学过的英语词。
//
// 逆向依据 (Assembly-CSharp, 2026-09-12):
//   战斗和三个小游戏共用同一个静态字段 MyParameters.S7TestWordList_Para:
//     · ChooseWordManager.FightList()      —— 按 复习范围/复习方式 生成
//     · ChooseWordManager.AddWordsToSelfChosenList() —— 用 HaveLearnedDictionary
//       补足到 5 个, **这一步不看书**。所以日语词书里本书已学不足 5 个时,
//       词表会被「全库已学词」(绝大部分是英语)填满 —— 这就是战斗显示英语的原因。
//     · ES3 键 "S7TestWordList_Para" 会持久化, 战斗场景 InitializeManagerS2.Start /
//       WordListManagerS7.Start / updateNewLearnWord.UpdateThis 直接 Load 它,
//       因此旧的英语词表会一直沿用下去。
//     · S3ScoreShow / LifeAndScoreManagerS15 / showWordS17 / RandomButtonInvoker
//       都从这个字段复制词表, 所以修好这一处即可覆盖战斗 + 全部小游戏。
//
// 设计: 不改游戏逻辑, 只在游戏算完之后把「明显跑偏」的词表重写成该词书的日语词,
//       并回写 ES3; 选英语词书时完全不介入。
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace JpWordList
{
    [BepInPlugin("dev.hanserdesu.jpwordlist", "WCP JP Word List", "1.0.0")]
    public class JpWordListPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static JpWordListPlugin Instance;
        private ConfigEntry<bool> _enabled;
        private ConfigEntry<bool> _fillToMax;
        private float _next;
        private bool _missingLogged;

        void Awake()
        {
            Log = Logger;
            Instance = this;
            _enabled = Config.Bind("General", "Enabled", true,
                "日语词书时, 战斗与小游戏词表只从该词书取日语词。");
            _fillToMax = Config.Bind("General", "FillToFightWordMax", true,
                "本书已学词不足时, 用词书里的其它日语词补足到「复习范围词上限」。");
            PatchAll();
        }

        // 逐个打补丁: 某个方法缺失时只跳过它, 不让整批失效
        private void PatchAll()
        {
            var h = new Harmony("dev.hanserdesu.jpwordlist");
            var post = AccessTools.Method(typeof(JpWordListPlugin), "Post");
            var postRef = AccessTools.Method(typeof(JpWordListPlugin), "PostRef");
            string[] types = new string[] {
                "ChooseWordManager", "WordListManagerS7",
                "InitializeManagerS2", "updateNewLearnWord"
            };
            string[] methods = new string[] {
                "FightList", "setFightWord", "AddWordsToSelfChosenList",
                "ResetTestListQuick", "ResetTestListQuick_FreeChoose",
                "ResetExtraReviewList", "ResetExtraReviewList_FreeChoose",
                "Get_TodayNewLearnWordForFight", "Get_TodayNewReviewWordForFight",
                "UpdateThis", "Start"
            };
            int ok = 0;
            for (int i = 0; i < types.Length; i++)
            {
                var t = FindType(types[i]);
                if (t == null) { Warn("type not found: " + types[i]); continue; }
                for (int j = 0; j < methods.Length; j++)
                {
                    var m = AccessTools.Method(t, methods[j]);
                    if (m == null) continue;
                    try
                    {
                        var pf = m.IsStatic ? postRef : post;
                        // AddWordsToSelfChosenList 带 ref 形参: 用 PostRef 才能写回
                        if (m.Name == "AddWordsToSelfChosenList") pf = postRef;
                        h.Patch(m, null, new HarmonyMethod(pf));
                        ok++;
                        Log.LogInfo("patched " + types[i] + "." + methods[j]);
                    }
                    catch (Exception e)
                    {
                        Warn("patch " + types[i] + "." + methods[j] + ": " + e.Message);
                    }
                }
            }
            Log.LogInfo("JPWordList: " + ok + " method(s) patched");
        }

        private static void Warn(string s)
        {
            if (Log != null) Log.LogWarning("JPWordList: " + s);
        }

        // 无参 postfix: 游戏算完词表后立刻校正
        private static void Post()
        {
            if (Instance == null || !Instance._enabled.Value) return;
            try { Enforce(); }
            catch (Exception e) { Warn("postfix: " + e.Message); }
        }

        // 带 ref 形参的 postfix: AddWordsToSelfChosenList 就地改列表
        private static void PostRef(ref List<string> S7TestWordList_Para)
        {
            if (Instance == null || !Instance._enabled.Value) return;
            try
            {
                var fixedList = Rebuild(S7TestWordList_Para);
                if (fixedList != null) S7TestWordList_Para = fixedList;
            }
            catch (Exception e) { Warn("postfix(ref): " + e.Message); }
        }

        // 兜底轮询: 覆盖那些没有补丁的读取点(例如小游戏直接复制静态字段)
        void Update()
        {
            if (!_enabled.Value) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            try { Enforce(); }
            catch (Exception e) { Warn("update: " + e.Message); }
        }

        internal static void Enforce()
        {
            if (!JapaneseBookSelected()) return;
            var fixedList = Rebuild(MyParameters.S7TestWordList_Para);
            if (fixedList == null) return;
            MyParameters.S7TestWordList_Para = fixedList;
            SaveEs3("S7TestWordList_Para", fixedList);
            if (Log != null)
                Log.LogInfo("JPWordList: 词表重写为 " + fixedList.Count + " 个日语词");
        }

        // 返回校正后的词表; 无需改动时返回 null
        private static List<string> Rebuild(List<string> cur)
        {
            var book = BookWords();
            if (book == null || book.Count < 5)
            {
                if (Instance != null && !Instance._missingLogged)
                {
                    Instance._missingLogged = true;
                    Warn("词书词表为空, 无法校正");
                }
                return null;
            }
            if (cur == null) cur = new List<string>();

            var seen = new HashSet<string>();
            var keep = new List<string>();
            int hits = 0;
            for (int i = 0; i < cur.Count; i++)
            {
                var w = cur[i];
                if (string.IsNullOrEmpty(w)) continue;
                if (!book.Contains(w)) continue;   // 非本书的词(英语)一律剔除
                hits++;
                if (seen.Add(w)) keep.Add(w);
            }

            // 只在「跑偏」时介入: 一个本书词都没有、词数不足 5、或混入了非本书词,
            // 避免干扰英语词书, 也避免动用户自选的范围设置。
            bool mixed = hits > 0 && keep.Count != cur.Count;
            bool broken = hits == 0 || cur.Count < 5 || mixed;
            if (!broken) return null;

            int target = 5;
            if (Instance != null && Instance._fillToMax.Value)
                target = Math.Max(5, MyParameters.S7FightWordMax);

            var all = MyParameters.ChosenBook_List;
            if (all != null)
            {
                for (int i = 0; i < all.Count && keep.Count < target; i++)
                {
                    var w = all[i];
                    if (string.IsNullOrEmpty(w)) continue;
                    if (seen.Add(w)) keep.Add(w);
                }
            }
            if (keep.Count < 5) return null;   // 凑不够就不动, 免得越改越糟
            return keep;
        }

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

        private static HashSet<string> BookWords()
        {
            try
            {
                var l = MyParameters.ChosenBook_List;
                if (l == null || l.Count == 0) return null;
                return new HashSet<string>(l);
            }
            catch (Exception) { return null; }
        }

        private static Type FindType(string name)
        {
            var t = Type.GetType(name + ", Assembly-CSharp");
            if (t != null) return t;
            var asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
            {
                try
                {
                    t = asms[i].GetType(name);
                    if (t != null) return t;
                }
                catch (Exception) { }
            }
            return null;
        }

        // ES3 是游戏自带的存档库(在其它程序集), 用反射调用, 失败就只靠补丁纠正。
        internal static bool SaveEs3(string key, object value)
        {
            try
            {
                var t = FindType("ES3");
                if (t == null) { Warn("ES3 type not found"); return false; }
                var ms = t.GetMethods(BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < ms.Length; i++)
                {
                    var m = ms[i];
                    if (m.Name != "Save" || !m.IsGenericMethodDefinition) continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 3) continue;
                    if (ps[0].ParameterType != typeof(string)) continue;
                    if (ps[2].ParameterType != typeof(string)) continue;
                    try
                    {
                        m.MakeGenericMethod(typeof(List<string>))
                         .Invoke(null, new object[] { key, value, null });
                        return true;
                    }
                    catch (Exception) { }
                }
                for (int i = 0; i < ms.Length; i++)
                {
                    var m = ms[i];
                    if (m.Name != "Save" || m.IsGenericMethodDefinition) continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 2) continue;
                    if (ps[0].ParameterType != typeof(string)) continue;
                    if (ps[1].ParameterType != typeof(object)) continue;
                    try
                    {
                        m.Invoke(null, new object[] { key, value });
                        return true;
                    }
                    catch (Exception) { }
                }
                return false;
            }
            catch (Exception) { return false; }
        }
    }
}

