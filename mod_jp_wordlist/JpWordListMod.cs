// WCP JP Word List — BepInEx 5 插件 (C# 5 语法)
//
// 目的: 让「日语词书」(游戏里就是 自定义词书一~四) 与其它词书彻底互不干扰。
//
// 逆向依据 (Assembly-CSharp, 2026-09-12):
//   1) 战斗 + S3/S15/S17 小游戏共用静态字段 MyParameters.S7TestWordList_Para:
//        · ChooseWordManager.FightList() 按 复习范围/复习方式 生成, 末尾用
//          AddWordsToSelfChosenList(ref list, 5) 从「全库已学词」补足 —— 这一步不看书,
//          日语书已学不足时会被英语词填满。
//        · 「复习范围词」模式下, 战斗场景 InitializeManagerS2.Start /
//          WordListManagerS7.Start / updateNewLearnWord.UpdateThis 只做
//          ES3.Load("S7TestWordList_Para"), 不重算。换书后旧英语词表会一直沿用。
//        · S3ScoreShow / LifeAndScoreManagerS15 / showWordS17 / RandomButtonInvoker
//          都从这个字段复制词表, 修好这一处即可覆盖战斗 + 全部小游戏。
//   2) HaveLearnedDictionary 是**全局**已学词典, 不带词书标记。
//        「复习范围 = 所有已学」的查询 (GetWordsForReviewTime*/Label*/Syn, 无书过滤)
//        以及 allTestWordsS10_Para (S9/S10 测试) 都直接查它 ——
//        所以在日语书里学过的词会漏进英语词书, 反之英语词也会漏进日语书。
//        本插件按「词是否属于当前词书」把这类词表过滤干净。
//
// 设计: 不改游戏逻辑, 只在游戏算完之后校正词表并回写 ES3。
//   · 日语词书: 词表只保留本书日语词, 不足 5 个时从本书补足。
//   · 其它词书: 从词表里剔除日语词 (含从日语书带过去的), 不足 5 个时用当前词书补足。
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
    [BepInPlugin("dev.hanserdesu.jpwordlist", "WCP JP Word List", "1.2.0")]
    public class JpWordListPlugin : BaseUnityPlugin
    {
        internal const string ReviewRangeType = "复习范围词";

        internal static ManualLogSource Log;
        internal static JpWordListPlugin Instance;

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _topUp;
        private static ConfigEntry<bool> _guardOtherLists;
        private static readonly HashSet<string> Warned = new HashSet<string>();

        private float _nextPoll;
        private string _lastSignature = "";
        private string _lastStripSignature = "";

        // 场景加载时先把词表摆正, 再让游戏去读 (prefix)
        internal static readonly string[] SceneTypes = new string[] {
            "InitializeManagerS2", "WordListManagerS7", "S3ScoreShow",
            "LifeAndScoreManagerS15", "showWordS17", "RandomButtonInvoker",
            "MultipleChoiceGenerator", "MultipleChoiceGeneratorS9", "SetS8Data"
        };

        // 游戏算完 / 读完词表之后再校正 (postfix)
        internal static readonly string[] PostTypes = new string[] {
            "ChooseWordManager", "InitializeManagerS2", "updateNewLearnWord",
            "WordListManagerS7", "SetS8Data", "ButtonEquivalence", "S7NumberAdd",
            "ColorInputFieldS17", "MultipleChoiceGenerator", "RandomButtonInvoker"
        };

        internal static readonly string[] PostMethods = new string[] {
            "Awake", "Start", "FightList", "setFightWord", "setAsFightWord",
            "setAsNewLearnWord", "setAsNewReviewWord", "SwitchSelfChosenMode",
            "UpdateThis", "ResetTestListQuick", "ResetTestListQuick_FreeChoose",
            "ResetExtraReviewList", "ResetExtraReviewList_FreeChoose",
            "ResetExtraStudyList", "ResetExtraStudyList_FreeChoose",
            "ResetDailyStudyList", "ResetDailyReviewList",
            "Get_TodayNewLearnWordForFight", "Get_TodayNewReviewWordForFight",
            "GetNewWord", "InvokeRandomButton", "Randomize"
        };

        private static readonly string[] SceneMethods = new string[] { "Awake", "Start" };

        void Awake()
        {
            Log = Logger;
            Instance = this;
            _enabled = Config.Bind("General", "Enabled", true,
                "开启词表校正: 日语词书只出日语词, 其它词书不出日语词。");
            _topUp = Config.Bind("General", "TopUpFromWholeBook", false,
                "日语词书: 本书已学词不足时, 是否用本书其它(未学)词补满到复习范围词上限。");
            _guardOtherLists = Config.Bind("General", "GuardSharedLists", true,
                "同时校正 每日/额外 学习复习表与测试表, 挡住全局已学词典带来的跨词书串词。");
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

        private static void Pre()
        {
            if (!IsEnabled()) return;
            try { Enforce(true); }
            catch (Exception e) { Warn("prefix 异常: " + e.Message); }
        }

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
                List<string> fixedList = Filter(S7TestWordList_Para, TopUpTarget());
                if (fixedList == null) return;
                S7TestWordList_Para = fixedList;
                MyParameters.S7TestWordList_Para = fixedList;
                SaveField("S7TestWordList_Para", fixedList, "addwords");
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
                else
                {
                    RegenerateByGame();
                    Enforce(false);
                }
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
            bool jp = JapaneseBookSelected();

            List<string> fixedMain = Filter(MyParameters.S7TestWordList_Para, TopUpTarget());
            if (fixedMain != null)
            {
                MyParameters.S7TestWordList_Para = fixedMain;
                SaveField("S7TestWordList_Para", fixedMain, jp ? "jp" : "other");
            }

            if (!_guardOtherLists.Value) return;

            FixField("allTestWordsS10_Para", ref MyParameters.allTestWordsS10_Para, 5, jp);
            FixField("S8TestWordList_DailyReview", ref MyParameters.S8TestWordList_DailyReview, 5, jp);
            FixField("S8TestWordList_DailyReview_left", ref MyParameters.S8TestWordList_DailyReview_left, 5, jp);
            FixField("S8TestWordList_ExtraReview", ref MyParameters.S8TestWordList_ExtraReview, 5, jp);
            FixField("S8TestWordList_ExtraReview_left", ref MyParameters.S8TestWordList_ExtraReview_left, 5, jp);
            FixField("S8TestWordList_DailyStudy", ref MyParameters.S8TestWordList_DailyStudy, 5, jp);
            FixField("S8TestWordList_DailyStudy_left", ref MyParameters.S8TestWordList_DailyStudy_left, 5, jp);
            FixField("S8TestWordList_ExtraStudy", ref MyParameters.S8TestWordList_ExtraStudy, 5, jp);
            FixField("S8TestWordList_ExtraStudy_left", ref MyParameters.S8TestWordList_ExtraStudy_left, 5, jp);
            FixField("S8TestWordList_LearnedTest_left", ref MyParameters.S8TestWordList_LearnedTest_left, 5, jp);
        }

        private static void FixField(string key, ref List<string> field, int topUpTo, bool jpBook)
        {
            List<string> fixedList = Filter(field, topUpTo);
            if (fixedList == null) return;
            field = fixedList;
            SaveField(key, fixedList, jpBook ? "jp" : "other");
        }

        private static int TopUpTarget()
        {
            int cap = MyParameters.S7FightWordMax;
            if (cap < 5) cap = 5;
            bool topUp = Instance != null && _topUp.Value;
            return topUp ? cap : 5;
        }

        // 返回校正后的词表; 无需改动时返回 null。
        // 日语词书: 只保留本书的词; 其它词书: 剔除日语词。
        private static List<string> Filter(List<string> cur, int topUpTo)
        {
            if (cur == null || cur.Count == 0) return null;
            bool jp = JapaneseBookSelected();

            List<string> book = MyParameters.ChosenBook_List;
            HashSet<string> bookSet = null;
            if (jp)
            {
                if (book == null || book.Count < 5)
                {
                    WarnOnce("bookEmpty", "词书词表为空或过小, 无法校正");
                    return null;
                }
                bookSet = new HashSet<string>(book);
            }

            HashSet<string> seen = new HashSet<string>();
            List<string> keep = new List<string>();
            int dropped = 0;
            for (int i = 0; i < cur.Count; i++)
            {
                string w = cur[i];
                if (string.IsNullOrEmpty(w)) { dropped++; continue; }
                bool ok = jp ? bookSet.Contains(w) : !LooksJapanese(w);
                if (!ok) { dropped++; continue; }
                if (seen.Add(w)) keep.Add(w);
            }
            if (dropped == 0 && keep.Count == cur.Count) return null;

            if (topUpTo < 5) topUpTo = 5;
            if (keep.Count < topUpTo && book != null)
            {
                // 补词只用「当前词书」且必须通过同一个过滤条件
                for (int i = 0; i < book.Count && keep.Count < topUpTo; i++)
                {
                    string w = book[i];
                    if (string.IsNullOrEmpty(w)) continue;
                    bool ok = jp ? bookSet.Contains(w) : !LooksJapanese(w);
                    if (!ok) continue;
                    if (seen.Add(w)) keep.Add(w);
                }
            }
            if (keep.Count < 5 && cur.Count >= 5) return null;  // 保不住就不动, 免得把界面搞空
            return keep.Count == 0 ? null : keep;
        }

        // 单词是否像日语 (含假名 / 汉字 / 半角片假名 / 々)
        internal static bool LooksJapanese(string w)
        {
            if (string.IsNullOrEmpty(w)) return false;
            for (int i = 0; i < w.Length; i++)
            {
                char c = w[i];
                if ((c >= 0x3040 && c <= 0x30FF) ||
                    (c >= 0x3400 && c <= 0x4DBF) ||
                    (c >= 0x4E00 && c <= 0x9FFF) ||
                    (c >= 0xF900 && c <= 0xFAFF) ||
                    (c >= 0xFF66 && c <= 0xFF9D) ||
                    c == 0x3005 || c == 0x3006 || c == 0x3007)
                {
                    return true;
                }
            }
            return false;
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

        private static void SaveField(string key, List<string> list, string where)
        {
            try { ES3.Save(key, list); }
            catch (Exception e) { Warn("写回 " + key + " 失败: " + e.Message); }
            LogChange(key, list, where);
        }

        private static void LogChange(string key, List<string> list, string where)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count && i < 4; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(list[i]);
            }
            string sig = key + "|" + where + "|" + list.Count + "|" + sb.ToString();
            if (Instance != null)
            {
                if (key == "S7TestWordList_Para")
                {
                    if (Instance._lastSignature == sig) return;
                    Instance._lastSignature = sig;
                }
                else
                {
                    if (Instance._lastStripSignature == sig) return;
                    Instance._lastStripSignature = sig;
                }
            }
            Log.LogInfo("JPWordList: " + key + " -> " + list.Count + " 词 (示例: "
                + sb.ToString() + ") @" + where);
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

