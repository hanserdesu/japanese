// WCP JP Word List — BepInEx 5 插件 (C# 5 语法)
//
// 目的: 选中「日语词书」(游戏里就是 自定义词书一~四) 时, 战斗与全部小游戏的
//       词表只从该词书的日语词里取, 不再混入以前学过的英语词。
//
// 逆向依据 (Assembly-CSharp, 2026-09-12):
//   战斗 + S3/S15/S17 小游戏共用同一个静态字段 MyParameters.S7TestWordList_Para:
//     · ChooseWordManager.FightList() 按 复习范围/复习方式 生成词表, 末尾用
//       AddWordsToSelfChosenList(ref list, 5) 从「全库已学词」补足 —— 这一步不看书,
//       日语书已学不足时就会被英语词填满。
//     · 「复习范围词」模式下战斗场景 InitializeManagerS2.Start / WordListManagerS7.Start /
//       updateNewLearnWord.UpdateThis 只做 ES3.Load("S7TestWordList_Para"), 不会重算。
//       所以换到日语词书后, 旧词书留下的英语词表会一直沿用 —— 这就是战斗显示英语的原因。
//     · S3ScoreShow / LifeAndScoreManagerS15 / showWordS17 / RandomButtonInvoker
//       都从这个字段复制词表, 修好这一处即可覆盖战斗 + 全部小游戏。
//
// 设计: 不改游戏逻辑, 只在游戏算完之后把「明显跑偏」的词表重写成该词书的日语词,
//       并回写 ES3。非日语词书完全不介入; 换回非日语词书时让游戏按当前词书重算,
//       避免两种词书互相污染。
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
    [BepInPlugin("dev.hanserdesu.jpwordlist", "WCP JP Word List", "1.1.0")]
    public class JpWordListPlugin : BaseUnityPlugin
    {
        internal const string ReviewRangeType = "复习范围词";

        internal static ManualLogSource Log;
        internal static JpWordListPlugin Instance;

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _topUp;
        private static readonly HashSet<string> Warned = new HashSet<string>();

        private float _nextPoll;
        private string _lastSignature = "";

        // 场景开始时先把词表摆正, 再让游戏去读 (prefix)
        internal static readonly string[] SceneTypes = new string[] {
            "InitializeManagerS2", "WordListManagerS7", "S3ScoreShow",
            "LifeAndScoreManagerS15", "showWordS17", "RandomButtonInvoker",
            "MultipleChoiceGenerator"
        };

        // 游戏算完 / 读完词表之后再校正 (postfix)
        internal static readonly string[] PostTypes = new string[] {
            "ChooseWordManager", "InitializeManagerS2", "updateNewLearnWord",
            "WordListManagerS7", "ButtonEquivalence", "S7NumberAdd",
            "ColorInputFieldS17", "MultipleChoiceGenerator", "RandomButtonInvoker"
        };

        internal static readonly string[] PostMethods = new string[] {
            "Awake", "Start", "FightList", "setFightWord", "setAsFightWord",
            "setAsNewLearnWord", "setAsNewReviewWord", "SwitchSelfChosenMode",
            "UpdateThis", "ResetTestListQuick", "ResetTestListQuick_FreeChoose",
            "ResetExtraReviewList", "ResetExtraReviewList_FreeChoose",
            "Get_TodayNewLearnWordForFight", "Get_TodayNewReviewWordForFight",
            "GetNewWord", "InvokeRandomButton", "Randomize"
        };

        private static readonly string[] SceneMethods = new string[] { "Awake", "Start" };

        void Awake()
        {
            Log = Logger;
            Instance = this;
            _enabled = Config.Bind("General", "Enabled", true,
                "日语词书时, 战斗与小游戏的词表只从该词书取日语词。");
            _topUp = Config.Bind("General", "TopUpFromWholeBook", false,
                "本书已学词不足时是否用词书里其它(未学)的词补满到复习范围词上限。");
            PatchAll();
        }

        // ---------------- 打补丁 ----------------

        private void PatchAll()
        {
            Harmony harmony = new Harmony("dev.hanserdesu.jpwordlist");
            MethodInfo pre = AccessTools.Method(typeof(JpWordListPlugin), "Pre");
            MethodInfo post = AccessTools.Method(typeof(JpWordListPlugin), "Post");
            MethodInfo postRef = AccessTools.Method(typeof(JpWordListPlugin), "PostRef");
            MethodInfo postBook = AccessTools.Method(typeof(JpWordListPlugin), "PostBookChange");

            int preCount = PatchSet(harmony, SceneTypes, SceneMethods, pre);
            int postCount = PatchSet(harmony, PostTypes, PostMethods, post);

            int refCount = 0;
            Type cm = AccessTools.TypeByName("ChooseWordManager");
            if (cm != null)
            {
                MethodInfo m = AccessTools.Method(cm, "AddWordsToSelfChosenList");
                if (m != null)
                {
                    try { harmony.Patch(m, null, new HarmonyMethod(postRef)); refCount++; }
                    catch (Exception e) { Warn("patch AddWordsToSelfChosenList 失败: " + e.Message); }
                }
            }

            int bookCount = 0;
            Type bs = AccessTools.TypeByName("WordChooseButtonS10");
            if (bs != null)
            {
                MethodInfo m2 = AccessTools.Method(bs, "MakeCertainChange");
                if (m2 != null)
                {
                    try { harmony.Patch(m2, null, new HarmonyMethod(postBook)); bookCount++; }
                    catch (Exception e) { Warn("patch MakeCertainChange 失败: " + e.Message); }
                }
            }

            Log.LogInfo("JPWordList: 补丁完成 scene=" + preCount + " post=" + postCount +
                        " addref=" + refCount + " bookchange=" + bookCount);
        }

        private int PatchSet(Harmony harmony, string[] typeNames, string[] methodNames, MethodInfo patch)
        {
            int n = 0;
            for (int i = 0; i < typeNames.Length; i++)
            {
                Type t = AccessTools.TypeByName(typeNames[i]);
                if (t == null)
                {
                    WarnOnce("type:" + typeNames[i], "找不到类型 " + typeNames[i]);
                    continue;
                }
                List<MethodInfo> all = AccessTools.GetDeclaredMethods(t);
                for (int j = 0; j < all.Count; j++)
                {
                    MethodInfo m = all[j];
                    if (!NameIn(m.Name, methodNames)) continue;
                    if (m.IsAbstract || m.ContainsGenericParameters) continue;
                    try { harmony.Patch(m, null, new HarmonyMethod(patch)); n++; }
                    catch (Exception e)
                    {
                        WarnOnce("patch:" + t.Name + "." + m.Name,
                            "patch 失败 " + t.Name + "." + m.Name + ": " + e.Message);
                    }
                }
            }
            return n;
        }

        private static bool NameIn(string name, string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] == name) return true;
            }
            return false;
        }

        // ---------------- Harmony 出入口 ----------------

        // 场景开始: 先把 ES3 里的词书/已学词读进来, 再摆正词表
        private static void Pre()
        {
            if (!IsEnabled()) return;
            try { Enforce(true); }
            catch (Exception e) { Warn("prefix 异常: " + e.Message); }
        }

        // 游戏算完/读完: 直接用内存里的词书数据校正
        private static void Post()
        {
            if (!IsEnabled()) return;
            try { Enforce(false); }
            catch (Exception e) { Warn("postfix 异常: " + e.Message); }
        }

        // AddWordsToSelfChosenList 是带 ref 形参的静态方法, 就地改列表
        private static void PostRef(ref List<string> S7TestWordList_Para)
        {
            if (!IsEnabled()) return;
            try
            {
                List<string> fixedList = Rebuild(S7TestWordList_Para);
                if (fixedList == null) return;
                S7TestWordList_Para = fixedList;
                MyParameters.S7TestWordList_Para = fixedList;
                SaveList(fixedList, "addwords");
            }
            catch (Exception e) { Warn("postfix(ref) 异常: " + e.Message); }
        }

        // 词书切换确认之后: 日语词书 -> 摆正; 换回别的词书 -> 让游戏按当前词书重算
        private static void PostBookChange()
        {
            if (!IsEnabled()) return;
            try
            {
                if (JapaneseBookSelected()) Enforce(true);
                else RegenerateByGame();
            }
            catch (Exception e) { Warn("换书异常: " + e.Message); }
        }

        // 兜底轮询: 覆盖没有补丁可打的读取点
        private void Update()
        {
            if (!IsEnabled()) return;
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 1f;
            try { Enforce(false); }
            catch (Exception e) { Warn("轮询异常: " + e.Message); }
        }

        // ---------------- 主逻辑 ----------------

        internal static void Enforce(bool refreshFromSave)
        {
            if (refreshFromSave) RefreshFromSave();
            List<string> fixedList = Rebuild(MyParameters.S7TestWordList_Para);
            if (fixedList == null) return;
            MyParameters.S7TestWordList_Para = fixedList;
            SaveList(fixedList, refreshFromSave ? "scene" : "postfix");
        }

        // 返回校正后的词表; 无需改动时返回 null
        private static List<string> Rebuild(List<string> cur)
        {
            if (!JapaneseBookSelected()) return null;

            List<string> book = MyParameters.ChosenBook_List;
            if (book == null || book.Count < 5)
            {
                WarnOnce("bookEmpty", "词书词表为空或过小, 无法校正");
                return null;
            }
            HashSet<string> bookSet = new HashSet<string>(book);
            if (cur == null) cur = new List<string>();

            HashSet<string> seen = new HashSet<string>();
            List<string> keep = new List<string>();
            int hits = 0;
            int missed = 0;
            for (int i = 0; i < cur.Count; i++)
            {
                string w = cur[i];
                if (string.IsNullOrEmpty(w)) continue;
                if (!bookSet.Contains(w)) { missed++; continue; }
                hits++;
                if (seen.Add(w)) keep.Add(w);
            }

            string type = MyParameters.S7FightWordType;
            bool reviewRangeMode = string.IsNullOrEmpty(type) || type == ReviewRangeType;
            bool mixed = missed > 0 && hits > 0;
            bool broken = hits == 0 || mixed || (reviewRangeMode && cur.Count < 5);
            if (!broken) return null;

            int cap = MyParameters.S7FightWordMax;
            if (cap < 5) cap = 5;

            // 1) 本书已学词 (保持字典里的学习顺序)
            Dictionary<string, WordInfo> learned = MyParameters.HaveLearnedDictionary;
            if (learned != null)
            {
                foreach (KeyValuePair<string, WordInfo> kv in learned)
                {
                    if (keep.Count >= cap) break;
                    string w = kv.Key;
                    if (string.IsNullOrEmpty(w)) continue;
                    if (!bookSet.Contains(w)) continue;
                    if (seen.Add(w)) keep.Add(w);
                }
            }

            // 2) 本书已学不足 5 个时用词书里其它词补足;
            //    开了 TopUpFromWholeBook 才补满到复习范围词上限
            bool topUp = Instance != null && _topUp.Value;
            int want = topUp ? cap : 5;
            if (keep.Count < want)
            {
                for (int i = 0; i < book.Count && keep.Count < want; i++)
                {
                    string w = book[i];
                    if (string.IsNullOrEmpty(w)) continue;
                    if (seen.Add(w)) keep.Add(w);
                }
            }

            if (keep.Count < 5) return null;
            return keep;
        }

        private static void RegenerateByGame()
        {
            string type = MyParameters.S7FightWordType;
            if (!string.IsNullOrEmpty(type) && type != ReviewRangeType) return;
            ChooseWordManager mgr = UnityEngine.Object.FindObjectOfType<ChooseWordManager>();
            if (mgr == null)
            {
                WarnOnce("noMgr", "找不到 ChooseWordManager, 跳过重算");
                return;
            }
            mgr.FightList();
            Log.LogInfo("JPWordList: 已切回非日语词书, 让游戏按当前词书重算战斗词表");
        }

        // ---------------- ES3 ----------------

        private static void RefreshFromSave()
        {
            try { MyParameters.ChosenBook_Para = ES3.Load<string>("ChosenBook_Para", MyParameters.ChosenBook_Para); }
            catch (Exception e) { WarnOnce("ld:book", "读取 ChosenBook_Para 失败: " + e.Message); }
            try { MyParameters.ChosenBook_List = ES3.Load<List<string>>("ChosenBook_List", MyParameters.ChosenBook_List); }
            catch (Exception e) { WarnOnce("ld:list", "读取 ChosenBook_List 失败: " + e.Message); }
            try { MyParameters.HaveLearnedDictionary = ES3.Load<Dictionary<string, WordInfo>>("HaveLearnedDictionary", MyParameters.HaveLearnedDictionary); }
            catch (Exception e) { WarnOnce("ld:learned", "读取 HaveLearnedDictionary 失败: " + e.Message); }
            try { MyParameters.S7FightWordMax = ES3.Load<int>("S7FightWordMax", MyParameters.S7FightWordMax); }
            catch (Exception e) { WarnOnce("ld:max", "读取 S7FightWordMax 失败: " + e.Message); }
            try { MyParameters.S7FightWordType = ES3.Load<string>("S7FightWordType", MyParameters.S7FightWordType); }
            catch (Exception e) { WarnOnce("ld:type", "读取 S7FightWordType 失败: " + e.Message); }
        }

        private static void SaveList(List<string> list, string where)
        {
            try { ES3.Save("S7TestWordList_Para", list); }
            catch (Exception e) { Warn("写回 S7TestWordList_Para 失败: " + e.Message); }
            LogChange(list, where);
        }

        private static void LogChange(List<string> list, string where)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count && i < 5; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(list[i]);
            }
            string sig = where + "|" + list.Count + "|" + sb.ToString();
            if (Instance != null)
            {
                if (Instance._lastSignature == sig) return;
                Instance._lastSignature = sig;
            }
            Log.LogInfo("JPWordList: 词表 -> " + list.Count + " 个日语词 (示例: " + sb.ToString() + ") @" + where);
        }

        internal static bool JapaneseBookSelected()
        {
            string s = MyParameters.ChosenBook_Para;
            if (string.IsNullOrEmpty(s)) return false;
            return s.StartsWith("自定义词书", StringComparison.Ordinal)
                || s.StartsWith("日语词书", StringComparison.Ordinal);
        }

        private static bool IsEnabled()
        {
            return Instance != null && _enabled != null && _enabled.Value;
        }

        private static void Warn(string s)
        {
            if (Log != null) Log.LogWarning("JPWordList: " + s);
        }

        private static void WarnOnce(string key, string s)
        {
            if (!Warned.Add(key)) return;
            Warn(s);
        }
    }
}

