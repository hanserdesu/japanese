// WCP JP Word List — BepInEx 5 插件 (C# 5 语法)  v1.7.2
//
// 目的:
//   A) 日语词书(游戏里就是 自定义词书一~四)与其它词书彻底互不干扰。
//   B) 日语词书下: 题目给假名读音, 选项给「汉字写法 + 中文释义」。
//   C) 词表绝不回写成空表/过短表 —— 这是 v1.2.0 的恶性 bug:
//        已学词测试(MultipleChoiceGeneratorS9)取 allTestWordsS10_Para[S8Progress_Para],
//        词表被剔空后第一时间 ArgumentOutOfRangeException 崩溃; 空表还被写回 ES3 存档,
//        每次启动重写一遍, 游戏无法自愈。
//   D) 自选测试(自由选择)的选词界面(截图里那份「已学词汇」)渲染 MyParameters.S9CurrentArray_Para,
//      勾选写 S9extraStudy_Para; 开始测试 = ChooseWordManager.ResetTestListQuick_FreeChoose
//      把 allTestWordsS10_Para 设成 S9extraStudy_Para。所以这两张表也必须只含本书词,
//      否则用户只能从一串英语词里选, 测试里也还是英语。
//   E) 已学词测试与自选测试都跑 MultipleChoiceGeneratorS9: 题目取 allTestWordsS10_Para[progress],
//      题干取 S8needToLearnWordList_Para[0]。两者错位会答非所问, 越界会直接崩; 每次进入前都要摆正。
//   F) 读档之前 MyParameters 全是编译期默认值(默认书「四级大纲词汇」), 那时按内存剔词并写回 ES3
//      会把存档写坏(实测: 默认的 69313 词全词表被当成"别的词书"筛过一遍写回存档)。
//      所以 BookState() 先用存档里的词书名和内存对比, 确认读过档才动手。
//   G) 「已学词测试 → 快速测试」不是只换词池:
//      · 游戏 clickChangeImageSource.StartQuickTest 用**全局**已学词典(别的词书学过的英语词也在里面)
//        按等级筛选 + lastStudyTime 倒序重采样 allTestWordsS10_Para = TestWordsNum_Para 个。
//      · 题面显示的词却是 SetInputFieldValueS8.ShowTheWord 里的 S8needToLearnWordList_Para[0],
//        而这张队列 StartQuickTest 完全不碰, 还是上一轮遗留的内容。
//      两张表不同源 → 题面出现「superiority + 猪肉/全部/停/破碎」这种英语题干配日语选项, 谁都不可能答对。
//      所以: 词池一换, 题干队列/剩余/进度必须一起换(SetTestLists / SyncStemQueue), 且要在游戏读 need[0] 之前。
//   H) 假名题干 / 汉字选项原先依赖 MyParameters.SelfBookMeaningDictionary, 而这张表只有
//      SelfBookMeaningConnectIf 为真时游戏才从 MyBook.es3 载入(GetSelfBookMeaningAfterSet/SelfBookDictionaryGet)。
//      实测该开关是 false(存档里没这个键), 日志里也没有「【自定义书籍字典加载】」, 于是内存字典是空的,
//      KanaOf/KanjiOption 全部返回 null → 题干照旧显示汉字, 选项照旧带【假名】, 功能等于没生效。
//      现在释义来源改成三级: 游戏内存字典 → 自己读 MyBook.es3 的 wordDictionaryN → 出题时顺手攒下的词条;
//      而且题干读音优先直接从**游戏已经渲染出来的选项释义**(它就是「【假名】释义」)里取, 不依赖那张字典。
//   I) 作用域按「词书身份」而不是按槽位或语言判断: 内存词表和 MyBook.es3 槽位词表都必须
//      匹配猫条版的完整 SHA-256 指纹，且是同一册，才会写队列/改题面。其它任何词书即使也是
//      日语，也一律不写；两份记录不一致(通常正在换书)也一律不写。
//   J) 可移植性: 指纹和语言策略是独立 BookProfile。导入到一至四任意槽都能匹配；未来增加
//      法语/俄语词书只需增加 Profile 和对应策略，不会令日语插件接管它们。
//   K) 跨词书启动守卫 (v1.7.2): 状态只在受管日语词书内落盘, 所以「在日语词书里关掉游戏,
//      之后用别的词书继续那个未完成测试」会读到日语队列。插件落盘时同时记下
//      「接管了哪些字段」和「接管前的原值」(自己的 JpWL_* 键), 启动时若当前不是本词书,
//      就把队列还原; 原值不可用(读不到 / 本身就是日语)时清空队列并把本轮测试标记为已完成
//      —— 绝不能留少于 5 个词的词池给 game 的 GenerateOptions, 那会越界崩。
//   L) 版本兼容: ① 完全不碰游戏数据库(wcpOnlyWord.db / wcpFullEng.db), 只读游戏已经在用的
//      ES3 存档; ② 新增逻辑对 MyParameters 一律走反射 (SetParameterField / GetField),
//      字段改名或消失时只记一条日志, 不抛异常; ③ 自己存的数据只用字符串键, 不依赖任何
//      游戏内部类型。所以游戏更新后最坏是「本插件这部分失效并记日志」, 不会崩、不会写坏存档。
//
// 逆向依据 (Assembly-CSharp, 2026-09-12):
//   · 战斗/复习四选一 = MultipleChoiceGenerator: 题干 = testWordText,
//     选项 = option1..4Text = GetMeaning_S7(词); 判对用 wordXText.text(汉字形),
//     另一条判对分支比较 optionText 之间的文本 —— 所以改写题干与选项显示文本是安全的,
//     但绝不能动 wordXText / matchingWordText。
//   · 已学词测试(S9) = MultipleChoiceGeneratorS9 + SetInputFieldValueS8:
//     题干 = 输入框文本, 选项 = GetMeaning_S7(词), 判对用 rightOption_S9 索引。
//     v1.7.1 补充(真因): 题干取 need[0] (ShowTheWord 里 ES3.Load), 选项取
//     allTestWordsS10_Para[progress] (GenerateOptions). 插件此前只改内存、从不落盘,
//     而游戏在这两处都会**从 ES3 重新读一遍**。于是「插件已改正的内存值」被存档里
//     残留的旧队列顶掉: 选项是新算的日语词, 题干却是存档里上一轮(英语书)留下的
//     superiority —— 就是截图里那个英语题干配日语选项的错题。
//     修法: ① 插件写队列时同步 ES3.Save, 并记下改动前的存档基线用于换书回填
//     (SaveField / RestoreSharedFields); ② HealTestList 增加「词池必须全在本书内」
//     的校验 —— 旧逻辑只看 need 与词池是否错位, 而坏存档里两者「自洽地」都是英语,
//     永远诊断不出来, 于是坏存档不会自愈。
//   · 选词界面(截图里那份「已学词汇」)= PageController 渲染 MyParameters.S9CurrentArray_Para,
//     勾选写入 S9extraStudy_Para(string[])。
//   · 词书释义字典 MyParameters.SelfBookMeaningDictionary: 汉字词 = 「【假名】中文〈词性〉」,
//     假名词 = 「中文」。这是本书自有释义, 与游戏本地库无关。
//   · 硬性规模要求: S9 需要 allTestWordsS10_Para.Count >= 5, 战斗需要 S7TestWordList_Para >= 4。
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using WcpBookProfiles;

namespace JpWordList
{
    [BepInPlugin("dev.hanserdesu.jpwordlist", "WCP JP Word List", "1.7.2")]
    public class JpWordListPlugin : BaseUnityPlugin
    {
        internal const string ReviewRangeType = "复习范围词";

        internal static ManualLogSource Log;
        internal static JpWordListPlugin Instance;

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _topUp;
        private static ConfigEntry<bool> _guardOtherLists;
        private static ConfigEntry<bool> _kanaStem;
        private static readonly HashSet<string> Warned = new HashSet<string>();
        private static readonly Dictionary<string, string> LastSig = new Dictionary<string, string>();

        // 存档里的词书名缓存(判断游戏是否已经读档)
        private static string _diskBook;
        private static float _diskBookAt = -1E9f;

        // 本书释义字典(MyBook.es3 wordDictionaryN)缓存 + 出题时攒下的词条缓存
        private static Dictionary<string, string> _bookDict;
        private static int _bookDictIdx = -1;
        private static readonly Dictionary<string, string> Harvested = new Dictionary<string, string>();

        // 当前内存词表与各槽词表的身份缓存；完整指纹比语言采样更严格。
        private const float SlotProfileTtl = 5f;
        private static int _slotProfileIdx = -1;
        private static float _slotProfileAt = -1E9f;
        private static BookProfile _slotProfile;
        private static List<string> _memoryProfileList;
        private static int _memoryProfileCount = -1;
        private static BookProfile _memoryProfile;
        private static string _stateMem = "<none>";   // 本次判定信号，仅供日志
        private static string _stateSlot = "<none>";

        // 本插件只能临时接管已登记词书的共享队列。绝不把改写后的队列保存到 ES3；
        // 离开词书时从游戏自己的存档基线恢复内存，避免残留影响其它词书。
        private static bool _managedScopeActive;
        private static readonly HashSet<string> TouchedListFields = new HashSet<string>();
        private static readonly HashSet<string> TouchedArrayFields = new HashSet<string>();
        private static readonly HashSet<string> TouchedIntFields = new HashSet<string>();

        // 插件动过的存档键, 改动前的原始值。落盘会让游戏的 ES3.Load 读到插件值,
        // 所以换书时不能再靠「从存档重读」回填 —— 必须用这里记下的基线。
        private static readonly Dictionary<string, List<string>> BaselineLists =
            new Dictionary<string, List<string>>();
        private static readonly Dictionary<string, string[]> BaselineArrays =
            new Dictionary<string, string[]>();

        // 插件自有的存档键(不用游戏字段名, 也不碰游戏数据库):
        //   JpWL_owned_lists / JpWL_owned_arrays = 当前被插件接管并已落盘的共享字段名
        //   JpWL_bak_<字段>                      = 该字段被接管前的原始值
        // 只靠 ES3 的字符串键读写, 不引用任何游戏内部类型, 游戏改版也不会因此失效。
        private const string OwnedListKey = "JpWL_owned_lists";
        private const string OwnedArrayKey = "JpWL_owned_arrays";
        private const string BakPrefix = "JpWL_bak_";

        // 跨词书启动守卫: 每次进游戏只做一次
        private static bool _crossBookGuardDone;

        // 换行 / 释义里转义过的换行 (游戏写的是两个反斜杠加 n)
        private static readonly string NL = ((char)10).ToString();
        private static readonly string ESC_NL = new string(new char[] { (char)92, (char)92, 'n' });

        private float _nextPoll;

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
            "ColorInputFieldS17", "MultipleChoiceGenerator", "RandomButtonInvoker",
            "clickChangeImageSource"
        };

        internal static readonly string[] PostMethods = new string[] {
            "Awake", "Start", "FightList", "setFightWord", "setAsFightWord",
            "setAsNewLearnWord", "setAsNewReviewWord", "SwitchSelfChosenMode",
            "UpdateThis", "ResetTestListQuick", "ResetTestListQuick_FreeChoose",
            "ResetExtraReviewList", "ResetExtraReviewList_FreeChoose",
            "ResetExtraStudyList", "ResetExtraStudyList_FreeChoose",
            "ResetDailyStudyList", "ResetDailyReviewList",
            "Get_TodayNewLearnWordForFight", "Get_TodayNewReviewWordForFight",
            "GetNewWord", "InvokeRandomButton", "Randomize", "StartQuickTest"
        };

        private static readonly string[] SceneMethods = new string[] { "Awake", "Start" };

        void Awake()
        {
            Log = Logger;
            Instance = this;
            _enabled = Config.Bind("General", "Enabled", true,
                "开启词表校正: 日语词书只出日语词, 其它词书不出日语词。");
            _topUp = Config.Bind("General", "TopUpFromWholeBook", false,
                "日语词书: 战斗词表不足时, 是否用本书其它(未学)词补满到复习范围词上限。");
            _guardOtherLists = Config.Bind("General", "GuardSharedLists", true,
                "同时校正 每日/额外 学习复习表、已学词测试表与选词列表, 挡住全局已学词典的跨词书串词。");
            _kanaStem = Config.Bind("General", "KanaQuestionStem", true,
                "日语词书: 四选一题目显示假名读音, 选项显示「汉字写法 + 中文释义」。");
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
            MethodInfo postS8 = AccessTools.Method(typeof(JpWordListPlugin), "PostSetAsLearnedTest");
            MethodInfo preS9 = AccessTools.Method(typeof(JpWordListPlugin), "PreMcGenS9");

            int preCount = PatchSet(harmony, SceneTypes, SceneMethods, pre);
            int postCount = PatchSet(harmony, PostTypes, PostMethods, post);

            int refCount = PatchOne(harmony, "ChooseWordManager", "AddWordsToSelfChosenList", postRef);
            int bookCount = PatchOne(harmony, "WordChooseButtonS10", "MakeCertainChange", postBook);
            int healCount = PatchOne(harmony, "SetS8Data", "SetAsLearnedTest", postS8);
            int s9Count = PatchPre(harmony, "MultipleChoiceGeneratorS9", "Start", preS9);
            s9Count += PatchPre(harmony, "MultipleChoiceGeneratorS9", "GenerateOptions", preS9);

            // 选词界面(截图里那份「已学词汇」)渲染前 / 词组重建后立刻摆正候选表
            MethodInfo preSel = AccessTools.Method(typeof(JpWordListPlugin), "PreFixSelection");
            MethodInfo postSel = AccessTools.Method(typeof(JpWordListPlugin), "PostFixSelection");
            int selCount = 0;
            selCount += PatchPre(harmony, "PageController", "SetThis", preSel);
            selCount += PatchOne(harmony, "GoToAllS9", "ArrayToAll", postSel);
            selCount += PatchOne(harmony, "SwitchCurrentArrayS9", "SwitchThis", postSel);

            int stemCount = 0;
            stemCount += PatchOne(harmony, "MultipleChoiceGenerator", "GenerateOptions",
                AccessTools.Method(typeof(JpWordListPlugin), "PostMcGen"));
            stemCount += PatchOne(harmony, "MultipleChoiceGeneratorS9", "GenerateOptions",
                AccessTools.Method(typeof(JpWordListPlugin), "PostMcGenS9"));
            stemCount += PatchOne(harmony, "SetInputFieldValueS8", "ShowTheWord",
                AccessTools.Method(typeof(JpWordListPlugin), "PostShowTheWord"));
            // 题面取 need[0], 必须在游戏读它之前把这些表对齐
            stemCount += PatchPre(harmony, "SetInputFieldValueS8", "ShowTheWord",
                AccessTools.Method(typeof(JpWordListPlugin), "PreShowTheWord"));
            // 「快速测试」按钮: 游戏刚用全局已学词典重建了词池, 立刻按本书重采样并同步题干队列
            stemCount += PatchOne(harmony, "clickChangeImageSource", "StartQuickTest",
                AccessTools.Method(typeof(JpWordListPlugin), "PostStartQuickTest"));

            Log.LogInfo("JPWordList: 补丁完成 scene=" + preCount + " post=" + postCount +
                        " addref=" + refCount + " bookchange=" + bookCount +
                        " heal=" + healCount + " s9pre=" + s9Count + " sel=" + selCount +
                        " stem=" + stemCount);
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
                    catch (Exception e) { WarnOnce("patch:" + t.Name + "." + m.Name, "patch 失败 " + t.Name + "." + m.Name + ": " + e.Message); }
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

        private static int PatchOne(Harmony harmony, string typeName, string methodName, MethodInfo patch)
        {
            Type t = AccessTools.TypeByName(typeName);
            if (t == null) { WarnOnce("type:" + typeName, "找不到类型 " + typeName); return 0; }
            MethodInfo m = AccessTools.Method(t, methodName);
            if (m == null) { WarnOnce("method:" + typeName + "." + methodName, "找不到方法 " + typeName + "." + methodName); return 0; }
            try { harmony.Patch(m, null, new HarmonyMethod(patch)); return 1; }
            catch (Exception e) { Warn("patch " + typeName + "." + methodName + " 失败: " + e.Message); return 0; }
        }

        private static int PatchPre(Harmony harmony, string typeName, string methodName, MethodInfo prefix)
        {
            Type t = AccessTools.TypeByName(typeName);
            if (t == null) { WarnOnce("type:" + typeName, "找不到类型 " + typeName); return 0; }
            MethodInfo m = AccessTools.Method(t, methodName);
            if (m == null) { WarnOnce("method:" + typeName + "." + methodName, "找不到方法 " + typeName + "." + methodName); return 0; }
            try { harmony.Patch(m, new HarmonyMethod(prefix), null); return 1; }
            catch (Exception e) { Warn("patch(pre) " + typeName + "." + methodName + " 失败: " + e.Message); return 0; }
        }

        // ---------------- Harmony 出入口 ----------------

        private static void Pre()
        {
            if (!IsEnabled()) return;
            // 场景补丁比 1 秒轮询更早触发, 顺手把跨词书守卫跑掉(一次性, 见 CrossBookGuard)
            try { CrossBookGuard(); }
            catch (Exception e) { Warn("跨词书守卫异常: " + e.Message); }
            try { Enforce(); }
            catch (Exception e) { Warn("prefix 异常: " + e.Message); }
        }

        private static void Post()
        {
            if (!IsEnabled()) return;
            try { CrossBookGuard(); }
            catch (Exception e) { Warn("跨词书守卫异常: " + e.Message); }
            try { Enforce(); }
            catch (Exception e) { Warn("postfix 异常: " + e.Message); }
        }

        // AddWordsToSelfChosenList 是带 ref 形参的静态方法, 就地改列表
        private static void PostRef(ref List<string> S7TestWordList_Para)
        {
            if (!IsEnabled()) return;
            if (BookState() != 1) return;
            try
            {
                List<string> fixedList = Filter(S7TestWordList_Para, 5, true);
                if (fixedList == null) return;
                S7TestWordList_Para = fixedList;
                MyParameters.S7TestWordList_Para = fixedList;
                SaveField("S7TestWordList_Para", fixedList);
            }
            catch (Exception e) { Warn("postfix(ref) 异常: " + e.Message); }
        }

        // 词书切换确认之后: 只有当前选中的词表匹配本插件的完整身份签名才摆正。
        // 离开时先恢复进入本词书前由游戏保存的共享队列，再让游戏按当前词书重算。
        private static void PostBookChange()
        {
            if (!IsEnabled()) return;
            try
            {
                _slotProfileAt = -1E9f; // 换书了, 槽位记录重读
                _memoryProfileList = null;
                _memoryProfileCount = -1;
                if (BookState() == 1)
                {
                    _managedScopeActive = true;
                    Enforce();
                }
                else if (_managedScopeActive)
                {
                    RestoreSharedFields();
                    _managedScopeActive = false;
                    RegenerateByGame();
                }
            }
            catch (Exception e) { Warn("换书异常: " + e.Message); }
        }

        // 兜底轮询: 覆盖没有补丁可打的读取点 (选词界面/重排/换标签)
        private void Update()
        {
            if (!IsEnabled()) return;
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 1f;
            try { CrossBookGuard(); }
            catch (Exception e) { Warn("跨词书守卫异常: " + e.Message); }
            try { Enforce(); }
            catch (Exception e) { Warn("轮询异常: " + e.Message); }
        }

        // ---------------- 已学词测试的兜底自愈 ----------------

        // SetS8Data.SetAsLearnedTest 之后: 测试词表若被历史 bug 剔空/过短, 整组重建。
        private static void PostSetAsLearnedTest()
        {
            if (!IsEnabled()) return;
            if (BookState() != 1) return;
            try { HealTestList(); }
            catch (Exception e) { Warn("测试词表自愈异常: " + e.Message); }
        }

        // MultipleChoiceGeneratorS9 出题之前再挡一次
        // (Start 与 completeThis 都会调 GenerateOptions, 取 allTestWordsS10_Para[进度], 空表/越界必崩)
        private static void PreMcGenS9()
        {
            if (!IsEnabled()) return;
            if (BookState() != 1) return;
            try { HealTestList(); }
            catch (Exception e) { Warn("S9 前置自愈异常: " + e.Message); }
        }

        // 选词界面渲染前 / 词组重建后: 立刻把候选表与已选表摆正
        private static void PreFixSelection()
        {
            if (BookState() != 1) return;
            try { FixSelectionLists(); }
            catch (Exception e) { Warn("选词表校正异常: " + e.Message); }
        }

        private static void PostFixSelection()
        {
            if (BookState() != 1) return;
            try { FixSelectionLists(); }
            catch (Exception e) { Warn("选词表校正异常: " + e.Message); }
        }

        private static void HealTestList()
        {
            if (MyParameters.S8ThisMode_Para != "已学词测试") return;
            List<string> cur = MyParameters.allTestWordsS10_Para;

            if (cur == null || cur.Count < 5)
            {
                int target = MyParameters.TestWordsNum_Para;
                if (target < 30) target = 30;
                List<string> rebuilt = RebuildWithFallback(cur, 5, target, true);
                if (rebuilt == null) return;

                SetTestLists(rebuilt);
                Warn("已学词测试词表为空/过短, 已用本书词重建 " + rebuilt.Count + " 个");
                return;
            }

            // 词池里混进了别的词书的词(最常见: 进入测试前存档里残留着上一轮英语书的队列,
            // 而 need 与它「自洽」, 错位检查看不出问题) -> 整组按本书重建。
            // 这一步不依赖游戏会不会触发 Enforce, 在出题前把词池钉死。
            List<string> bookFixed = Filter(cur, 5, true);
            if (bookFixed != null)
            {
                SetTestLists(bookFixed);
                Warn("测试词池混入书外词, 已按本书重建 " + bookFixed.Count + " 个");
                return;
            }

            int progress = LoadInt("S8Progress_Para", MyParameters.S8Progress_Para);
            if (progress < 0 || progress >= cur.Count)
            {
                ResetProgress();
                Warn("测试进度越界(" + progress + "/" + cur.Count + "), 已重置到第 1 题");
                return;
            }

            // 题干队列 = allTestWordsS10_Para 从 progress 起的尾巴; 历史 bug 会把两者弄错位
            List<string> need = MyParameters.S8needToLearnWordList_Para;
            if (need == null || need.Count == 0 || need[0] != cur[progress])
            {
                Warn("题干队列与题目错位, 已按进度重新对齐 " + (cur.Count - progress) + " 个");
                SyncStemQueue(progress, cur);
            }
        }

        // 「已学词测试」的题面 = need[0], 正确答案/选项 = allTestWordsS10_Para[S8Progress_Para]。
        // 游戏有两个入口会自作主张地重建词池(快速测试按钮 / SetAsLearnedTest 用 left 重建 need),
        // 只要 need 没跟着换, 题面就会出现别的词书或上一轮遗留的词, 与选项毫无关系。
        // 这里在游戏读 need[0] 之前把两张表对齐。
        private static void PreShowTheWord()
        {
            if (!IsEnabled()) return;
            if (BookState() != 1) return;
            try { HealTestList(); }
            catch (Exception e) { Warn("题干前置自愈异常: " + e.Message); }
        }

        // 「快速测试」按钮按下之后: 词池刚被全局已学词典重建(夹带别的词书的词), 立刻按本书重采样
        private static void PostStartQuickTest()
        {
            if (!IsEnabled()) return;
            try
            {
                if (BookState() != 1) return;       // 只在日语词书下重排, 其它词书保持游戏原样
                if (MyParameters.S8ThisMode_Para != "已学词测试") return;  // 这个按钮就在「已学词测试 → 快速测试」面板上
                int target = MyParameters.TestWordsNum_Para;
                if (target < 5) target = 5;
                List<string> pool = SampleBookLearned(target);
                if (pool == null || pool.Count < 5)
                    pool = RebuildWithFallback(MyParameters.allTestWordsS10_Para, 5, target, true);
                if (pool == null || pool.Count < 5) return;
                SetTestLists(pool);
                Warn("快速测试词池已按本书重采样 " + pool.Count + " 个");
            }
            catch (Exception e) { Warn("快速测试重采样异常: " + e.Message); }
        }

        // 复刻 clickChangeImageSource.GenerateWordList 的「0~5 级筛选 + 最近学习优先」, 但只取本书内已学词
        private static List<string> SampleBookLearned(int target)
        {
            Dictionary<string, WordInfo> learned = MyParameters.HaveLearnedDictionary;
            List<string> book = MyParameters.ChosenBook_List;
            if (learned == null || learned.Count == 0 || book == null || book.Count == 0) return null;
            HashSet<string> bookSet = new HashSet<string>(book);
            List<string> cand = new List<string>();
            foreach (KeyValuePair<string, WordInfo> kv in learned)
            {
                if (kv.Value == null) continue;
                if (!bookSet.Contains(kv.Key)) continue;
                if (!LevelOn(kv.Value.masteryLevel)) continue;
                cand.Add(kv.Key);
            }
            if (cand.Count == 0) return null;
            cand.Sort(delegate(string a, string b)
            {
                WordInfo wa = learned[a];
                WordInfo wb = learned[b];
                int ta = (wa == null) ? int.MinValue : wa.lastStudyTime;
                int tb = (wb == null) ? int.MinValue : wb.lastStudyTime;
                return tb.CompareTo(ta);
            });
            if (target > 0 && cand.Count > target) cand.RemoveRange(target, cand.Count - target);
            return cand;
        }

        // 快速测试面板上的 0~5 级筛选开关 (判定与游戏 GenerateWordList 一致)
        private static bool LevelOn(int level)
        {
            switch (level)
            {
                case 0: return MyParameters.level0If;
                case 1: return MyParameters.level1If;
                case 2: return MyParameters.level2If;
                case 3: return MyParameters.level3If;
                case 4: return MyParameters.level4If;
                case 5: return MyParameters.level5If;
            }
            return false;
        }

        // (pron 白名单方案作废: 实测猫条版整本书的词都在 wcpOnlyWord.db 里, 含日文词,
        //   它分不开「本书词」和「全局已学词典里的英语词」。S9 词池靠 SampleBookLearned
        //   的「本书 ∩ 已学」过滤就够了。)

        // 整套「已学词测试」状态一次写齐: 词池 / 剩余 / 题干队列 / 本轮记录 / 进度
        private static void SetTestLists(List<string> pool)
        {
            if (pool == null || pool.Count < 1) return;
            MyParameters.allTestWordsS10_Para = pool;
            SaveField("allTestWordsS10_Para", pool);
            MyParameters.S8TestWordList_Para = new List<string>(pool);
            SaveField("S8TestWordList_Para", MyParameters.S8TestWordList_Para);
            MyParameters.S8TestWordList_LearnedTest_left = new List<string>(pool);
            SaveField("S8TestWordList_LearnedTest_left", MyParameters.S8TestWordList_LearnedTest_left);
            MyParameters.S8needToLearnWordList_Para = new List<string>(pool);
            SaveField("S8needToLearnWordList_Para", MyParameters.S8needToLearnWordList_Para);

            // 词池是新造的, 「本轮 已完成/状态」记录必须一起清掉, 否则进度会指向错位的词。
            // 这几张表在学习/复习模式里是共用的, 只在已学词测试下清, 别误伤别的模式。
            if (MyParameters.S8ThisMode_Para == "已学词测试")
            {
                MyParameters.S8TestWordList_LearnedTest_Finished = new List<string>();
                SaveField("S8TestWordList_LearnedTest_Finished", MyParameters.S8TestWordList_LearnedTest_Finished);
                MyParameters.S8HaveLearnedWordList_Para = ClearList(MyParameters.S8HaveLearnedWordList_Para);
                SaveField("S8HaveLearnedWordList_Para", MyParameters.S8HaveLearnedWordList_Para);
                MyParameters.S8HaveLearnedStatusList_LearnedTest = ClearList(MyParameters.S8HaveLearnedStatusList_LearnedTest);
                SaveField("S8HaveLearnedStatusList_LearnedTest", MyParameters.S8HaveLearnedStatusList_LearnedTest);
                MyParameters.S8HaveLearnedStatusList_Para = ClearList(MyParameters.S8HaveLearnedStatusList_Para);
                SaveField("S8HaveLearnedStatusList_Para", MyParameters.S8HaveLearnedStatusList_Para);
                MyParameters.S8ForgetThisTimeList_LearnedTest = ClearList(MyParameters.S8ForgetThisTimeList_LearnedTest);
                SaveField("S8ForgetThisTimeList_LearnedTest", MyParameters.S8ForgetThisTimeList_LearnedTest);
                MyParameters.S8ForgetThisTimeList_Para = ClearList(MyParameters.S8ForgetThisTimeList_Para);
                SaveField("S8ForgetThisTimeList_Para", MyParameters.S8ForgetThisTimeList_Para);
                ResetProgress();
            }
        }

        // 题干队列(need)与剩余队列(left)都必须是词池从 progress 起的尾巴: 两张表错位 = 答非所问
        private static void SyncStemQueue(int progress, List<string> pool)
        {
            if (MyParameters.S8ThisMode_Para != "已学词测试") return;
            if (pool == null || pool.Count == 0) return;
            if (progress < 0 || progress >= pool.Count) progress = 0;
            List<string> tail = pool.GetRange(progress, pool.Count - progress);
            MyParameters.S8needToLearnWordList_Para = tail;
            SaveField("S8needToLearnWordList_Para", tail);
            List<string> left = new List<string>(tail);
            MyParameters.S8TestWordList_LearnedTest_left = left;
            SaveField("S8TestWordList_LearnedTest_left", left);
        }

        private static List<string> ClearList(List<string> field)
        {
            if (field == null) return new List<string>();
            field.Clear();
            return field;
        }

        // ---------------- 主逻辑 ----------------

        internal static void Enforce()
        {
            int state = BookState();
            if (state != 1) return;                 // 不是当前日语自定义词书 -> 一律不动
            bool jp = true;

            // 战斗词表: 至少 5 个, 否则战斗界面没词可用
            int fightMax = MyParameters.S7FightWordMax;
            if (fightMax < 5) fightMax = 5;
            bool topUp = jp && Instance != null && _topUp.Value;
            FixField("S7TestWordList_Para", ref MyParameters.S7TestWordList_Para,
                     topUp ? fightMax : 5, true);

            if (!_guardOtherLists.Value) return;

            // 测试词池: S9 取 allTestWordsS10_Para[S8Progress_Para], 既要求 >= 5, 也不能夹带别的词书的词。
            // 池子一换, 进度与「已完成」记录必须一起归零, 否则题干与正确答案会错位。
            List<string> testFixed = Filter(MyParameters.allTestWordsS10_Para, 5, true);
            if (testFixed == null && (MyParameters.allTestWordsS10_Para == null ||
                                      MyParameters.allTestWordsS10_Para.Count < 5))
            {
                testFixed = RebuildWithFallback(MyParameters.allTestWordsS10_Para, 5, 30, true);
            }
            if (testFixed != null)
            {
                MyParameters.allTestWordsS10_Para = testFixed;
                SaveField("allTestWordsS10_Para", testFixed);
                List<string> left = new List<string>(testFixed);
                MyParameters.S8TestWordList_LearnedTest_left = left;
                SaveField("S8TestWordList_LearnedTest_left", left);
                if (MyParameters.S8TestWordList_LearnedTest_Finished != null)
                {
                    MyParameters.S8TestWordList_LearnedTest_Finished.Clear();
                    SaveField("S8TestWordList_LearnedTest_Finished", MyParameters.S8TestWordList_LearnedTest_Finished);
                }
                ResetProgress();
                // 词池换了, 题干队列(need[0] 就是题面显示的词)必须一起换, 否则「别的书/上一轮的词 + 本轮的选项」
                SyncStemQueue(0, testFixed);
            }
            else
            {
                FixField("S8TestWordList_LearnedTest_left", ref MyParameters.S8TestWordList_LearnedTest_left, 5, true);
            }

            FixField("S8TestWordList_DailyReview", ref MyParameters.S8TestWordList_DailyReview, 1, true);
            FixField("S8TestWordList_DailyReview_left", ref MyParameters.S8TestWordList_DailyReview_left, 1, true);
            FixField("S8TestWordList_ExtraReview", ref MyParameters.S8TestWordList_ExtraReview, 1, true);
            FixField("S8TestWordList_ExtraReview_left", ref MyParameters.S8TestWordList_ExtraReview_left, 1, true);
            FixField("S8TestWordList_DailyStudy", ref MyParameters.S8TestWordList_DailyStudy, 1, false);
            FixField("S8TestWordList_DailyStudy_left", ref MyParameters.S8TestWordList_DailyStudy_left, 1, false);
            FixField("S8TestWordList_ExtraStudy", ref MyParameters.S8TestWordList_ExtraStudy, 1, false);
            FixField("S8TestWordList_ExtraStudy_left", ref MyParameters.S8TestWordList_ExtraStudy_left, 1, false);

            // 选词列表(截图里那份「已学词汇」)与自选测试集合
            FixSelectionLists();
        }

        private static void FixField(string key, ref List<string> field, int minKeep, bool preferLearned)
        {
            List<string> fixedList = Filter(field, minKeep, preferLearned);
            if (fixedList == null) return;
            field = fixedList;
            SaveField(key, fixedList);
        }

        private static void FixArray(string key, ref string[] field, int minKeep, bool preferLearned)
        {
            if (field == null) return;
            List<string> fixedList = Filter(new List<string>(field), minKeep, preferLearned);
            if (fixedList == null) return;
            field = fixedList.ToArray();
            SaveField(key, field);
        }

        // 选词界面: 候选表(S9CurrentArray_Para)补足到本书词, 已选表(S9extraStudy_Para)只剔除书外词
        internal static void FixSelectionLists()
        {
            if (!IsEnabled()) return;
            if (BookState() != 1) return;
            if (_guardOtherLists == null || !_guardOtherLists.Value) return;
            FixArray("S9CurrentArray_Para", ref MyParameters.S9CurrentArray_Para, 1, true);
            string[] extra = FilterDropArray(MyParameters.S9extraStudy_Para);
            if (extra == null) return;
            MyParameters.S9extraStudy_Para = extra;
            SaveField("S9extraStudy_Para", extra);
            Warn("已选词里剔除了词书外的词, 剩 " + extra.Length + " 个");
        }

        // 只剔除词书外的词, 不补词, 允许剔空(用户选了几个就是几个, 不足 5 个游戏自己会提示)
        private static string[] FilterDropArray(string[] cur)
        {
            if (cur == null || cur.Length == 0) return null;
            int state = BookState();
            if (state == 0) return null;
            bool jp = (state == 1);
            List<string> book = MyParameters.ChosenBook_List;
            HashSet<string> bookSet = jp ? new HashSet<string>(book) : null;
            HashSet<string> seen = new HashSet<string>();
            List<string> keep = new List<string>();
            bool changed = false;
            for (int i = 0; i < cur.Length; i++)
            {
                string w = cur[i];
                if (string.IsNullOrEmpty(w) || !Allowed(w, jp, bookSet) || !seen.Add(w))
                {
                    changed = true;
                    continue;
                }
                keep.Add(w);
            }
            return changed ? keep.ToArray() : null;
        }

        private static void ResetProgress()
        {
            if (BookState() != 1) return;
            _managedScopeActive = true;
            TouchedIntFields.Add("S8Progress_Para");
            TouchedIntFields.Add("S8LookBack_Para");
            MyParameters.S8Progress_Para = 0;
            MyParameters.S8LookBack_Para = 0;
        }

        private static int LoadInt(string key, int fallback)
        {
            try { return ES3.Load<int>(key, fallback); }
            catch (Exception e)
            {
                WarnOnce("ld:" + key, "读取 " + key + " 失败: " + e.Message);
                return fallback;
            }
        }

        // 返回校正后的词表; 无需改动时返回 null。绝不返回空表/过短表。
        private static List<string> Filter(List<string> cur, int minKeep, bool preferLearned)
        {
            if (cur == null || cur.Count == 0) return null;
            int state = BookState();
            if (state == 0) return null;
            bool jp = (state == 1);
            List<string> book = MyParameters.ChosenBook_List;
            HashSet<string> bookSet = jp ? new HashSet<string>(book) : null;

            HashSet<string> seen = new HashSet<string>();
            List<string> keep = new List<string>();
            int dropped = 0;
            for (int i = 0; i < cur.Count; i++)
            {
                string w = cur[i];
                if (string.IsNullOrEmpty(w)) continue;
                if (!Allowed(w, jp, bookSet)) { dropped++; continue; }
                if (seen.Add(w)) keep.Add(w);
            }
            if (dropped == 0 && keep.Count == cur.Count) return null;

            if (minKeep < 1) minKeep = 1;
            if (keep.Count < minKeep)
            {
                // 被剔空或过短: 用本书词重建(目标 = 原规模, 至少 minKeep)
                int target = cur.Count;
                if (target < minKeep) target = minKeep;
                keep.Clear();
                seen.Clear();
                Rebuild(keep, seen, jp, bookSet, preferLearned, target);
                if (keep.Count < minKeep) return null;   // 凑不齐就不动, 免得把词表弄坏
            }
            return keep;
        }

        // 直接造一份新表: 优先本书已学词(或本书未学词), 再退到整本书
        private static List<string> Rebuild(int minKeep, int target, bool preferLearned)
        {
            if (BookState() == 0) return null;
            bool jp = (BookState() == 1);
            List<string> book = MyParameters.ChosenBook_List;
            HashSet<string> bookSet = jp ? new HashSet<string>(book) : null;
            List<string> dest = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            if (target < minKeep) target = minKeep;
            Rebuild(dest, seen, jp, bookSet, preferLearned, target);
            if (dest.Count < minKeep) return null;
            return dest;
        }

        // 保留现有词(限定本书内)再补足到 target: 用于「用户已选了几个词」或「历史表被剔空」
        private static List<string> RebuildKeeping(List<string> cur, int minKeep, int target, bool preferLearned)
        {
            int state = BookState();
            if (state == 0) return null;
            bool jp = (state == 1);
            List<string> book = MyParameters.ChosenBook_List;
            HashSet<string> bookSet = jp ? new HashSet<string>(book) : null;
            List<string> dest = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            if (cur != null)
            {
                for (int i = 0; i < cur.Count; i++)
                {
                    string w = cur[i];
                    if (string.IsNullOrEmpty(w) || !Allowed(w, jp, bookSet)) continue;
                    if (seen.Add(w)) dest.Add(w);
                }
            }
            if (target < minKeep) target = minKeep;
            if (dest.Count > target) target = dest.Count;
            Rebuild(dest, seen, jp, bookSet, preferLearned, target);
            if (dest.Count < minKeep) return null;
            return dest;
        }

        // 重建词表: 优先按当前词书筛/补; 词书状态不明时退化成「用当前词书列表硬补」。
        // 崩不崩不能取决于能不能认出词书, 所以只要有 ChosenBook_List 就一定能凑够 minKeep。
        private static List<string> RebuildWithFallback(List<string> cur, int minKeep, int target, bool preferLearned)
        {
            List<string> rebuilt = RebuildKeeping(cur, minKeep, target, preferLearned);
            if (rebuilt != null) return rebuilt;

            List<string> book = MyParameters.ChosenBook_List;
            if (book == null || book.Count < minKeep) return null;
            if (target < minKeep) target = minKeep;
            List<string> dest = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            if (cur != null)
            {
                for (int i = 0; i < cur.Count && dest.Count < target; i++)
                {
                    string w = cur[i];
                    if (string.IsNullOrEmpty(w)) continue;
                    if (seen.Add(w)) dest.Add(w);
                }
            }
            FillFromList(dest, seen, book, target);
            if (dest.Count < minKeep) return null;
            return dest;
        }

        private static void Rebuild(List<string> dest, HashSet<string> seen, bool jp, HashSet<string> bookSet, bool preferLearned, int target)
        {
            List<string> book = MyParameters.ChosenBook_List;
            if (jp && book != null)
            {
                if (preferLearned)
                {
                    FillFromLearned(dest, seen, jp, bookSet, target);
                    FillFromList(dest, seen, book, target);
                }
                else
                {
                    Dictionary<string, WordInfo> learned = MyParameters.HaveLearnedDictionary;
                    for (int i = 0; i < book.Count && dest.Count < target; i++)
                    {
                        string w = book[i];
                        if (string.IsNullOrEmpty(w)) continue;
                        if (learned != null && learned.ContainsKey(w)) continue;
                        if (seen.Add(w)) dest.Add(w);
                    }
                    FillFromList(dest, seen, book, target);
                    FillFromLearned(dest, seen, jp, bookSet, target);
                }
            }
            else
            {
                FillFromLearned(dest, seen, jp, bookSet, target);
            }
        }

        private static void FillFromLearned(List<string> dest, HashSet<string> seen, bool jp, HashSet<string> bookSet, int target)
        {
            Dictionary<string, WordInfo> learned = MyParameters.HaveLearnedDictionary;
            if (learned == null) return;
            foreach (KeyValuePair<string, WordInfo> kv in learned)
            {
                if (dest.Count >= target) break;
                string w = kv.Key;
                if (string.IsNullOrEmpty(w)) continue;
                if (!Allowed(w, jp, bookSet)) continue;
                if (seen.Add(w)) dest.Add(w);
            }
        }

        private static void FillFromList(List<string> dest, HashSet<string> seen, List<string> src, int target)
        {
            if (src == null) return;
            for (int i = 0; i < src.Count && dest.Count < target; i++)
            {
                string w = src[i];
                if (string.IsNullOrEmpty(w)) continue;
                if (seen.Add(w)) dest.Add(w);
            }
        }

        private static bool Allowed(string w, bool jp, HashSet<string> bookSet)
        {
            if (string.IsNullOrEmpty(w)) return false;
            return jp ? (bookSet != null && bookSet.Contains(w)) : !LooksJapanese(w);
        }

        // 词书状态: 1 = 当前选中的、已登记的日语词书；0 = 其它任何情况(一律不动)。
        // 身份来自完整词表指纹；槽位仅用于读取当前导入位置，不参与身份判断。
        internal static int BookState()
        {
            int state = BookStateRaw();
            LogBookState(state);
            return state;
        }

        private static int BookStateRaw()
        {
            _stateMem = "<none>";
            _stateSlot = "<none>";
            if (!BookReady()) return 0;      // 还没读档 -> 一律不动
            string name = MyParameters.ChosenBook_Para;
            if (string.IsNullOrEmpty(name)) return 0;
            List<string> book = MyParameters.ChosenBook_List;
            if (book == null || book.Count < 5) return 0;

            // 只读取当前选中的自定义槽。原版书不可能有可管理 Profile，绝不写队列。
            int idx = SelfBookIndexOf(name);
            if (idx <= 0) return 0;
            BookProfile memoryProfile = ProfileOfCurrentList(book);
            BookProfile slotProfile = SlotProfile(idx);
            _stateMem = ProfileId(memoryProfile); // 游戏实际拿来过滤的那份词表
            _stateSlot = ProfileId(slotProfile);  // 这个槽自己在 MyBook.es3 里记的那份
            if (memoryProfile == null || memoryProfile.Language != BookProfiles.Japanese) return 0;
            // 读不到、不是本书、或正在换书时都失败关闭；不能以语言相近为由接管。
            if (slotProfile == null || slotProfile.Id != memoryProfile.Id) return 0;
            return 1;
        }

        private static string ProfileId(BookProfile profile)
        {
            return profile == null ? "<none>" : profile.Id;
        }

        // 当前内存词表在一次切换后通常复用同一个 List；缓存避免每帧计算完整 SHA-256。
        private static BookProfile ProfileOfCurrentList(List<string> list)
        {
            if (System.Object.ReferenceEquals(list, _memoryProfileList) && list != null &&
                list.Count == _memoryProfileCount) return _memoryProfile;
            _memoryProfileList = list;
            _memoryProfileCount = list == null ? -1 : list.Count;
            _memoryProfile = BookProfiles.Match(list);
            return _memoryProfile;
        }

        // 自定义槽位(1~4)自己的完整词表。槽位只定位导入位置，不是身份条件。
        private static BookProfile SlotProfile(int idx)
        {
            if (idx <= 0) return null;
            float now = Time.realtimeSinceStartup;
            if (idx == _slotProfileIdx && now - _slotProfileAt < SlotProfileTtl) return _slotProfile;
            _slotProfileIdx = idx;
            _slotProfileAt = now;
            _slotProfile = null;
            try
            {
                string path = System.IO.Path.Combine(Application.persistentDataPath, "MyBook.es3");
                List<string> slot = new List<string>();
                try
                {
                    string[] arr = ES3.Load<string[]>("SelfBookList" + idx, path);
                    if (arr != null && arr.Length > 0) slot.AddRange(arr);
                }
                catch (Exception) { }
                if (slot.Count < 5)
                {
                    // 有的存档只写了释义字典, 用它的键顶上
                    try
                    {
                        Dictionary<string, string> d = ES3.Load<Dictionary<string, string>>("wordDictionary" + idx, path);
                        if (d != null && d.Count > 0)
                        {
                            slot.Clear();
                            foreach (KeyValuePair<string, string> kv in d) slot.Add(kv.Key);
                        }
                    }
                    catch (Exception) { }
                }
                if (slot.Count > 0) _slotProfile = BookProfiles.Match(slot);
            }
            catch (Exception e) { WarnOnce("slotprofile:" + idx, "读取槽位 " + idx + " 词表失败: " + e.Message); }
            return _slotProfile;
        }

        // 两侧 Profile 必须相同，才允许日语策略工作。
        private static void LogBookState(int state)
        {
            List<string> book = MyParameters.ChosenBook_List;
            int n = (book == null) ? -1 : book.Count;
            string sig = state + "|" + MyParameters.ChosenBook_Para + "|" + n + "|" + _stateMem + "|" + _stateSlot;
            string prev;
            if (LastSig.TryGetValue("bookstate", out prev) && prev == sig) return;
            LastSig["bookstate"] = sig;
            Log.LogInfo("JPWordList: 词书状态=" + state + " 内存书名=" + MyParameters.ChosenBook_Para +
                        " 内存词表=" + n + "条(内存 Profile=" + _stateMem + " 槽位 Profile=" + _stateSlot +
                        ") 存档书名=" + (DiskBookName() ?? "<读不到>"));
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

        // ---------------- 题干 / 选项显示改写 ----------------

        private static bool KanaOn()
        {
            if (!IsEnabled()) return false;
            if (_kanaStem == null || !_kanaStem.Value) return false;
            return BookState() == 1;     // 按内容判定: 自定义槽里装的是英语书时不改写题干/选项
        }

        // 是否处于「测试」式问答 (学习界面不改写, 免得影响背单词)
        private static bool IsTestMode()
        {
            string m = MyParameters.S8ThisMode_Para;
            if (string.IsNullOrEmpty(m)) return false;
            if (m == "已学词测试") return true;
            if ((m == "每日复习" || m == "额外复习") && MyParameters.S8ReviewModeType == 1) return true;
            return false;
        }

        private static string CurrentFightWord()
        {
            List<string> l = MyParameters.S7TestWordList_Para;
            int i = MyParameters.S7Progress_Para;
            if (l == null || i < 0 || i >= l.Count) return null;
            return l[i];
        }

        private static string CurrentTestWordS9()
        {
            List<string> l = MyParameters.allTestWordsS10_Para;
            int i = MyParameters.S8Progress_Para;
            if (l == null || i < 0 || i >= l.Count) return null;
            return l[i];
        }

        // 战斗/复习四选一: 题干 = 假名, 选项 = 汉字 + 中文释义
        private static void PostMcGen(MultipleChoiceGenerator __instance)
        {
            if (!KanaOn() || __instance == null) return;
            try
            {
                string word = CurrentFightWord();
                TextMeshProUGUI[] opts = new TextMeshProUGUI[] {
                    __instance.option1Text, __instance.option2Text,
                    __instance.option3Text, __instance.option4Text };
                TextMeshProUGUI[] optsSM = new TextMeshProUGUI[] {
                    __instance.option1Text_SM, __instance.option2Text_SM,
                    __instance.option3Text_SM, __instance.option4Text_SM };
                TextMeshProUGUI[] words = new TextMeshProUGUI[] {
                    __instance.word1Text, __instance.word2Text,
                    __instance.word3Text, __instance.word4Text };

                // 趁释义原文还在, 先取正确答案的假名读音, 并把见到的词条攒下来
                string kana = null;
                for (int i = 0; i < opts.Length; i++)
                {
                    if (opts[i] == null) continue;
                    string w = (words[i] != null) ? words[i].text : null;
                    Harvest(w, opts[i].text);
                    if (kana == null && !string.IsNullOrEmpty(word) && w == word)
                        kana = ReadingOf(CleanEntry(opts[i].text));
                }
                if (kana == null) kana = KanaOf(word);      // 兜底: 释义没带【假名】时查本书字典

                for (int i = 0; i < opts.Length; i++)
                {
                    if (opts[i] == null) continue;
                    string w = (words[i] != null) ? words[i].text : null;
                    string txt = KanjiOption(w, opts[i].text);
                    opts[i].text = txt;
                    if (optsSM[i] != null) optsSM[i].text = txt;
                }
                if (kana != null && __instance.testWordText != null) __instance.testWordText.text = kana;
            }
            catch (Exception e) { Warn("题干改写(战斗)异常: " + e.Message); }
        }

        // 已学词测试: 选项 = 汉字 + 中文释义; 题干沿用 ShowTheWord 的假名
        private static void PostMcGenS9(MultipleChoiceGeneratorS9 __instance)
        {
            if (!KanaOn() || __instance == null) return;
            try
            {
                string word = CurrentTestWordS9();
                string[] optWords = new string[] {
                    MyParameters.S9Option1_Para, MyParameters.S9Option2_Para,
                    MyParameters.S9Option3_Para, MyParameters.S9Option4_Para };

                string kana = null;
                if (__instance.optionText != null)
                {
                    for (int i = 0; i < __instance.optionText.Length && i < optWords.Length; i++)
                    {
                        if (__instance.optionText[i] == null) continue;
                        Harvest(optWords[i], __instance.optionText[i].text);
                        if (kana == null && !string.IsNullOrEmpty(word) && optWords[i] == word)
                            kana = ReadingOf(CleanEntry(__instance.optionText[i].text));
                    }
                    for (int i = 0; i < __instance.optionText.Length && i < optWords.Length; i++)
                    {
                        if (__instance.optionText[i] == null) continue;
                        __instance.optionText[i].text = KanjiOption(optWords[i], __instance.optionText[i].text);
                    }
                }
                if (kana == null) kana = KanaOf(word);
                if (kana != null) SetStemText(word, kana);
            }
            catch (Exception e) { Warn("题干改写(S9)异常: " + e.Message); }
        }

        // 学习/复习/测试界面: 测试模式下把题干输入框显示成假名
        private static void PostShowTheWord(SetInputFieldValueS8 __instance)
        {
            if (!KanaOn() || __instance == null) return;
            if (!IsTestMode()) return;
            try
            {
                if (__instance.inputField == null) return;
                string word = __instance.inputField.text;
                string kana = KanaOf(word);
                if (kana != null && kana != word) __instance.inputField.text = kana;
            }
            catch (Exception e) { Warn("题干改写(S8)异常: " + e.Message); }
        }

        // 题干可能挂在输入框, 也可能挂在发音按钮旁的文本上, 两处都兜住
        private static void SetStemText(string word, string kana)
        {
            if (string.IsNullOrEmpty(word)) return;
            SetInputFieldValueS8[] sifs = UnityEngine.Object.FindObjectsOfType<SetInputFieldValueS8>();
            for (int i = 0; i < sifs.Length; i++)
            {
                if (sifs[i] == null || sifs[i].inputField == null) continue;
                if (sifs[i].inputField.text == word) sifs[i].inputField.text = kana;
            }
            VocabularyAudioPlayer[] vaps = UnityEngine.Object.FindObjectsOfType<VocabularyAudioPlayer>();
            for (int i = 0; i < vaps.Length; i++)
            {
                if (vaps[i] == null || vaps[i].text1 == null) continue;
                if (vaps[i].text1.text == word) vaps[i].text1.text = kana;
            }
        }

        // ---------------- 释义字典: 取假名 / 组选项 ----------------

        // 词条里取假名读音; 汉字词才有「【假名】」, 取不到返回 null
        internal static string KanaOf(string word)
        {
            return ReadingOf(EntryOf(word));
        }

        // 从「【假名】中文释义〈词性〉」里取假名; 没有【】或括号里不是假名则返回 null
        private static string ReadingOf(string entry)
        {
            if (string.IsNullOrEmpty(entry)) return null;
            int a = entry.IndexOf('【');
            if (a < 0) return null;
            int b = entry.IndexOf('】', a + 1);
            if (b < 0 || b <= a + 1) return null;
            string kana = entry.Substring(a + 1, b - a - 1).Trim();
            if (kana.Length == 0) return null;
            if (!IsKanaOnly(kana)) return null;
            return kana;
        }

        // 去掉词条里的「【假名】」; 没有【】或去掉后为空则返回 null
        private static string StripReading(string entry)
        {
            if (string.IsNullOrEmpty(entry)) return null;
            int a = entry.IndexOf('【');
            if (a < 0) return null;
            int b = entry.IndexOf('】', a + 1);
            if (b < 0) return null;
            string rest = (entry.Substring(0, a) + entry.Substring(b + 1)).Trim();
            return (rest.Length == 0) ? null : rest;
        }

        // 选项 = 「汉字写法 + 中文释义(去掉【假名】)」; 假名词或查不到时原样返回。
        // 优先用游戏已经渲染出来的释义文本(它本身就是「【假名】释义」), 不依赖内存字典是否被填过。
        internal static string KanjiOption(string word, string orig)
        {
            if (string.IsNullOrEmpty(word) || string.IsNullOrEmpty(orig)) return orig;
            string rest = StripReading(CleanEntry(orig));
            if (rest == null)
            {
                string entry = EntryOf(word);
                if (entry == null) return orig;
                rest = StripReading(entry);
                if (rest == null) return orig;
            }
            // 释义里已经带了这个词的汉字写法(例如「全部；所有〈名〉」)就不必再接一遍
            if (rest.StartsWith(word, StringComparison.Ordinal)) return rest;
            return word + " " + rest;
        }

        // 释义来源(按可靠性): 游戏内存字典 -> 自己读的 MyBook.es3 wordDictionaryN -> 出题时攒下的词条。
        // 不能只认 MyParameters.SelfBookMeaningDictionary: 实测 SelfBookMeaningConnectIf 为 false 时它是空的。
        private static string EntryOf(string word)
        {
            if (string.IsNullOrEmpty(word)) return null;
            string v = Lookup(MyParameters.SelfBookMeaningDictionary, word);
            if (v == null) v = Lookup(BookDict(), word);
            if (v == null) v = Lookup(Harvested, word);
            if (v == null) return null;
            return CleanEntry(v);
        }

        private static string Lookup(Dictionary<string, string> d, string word)
        {
            if (d == null || d.Count == 0) return null;
            string v;
            if (d.TryGetValue(word, out v) && !string.IsNullOrEmpty(v)) return v;
            string low = word.ToLower();
            if (low != word && d.TryGetValue(low, out v) && !string.IsNullOrEmpty(v)) return v;
            return null;
        }

        // 出题/学习界面渲染出来的释义就是「【假名】释义」, 见到就记下来, 之后题干要假名时直接查
        private static void Harvest(string word, string meaning)
        {
            if (string.IsNullOrEmpty(word) || string.IsNullOrEmpty(meaning)) return;
            if (meaning.IndexOf('【') < 0) return;
            string cur;
            if (Harvested.TryGetValue(word, out cur) && !string.IsNullOrEmpty(cur)) return;
            Harvested[word] = CleanEntry(meaning);
        }

        // 当前自定义词书序号(自定义词书一~四 / 日语词书一~四, 允许带昵称后缀); 认不出返回 0
        private static int SelfBookIndexOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            if (!name.StartsWith("自定义词书", StringComparison.Ordinal) &&
                !name.StartsWith("日语词书", StringComparison.Ordinal) &&
                !name.StartsWith("日语词库", StringComparison.Ordinal)) return 0;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '一') return 1;
                if (c == '二') return 2;
                if (c == '三') return 3;
                if (c == '四') return 4;
                if (c >= '1' && c <= '4') return c - '0';
            }
            return 0;
        }

        // MyBook.es3 里那份本书释义字典。游戏只在 SelfBookMeaningConnectIf 为真时才读它,
        // 那开关实测是关的, 所以这里自己读一份(按词书序号缓存, 换书才重读)。
        private static Dictionary<string, string> BookDict()
        {
            int idx = SelfBookIndexOf(MyParameters.ChosenBook_Para);
            if (idx <= 0) return null;
            if (_bookDictIdx == idx) return _bookDict;
            _bookDictIdx = idx;
            _bookDict = null;
            try
            {
                string path = System.IO.Path.Combine(Application.persistentDataPath, "MyBook.es3");
                _bookDict = ES3.Load<Dictionary<string, string>>("wordDictionary" + idx, path);
                if (_bookDict != null && Log != null)
                {
                    Log.LogInfo("JPWordList: 本书释义字典 wordDictionary" + idx + " 载入 " +
                                _bookDict.Count + " 条 (题干假名 / 汉字选项用)");
                }
            }
            catch (Exception e)
            {
                WarnOnce("bookdict" + idx, "读取 MyBook.es3 wordDictionary" + idx + " 失败: " + e.Message);
            }
            return _bookDict;
        }

        private static string CleanEntry(string v)
        {
            if (v.IndexOf(ESC_NL) < 0) return v;
            return v.Replace(ESC_NL, NL);
        }

        // 只接受假名(含长音符/中点/浊点/全角空格), 防止把中文或汉字当读音
        private static bool IsKanaOnly(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool ok = (c >= 0x3040 && c <= 0x30FF) ||
                          c == 0x30FB || c == 0x30FC || c == 0x3099 || c == 0x309A ||
                          c == 0x309B || c == 0x309C ||
                          (c >= 0xFF66 && c <= 0xFF9F) ||
                          c == ' ' || c == '.' || c == 0x3000 || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        // ---------------- ES3 ----------------

        // ES3 读 string 必须写命名参数 defaultValue:, 否则 C# 会选到 (key, filePath) 重载,
        // 把字符串默认值当文件名去找文件 (游戏自己那些 ES3.Load<string>(key, 值) 就是这么写坏的)。
        private static string LoadString(string key, string fallback)
        {
            try { return ES3.Load<string>(key, defaultValue: fallback); }
            catch (Exception e)
            {
                WarnOnce("ldstr:" + key, "读取 " + key + " 失败: " + e.Message);
                return fallback;
            }
        }

        // 存档里的词书名(1 秒缓存)
        private static string DiskBookName()
        {
            if (Time.realtimeSinceStartup - _diskBookAt < 1f) return _diskBook;
            _diskBookAt = Time.realtimeSinceStartup;
            _diskBook = LoadString("ChosenBook_Para", null);
            return _diskBook;
        }

        // 游戏还没把存档读进 MyParameters 之前, 内存里全是编译期默认值(默认书是「四级大纲词汇」),
        // 那时按内存里的词书剔词再写回 ES3 会把存档写坏。确认内存与存档一致了才动手。
        internal static bool BookReady()
        {
            string disk = DiskBookName();
            if (string.IsNullOrEmpty(disk))
            {
                WarnOnce("noready", "读不到存档里的词书名, 暂不改动词表");
                return false;
            }
            return disk == MyParameters.ChosenBook_Para;
        }

        private static void SaveField(string key, List<string> list)
        {
            if (BookState() != 1) return;
            _managedScopeActive = true;
            if (!BaselineLists.ContainsKey(key))
            {
                BaselineLists[key] = LoadBaselineList(key);
                PersistBaselineList(key, BaselineLists[key]);
                MarkOwned(OwnedListKey, key);
            }
            TouchedListFields.Add(key);
            LogChange(key, list.Count, Sample(list));
            PersistList(key, list);
        }

        private static void SaveField(string key, string[] arr)
        {
            if (BookState() != 1) return;
            _managedScopeActive = true;
            if (!BaselineArrays.ContainsKey(key))
            {
                BaselineArrays[key] = LoadBaselineArray(key);
                PersistBaselineArray(key, BaselineArrays[key]);
                MarkOwned(OwnedArrayKey, key);
            }
            TouchedArrayFields.Add(key);
            LogChange(key, arr.Length, Sample(arr));
            PersistArray(key, arr);
        }

        // 记下「这个字段被插件接管了」, 让下次启动(可能已经是别的词书)知道要还原。
        private static void MarkOwned(string ownedKey, string field)
        {
            try
            {
                List<string> owned = LoadStringList(ownedKey);
                if (owned.Contains(field)) return;
                owned.Add(field);
                ES3.Save(ownedKey, owned);
            }
            catch (Exception e) { WarnOnce("own:" + ownedKey, "写接管记录失败: " + e.Message); }
        }

        private static void ClearOwned()
        {
            try
            {
                ES3.Save(OwnedListKey, new List<string>());
                ES3.Save(OwnedArrayKey, new List<string>());
            }
            catch (Exception e) { WarnOnce("own:clear", "清接管记录失败: " + e.Message); }
        }

        private static List<string> LoadStringList(string key)
        {
            try
            {
                List<string> v = ES3.Load<List<string>>(key, new List<string>());
                return (v == null) ? new List<string>() : v;
            }
            catch (Exception e)
            {
                WarnOnce("ld:" + key, "读取 " + key + " 失败: " + e.Message);
                return new List<string>();
            }
        }

        private static void PersistBaselineList(string key, List<string> value)
        {
            try { ES3.Save(BakPrefix + key, new List<string>(value ?? new List<string>())); }
            catch (Exception e) { WarnOnce("bak:" + key, "写 " + key + " 基线失败: " + e.Message); }
        }

        private static void PersistBaselineArray(string key, string[] value)
        {
            try { ES3.Save(BakPrefix + key, (string[])(value ?? new string[0]).Clone()); }
            catch (Exception e) { WarnOnce("bak:" + key, "写 " + key + " 基线失败: " + e.Message); }
        }

        private static List<string> LoadBaselineListFromDisk(string key)
        {
            try { return ES3.Load<List<string>>(BakPrefix + key, new List<string>()); }
            catch (Exception e)
            {
                WarnOnce("ldbak:" + key, "读取 " + key + " 基线失败: " + e.Message);
                return null;
            }
        }

        private static string[] LoadBaselineArrayFromDisk(string key)
        {
            try { return ES3.Load<string[]>(BakPrefix + key, new string[0]); }
            catch (Exception e)
            {
                WarnOnce("ldbak:" + key, "读取 " + key + " 基线失败: " + e.Message);
                return null;
            }
        }

        private static bool ContainsJapanese(List<string> list)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
                if (LooksJapanese(list[i])) return true;
            return false;
        }

        private static bool ContainsJapanese(string[] arr)
        {
            if (arr == null) return false;
            for (int i = 0; i < arr.Length; i++)
                if (LooksJapanese(arr[i])) return true;
            return false;
        }

        // 改动前的存档值。必须在第一次 Persist 之前取, 取到的才是游戏自己的基线。
        private static List<string> LoadBaselineList(string key)
        {
            try { return ES3.Load<List<string>>(key, new List<string>()); }
            catch (Exception e)
            {
                WarnOnce("base:" + key, "读取 " + key + " 基线失败: " + e.Message);
                return new List<string>();
            }
        }

        private static string[] LoadBaselineArray(string key)
        {
            try { return ES3.Load<string[]>(key, new string[0]); }
            catch (Exception e)
            {
                WarnOnce("base:" + key, "读取 " + key + " 基线失败: " + e.Message);
                return new string[0];
            }
        }

        // 真正落盘: 游戏在多个读点会用 ES3.Load 覆盖内存, 不落盘就压不住残留的旧队列。
        private static void PersistList(string key, List<string> list)
        {
            try { ES3.Save(key, new List<string>(list)); }
            catch (Exception e) { WarnOnce("save:" + key, "写入 " + key + " 失败: " + e.Message); }
        }

        private static void PersistArray(string key, string[] arr)
        {
            try { ES3.Save(key, (string[])arr.Clone()); }
            catch (Exception e) { WarnOnce("save:" + key, "写入 " + key + " 失败: " + e.Message); }
        }

        // 插件现在会落盘, 所以不能再靠「从存档重读」回填(读到的会是插件自己的值)。
        // 用改动前记下的基线把内存和存档一起还原, 这样切到别的词书时, 别的词书看到的是
        // 游戏原本的队列, 插件也不会把自己的筛选结果带过去。
        private static void RestoreSharedFields()
        {
            foreach (string key in TouchedListFields)
            {
                List<string> value;
                if (!BaselineLists.TryGetValue(key, out value) || value == null)
                    value = new List<string>();
                try
                {
                    SetParameterField(key, new List<string>(value));
                    ES3.Save(key, new List<string>(value));
                }
                catch (Exception e) { Warn("恢复 " + key + " 失败: " + e.Message); }
            }
            foreach (string key in TouchedArrayFields)
            {
                string[] value;
                if (!BaselineArrays.TryGetValue(key, out value) || value == null)
                    value = new string[0];
                try
                {
                    SetParameterField(key, (string[])value.Clone());
                    ES3.Save(key, (string[])value.Clone());
                }
                catch (Exception e) { Warn("恢复 " + key + " 失败: " + e.Message); }
            }
            foreach (string key in TouchedIntFields)
                SetParameterField(key, LoadInt(key, 0));
            TouchedListFields.Clear();
            TouchedArrayFields.Clear();
            TouchedIntFields.Clear();
            BaselineLists.Clear();
            BaselineArrays.Clear();
            ClearOwned();
            Log.LogInfo("JPWordList: 已恢复游戏共享队列基线，离开猫条词书后不保留插件状态");
        }

        // ---------------- 跨词书启动守卫 ----------------

        // 场景: 在日语词书里测试到一半直接关掉游戏, 之后用**别的词书**继续那个未完成的测试。
        // 游戏只会照读存档, BookState() 又不是 1, 插件不会介入 -> 别的词书看到日语队列。
        // 插件落盘时留了「接管记录 + 接管前基线」, 这里在启动时把队列还原成接管前的样子。
        //
        // 版本兼容: 只读自己的字符串键, 不碰游戏数据库(wcpOnlyWord.db / wcpFullEng.db),
        // 也不引用游戏内部类型/方法; 对 MyParameters 字段一律走反射 + try/catch。
        // 游戏更新改了字段或库, 这里最坏是「还原失败并记一条日志」, 不会崩、不会写坏存档。
        private static void CrossBookGuard()
        {
            if (_crossBookGuardDone) return;
            if (!IsEnabled()) return;
            if (!BookReady()) return;            // 还没读档: 现在判断不了当前词书, 等下一轮
            _crossBookGuardDone = true;

            if (BookState() == 1) return;        // 当前就是日语词书: Enforce 负责, 不动

            List<string> ownedLists = LoadStringList(OwnedListKey);
            List<string> ownedArrs = LoadStringList(OwnedArrayKey);
            if (ownedLists.Count == 0 && ownedArrs.Count == 0) return;   // 没接管过, 不动

            bool dirty = false;
            for (int i = 0; i < ownedLists.Count; i++)
            {
                string key = ownedLists[i];
                List<string> baseVal = LoadBaselineListFromDisk(key);
                if (baseVal == null || ContainsJapanese(baseVal))
                {
                    // 基线读不到, 或基线本身就是日语(接管期间已在日语语系词书): 回填只会
                    // 把日语队列继续留在别的词书里, 所以清空并结束这个未完成的测试。
                    RestoreList(key, new List<string>());
                    dirty = true;
                }
                else RestoreList(key, baseVal);
            }
            for (int i = 0; i < ownedArrs.Count; i++)
            {
                string key = ownedArrs[i];
                string[] baseVal = LoadBaselineArrayFromDisk(key);
                if (baseVal == null || ContainsJapanese(baseVal))
                {
                    RestoreArray(key, new string[0]);
                    dirty = true;
                }
                else RestoreArray(key, baseVal);
            }

            if (dirty) MarkNoTestInProgress();
            ClearOwned();
            Warn("跨词书守卫: 已还原插件接管前的测试队列" + (dirty ? " (残留日语队列改为清空并结束本轮测试)" : ""));
        }

        private static void RestoreList(string key, List<string> value)
        {
            try
            {
                SetParameterField(key, new List<string>(value));
                ES3.Save(key, new List<string>(value));
            }
            catch (Exception e) { Warn("守卫还原 " + key + " 失败: " + e.Message); }
        }

        private static void RestoreArray(string key, string[] value)
        {
            try
            {
                SetParameterField(key, (string[])value.Clone());
                ES3.Save(key, (string[])value.Clone());
            }
            catch (Exception e) { Warn("守卫还原 " + key + " 失败: " + e.Message); }
        }

        // 清空队列后, 还要把「有测试没做完」的状态也清掉, 否则游戏会拿空队列去继续:
        // MultipleChoiceGeneratorS9.GenerateOptions 在词池 < 5 时会死循环取空表而崩。
        // 这里复刻游戏自己的「本轮完成」状态(SetS8Data/MultipleChoiceGeneratorS9 都这么写),
        // 「继续」按钮会自动变灰, 玩家重新开一次测试即可。
        private static void MarkNoTestInProgress()
        {
            try
            {
                SetParameterField("S8Progress_Para", 0);
                ES3.Save("S8Progress_Para", 0);
                SetParameterField("testingIf_Para", false);
                ES3.Save("testingIf_Para", false);
                SetParameterField("testingIf_CompleteIf", true);
                ES3.Save("testingIf_CompleteIf", true);
            }
            catch (Exception e) { Warn("清测试状态失败: " + e.Message); }
        }

        private static void SetParameterField(string key, object value)
        {
            FieldInfo field = typeof(MyParameters).GetField(key,
                BindingFlags.Public | BindingFlags.Static);
            if (field != null) field.SetValue(null, value);
            else WarnOnce("restore:" + key, "找不到 MyParameters." + key + "，无法恢复该共享字段");
        }

        private static string Sample(List<string> list)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count && i < 4; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(list[i]);
            }
            return sb.ToString();
        }

        private static string Sample(string[] arr)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < arr.Length && i < 4; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(arr[i]);
            }
            return sb.ToString();
        }

        private static void LogChange(string key, int count, string sample)
        {
            string sig = count + "|" + sample;
            string prev;
            if (LastSig.TryGetValue(key, out prev) && prev == sig) return;
            LastSig[key] = sig;
            Log.LogInfo("JPWordList: " + key + " -> " + count + " 词 (示例: " + sample + ") @" +
                (JapaneseBookSelected() ? "jp" : "other"));
        }

        internal static bool JapaneseBookSelected()
        {
            return BookState() == 1;
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
