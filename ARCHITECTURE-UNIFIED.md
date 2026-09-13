# WCP 多语言词书 — 统一接入架构设计（Host / Pack / Strategy）

> 目标：**资源逻辑与接入逻辑解耦**。所有语言共用一套接入逻辑（宿主），
> 每套语言资源（词库/释义/例句/音频）互相隔离、互不读写，只有被选中的词书
> 才由宿主激活。语言词书与游戏本体玩法解耦，语言词书之间也解耦，
> 但全部通过同一套接入逻辑集成。
>
> 状态：**设计稿，未实施**。所有实测数字来自 2026-09-14 现场勘查，命令可复现。
> 作者立场：方向正确，**可以做到**，但做不到"零代码语言包"，且有 3 处
> **物理上无法解耦**，必须承认并显式处理。

---

## §0 结论（先行）

| 判断 | 结论 |
|---|---|
| 目标架构可行性 | **可行**。身份层（`BookProfile`）已经打好地基，缺的是资源路由与策略抽离。 |
| 能否做到"语言包 = 纯数据"？ | **不能**。至少 4 类逻辑是行为而非资源，必须留一个小的策略实现（约 150 行/语言）。 |
| 多语言能否同时共存？ | **资源层面可以；运行态层面不行**。游戏只有一套全局内存单例，同一时刻只有一个语言能接管。 |
| 最深的耦合在哪？ | **往 `wcpFullEng.db` 写语言内容**。这是单文件、单主键（`word`）的表，跨语言同形词会互相覆盖。必须停止写入，改由宿主自服务。 |
| 硬上限 | **4 个自定义词书槽位**。每语言合并成一本书占一槽 → 最多 4 种语言共存。 |

**最高优先级动作**：把例句音频与单词音频目录**按语言私有化**，不再共享
`LocalLow\WCP\vocabulary\` 与 `LocalLow\WCP\wcp\sentence_audio\`。
这是当前唯一能让"新加一种拉丁语系语言就串音"的真实风险归零的改动，
且不需要动游戏数据库、不需要动运行态协议。

---

## §1 现状实测：耦合到底在哪

### 1.1 接入逻辑是 fork，不是复用（实测 diff）

去掉 CRLF 噪声后逐行 diff（`diff --strip-trailing-cr`）：

| 模块 | 源码行数 | JP→FR 差异 | JP→RU 差异 | 复用率 |
|---|---|---|---|---|
| 词表插件 `*WordListMod.cs` | 2,339 | **2,084** | **1,811** | 11% / 23% |
| 例句音频 `SentenceAudio*Mod.cs` | 923 | **732** | **527** | 21% / 43% |
| 书名插件 `BookNameMod.cs` | 693 | **26** | — | **96%** |

- 书名插件基本就是复用品（26 行差异），证明**抽象的边界是成立的** —— 只要接口切对，95% 可以共用。
- 词表插件 80~89% 在重复。四语言 × 三个 DLL = **12 个 DLL、约 11,700 行**，
  其中真正的独立逻辑估计不足 1,500 行。

复算命令：

```bash
diff --strip-trailing-cr \
  D:/Japanese/mod_jp_wordlist/JpWordListMod.cs \
  D:/French/mod_fr_wordlist/FrWordListMod.cs | grep -cE '^[<>]'
```

### 1.2 五处共享资源，三套互相不一致的命名空间

安装器把四语言资源倒进同一批全局位置（`Install-WCP-*.ps1` 实测）：

| # | 共享资源 | 物理位置 | 各语言怎么处理 | 风险 |
|---|---|---|---|---|
| 1 | 单词音频 | `LocalLow\WCP\vocabulary\` | JA 直接倒进游戏原生目录；FR 私有 `fr_word_audio\`；RU 私有 `ru_word_audio\` **再回退 `vocabulary\`** | **高**：JA 污染游戏自己的英语音频目录；三套策略不一致，FR 的隔离能被 RU 的回退绕过 |
| 2 | 例句音频 | `LocalLow\WCP\wcp\sentence_audio\` | 三语言**全共享**，靠 `ExtractJa/ExtractFr/ExtractRu` 的谓词互斥（假名 / 拉丁且非CJK / 西里尔且非拉丁）分开 | **高**：隔离是"逻辑巧合"而非物理保证。新增德语、西班牙语（都是拉丁字母）→ 谓词无法互斥，直接串音 |
| 3 | 词书文件 | `LocalLow\WCP\wcp\*.xlsx` | 同目录，靠文件名区分 | 中：无命名空间，靠约定 |
| 4 | 游戏数据库 | `StreamingAssets\wcpFullEng.db` 的 `pron` / `sentence2` | 各语言按 `word` 主键写入 | **最高**：单文件单主键，跨语言同形词互相覆盖（实测法语 8,116 词中 **1,638 个与英语同形**） |
| 5 | BepInEx 插件 | `BepInEx\plugins\*.dll` | 每语言 3 个 DLL，线性增长 | 低：靠文件名区分 |

对比：**已经做对的**是自愈包 `jp_db_payload` / `fr_db_payload` / `ru_db_payload`
—— 这是全项目唯一一处已经按语言命名空间化的资源。

### 1.3 已经存在的地基（不要推倒）

`mod_book_name/BookProfiles.cs`（93 行）已经实现了目标架构最关键的两件事：

1. **身份层**：`BookProfile { Id, Language, DisplayName, WordCount, Fingerprint }`，
   指纹 = 对排序后的全词形集合做 SHA-256。**一本书在任何槽位都能被认出来**。
2. **负面识别（negative identification）**：注册表 `All[]` **登记全部语言**，
   每个插件只碰自己那一份，其它语言正面识别为"别人的"。
   法语项目注释原文：*"Registering the Japanese book here as well lets the FR plugin
   positively identify (and therefore never touch) it."*

再加上词表插件的 **fail-closed 作用域门**（内存词表 / 槽位 / 落盘书名三者一致才动手，
不一致一律不接管）—— 这是整个架构里最值得保留的设计。

**但有两个问题**：

- **副本漂移**：JA 项目是单份共享（`mod_jp_wordlist/build.cmd` 引用
  `%~dp0..\mod_book_name\BookProfiles.cs`）；FR/RU 项目各带 **3 份拷贝**
  （`mod_book_name/`、`mod_*_wordlist/`、`mod_sentence_audio_*/`）→ 已经开始漂移。
- **注册表与代码一起编译进 DLL**，新增语言要改源码、重编全部 DLL。
  应外置成数据。

### 1.4 槽位：一个被忽略的硬约束

游戏只有 **4 个自定义词书槽位**，前缀「自定义词书一~四」是硬编码字面量
（`BookNameMod.cs` 第 5 行注释、`_slotProfiles[4]` 数组）。
且**改名会切断 `SelfBookMeaningDictionary` 的加载路径** —— 身份键只能读书名，不能改书名。

实测当前占用：日语词书 = **1 本书 1 个槽**（`日语词库(猫条版).xlsx` 7,922 词，
与 `BookProfile("catbar-jlpt-complete", …, 7922, "6d7a51c0…")` 精确对应；
`JLPT_N1/N2/N3/N5N4/IT用语` 是合并前的源片段，不单独占槽）。

**推论：4 槽 = 最多 4 种语言共存。** 每语言的多个来源必须**合并成一本书**。
这不是缺陷，反而是好事：路由键天然就是「一本书 = 一个指纹 = 一种语言」。

---

## §2 目标架构：三层

```
┌─────────────────────────────────────────────────────────────┐
│ L0 宿主  WcpHost.dll        （1 个，语言无关，唯一会碰游戏的地方） │
│  ├ 补丁层：全部 Harmony 补丁点，固定集合，不再随语言增长          │
│  ├ 适配层：MyParameters / 场景类名 / ES3 键的反射封装 + 降级      │
│  ├ 身份层：BookProfile 注册表（外置数据） + SHA-256 指纹          │
│  ├ 作用域门：fail-closed（内存/槽位/落盘三者一致才动手）           │
│  ├ 路由层：ResourceRouter (lang, kind, key) → 物理路径 / 记录     │
│  └ 接管层：TakeoverScope 统一接管-还原协议（运行态串行化）          │
└───────────────┬─────────────────────────────────────────────┘
                │ 只加载 activeLang 的 pack；其它 pack 处于 unmounted
    ┌───────────┴───────────┬───────────┬───────────┐
    ▼                       ▼           ▼           ▼
┌─────────┐          ┌─────────┐  ┌─────────┐  ┌─────────┐
│ L1 ja   │          │ L1 fr   │  │ L1 ru   │  │ L1 de   │
│ Pack    │          │ Pack    │  │ Pack    │  │ Pack    │
│ 纯数据  │          │ 纯数据  │  │ 纯数据  │  │ 纯数据  │
├─────────┤          ├─────────┤  ├─────────┤  ├─────────┤
│manifest │          │manifest │  │manifest │  │manifest │
│books/   │          │books/   │  │books/   │  │books/   │
│db/      │          │db/      │  │db/      │  │db/      │
│audio/   │          │audio/   │  │audio/   │  │audio/   │
└────┬────┘          └────┬────┘  └────┬────┘  └────┬────┘
     ▼                    ▼            ▼            ▼
┌─────────┐          ┌─────────┐  ┌─────────┐  ┌─────────┐
│ L2 ja   │          │ L2 fr   │  │ L2 ru   │  │ L2 de   │
│Strategy │          │Strategy │  │Strategy │  │Strategy │
│ ~150 行 │          │ ~150 行 │  │ ~150 行 │  │ ~150 行 │
└─────────┘          └─────────┘  └─────────┘  └─────────┘
```

### 2.1 L0 宿主：唯一会碰游戏的地方

宿主承担"机制"，与语言无关：

- **补丁层**：合并现有三套插件的补丁点，成为固定集合。
  已知补丁点（实测）：
  - `VocabularyAudioPlayer.PlayWordAudio`（单词音频，三语言都已用，前缀/后缀）
  - `WordChooseButtonS10.SonBookChoose`、`changePreferredSoundS8/S9.*`、
    `ChangeLocalVoiceIf.*`、`SoundLocalManager.*`（书名显示，6 处，`BookNameMod.cs`）
  - `MultipleChoiceGenerator/S9`、`ChooseWordManager.ResetTestListQuick*`、
    `SetInputFieldValueS8.ShowTheWord` 等（题干/选项/队列，`JpWordListMod.cs`）
  - `DatabaseManagerS8`、`S8checkWordMeaning`、`ButtonTextTransfer.OnSearchButtonClick`（查词面板）
  - 家族 7 个场景的 `Awake/Start`（批量挂载点）
- **反射适配层**：所有 `MyParameters` 字段访问走 `GetField/SetParameterField`，
  字段改名只记日志不抛异常（现有 `JpWordListMod` 已实现，提升到宿主）。
- **身份层**：从 `packs/*/manifest.json` 读注册表，不再硬编码进 DLL。
- **作用域门**：现成的 fail-closed 三一致检查，原样保留。
- **路由层**：见 §2.4。
- **接管层**：见 §3.1。

### 2.2 L1 语言包：纯数据，manifest 驱动

```
LocalLow\WCP\packs\
  ja\
    manifest.json
    books\日语词库(猫条版).xlsx
    db\meaning.sqlite         # 释义（原 pron 表）
    db\sentences.json         # 例句 + 中文翻译（原 sentences_master.json）
    db\repair.tsv             # 自愈载荷（原 jp_db_payload）
    audio\word\*.mp3          # 单词音频，按词形命名
    audio\sentence\*.mp3      # 例句音频，按 md5(例句原文) 命名
  fr\ ...
  ru\ ...
  de\ ...
```

`manifest.json` schema（草案）：

```json
{
  "schema": 1,
  "profile_id": "catbar-jlpt-complete",
  "language": "ja",
  "display_name": "日语词库(猫条版)",
  "word_count": 7922,
  "fingerprint_sha256": "6d7a51c0c5d30dd63d8a6e3412bce1cf2419554fb8487cf5c8a205e43ac41663",
  "slot_hint": 1,
  "es3_prefix": "ja",
  "strategy": { "assembly": "WcpPack.Ja.dll", "type": "WcpPack.Ja.JapaneseStrategy" },
  "resources": {
    "books":          ["books/日语词库(猫条版).xlsx"],
    "meaning_db":     "db/meaning.sqlite",
    "sentence_table": "db/sentences.json",
    "repair":         "db/repair.tsv",
    "word_audio":     "audio/word/",
    "sentence_audio": "audio/sentence/"
  },
  "repair_probes": ["全部", "調べる", "噛む"]
}
```

要点：
- `fingerprint_sha256` **与 `word_count` 一起**是权威身份键，来自现有 `BookProfile`。
- `slot_hint` 只是建议；实际槽位由游戏存档决定，宿主按指纹匹配任意槽。
- `es3_prefix` 给运行态键加命名空间（现为全局 `JpWL_*`，多语言会撞车）。

### 2.3 L2 语言策略：约 150 行的接口实现

**这是"不能做到纯数据"的诚实回应。** 以下 4 类必须留代码：

```csharp
public interface ILanguageStrategy
{
    string Language { get; }

    /// 句子层：从游戏渲染出来的文本里切出"可命名的一段"。
    /// 返回 null = 这句话不属于本语言，宿主不得接管其 ▶ 按钮。
    /// JA=ExtractJa  FR=ExtractFr  RU=ExtractRu（谓词即互斥性来源）
    string ExtractSentenceKey(string renderedText);

    /// 单词层：题干 / 选项的显示形。
    /// JA：题干假名化、选项汉字+中文释义；FR/RU：原形直出
    string StemDisplay(string canonicalWord, string meaning);
    string OptionDisplay(string canonicalWord, string meaning);

    /// 单词音频：题干被改写后，用哪个词形去查音频文件。
    /// JA：假名→汉字临时换回；FR/RU：原形
    string AudioLookupForm(string displayedForm, string canonicalWord);

    /// 查词面板：本地释义 / 注音（宿主自服务，不再查游戏 DB）
    bool ProvideMeaning(string word, out string meaning, out string phonic);

    /// 自愈探针词
    IList<string> RepairProbes { get; }
}
```

把 2,084 行的 fork 差异收敛成这个接口 —— 这就是收益的来源。

### 2.4 路由：显式语言标签，取代内容寻址

```
ResourceRouter.Resolve(kind, key):
    activeLang = ActiveBook().Language      // 来自作用域门，失败则返回 None
    switch kind:
      WordAudio      → packs/<activeLang>/audio/word/<key>.mp3
      SentenceAudio  → packs/<activeLang>/audio/sentence/<md5(key)>.mp3
      Meaning        → packs/<activeLang>/db/meaning.sqlite  WHERE word = key
      Sentences      → packs/<activeLang>/db/sentences.json[key]
```

**关键性质**：语言 A 的代码路径里**不存在**语言 B 的路径字符串。
隔离是**文件系统层面的物理保证**，不再依赖"提取器谓词刚好互斥"这种逻辑巧合。
新增一种拉丁语系语言（德语/西班牙语）不会串音。

---

## §3 三处物理上无法解耦的部分（必须显式处理）

### 3.1 运行态是全局单例 → 只能串行，不能并存

游戏只有一套全局内存：

- `MyParameters.allTestWordsS10_Para`（测试词池）
- `S8needToLearnWordList_Para`（题干队列）
- `S9extraStudy_Para`（自选勾选）
- `exmplesentences` 数组 + 例句 ▶ 按钮是**所有词书共用的一套 UI**

所以"多个语言包共存"是**资源的共存**，不是**运行态的共存**。
同一时刻只有一个语言能接管队列。

宿主必须实现统一的 `TakeoverScope`：

```
进入语言 L 的词书时：
  1. 对每个受管字段 (allTestWordsS10_Para, S8needToLearnWordList_Para,
     S9extraStudy_Para, …) 记录 (字段, 原值) 到 ES3 键 "<prefix>_TAKEOVER"
  2. 把字段改成本书的内容
离开 / 切到别的词书时：
  3. 若原值可用 → 还原
  4. 原值不可用（读不到 / 本身就是本语言）→ 清空队列 + 标记本轮测试已完成
     ※ 绝不能给游戏留 < 5 个词的词池，`GenerateOptions` 会越界崩
```

这套协议 **JP 已经实现并踩过全部坑**（`JpWordListMod.cs` 头部注释 K 段，
v1.7.2 / v1.7.3 两次补漏："游戏内切书"与"基线读不到时 ES3 返回空表"）。
**要求：原样提升为宿主级协议，并把 ES3 键加 `es3_prefix` 命名空间** ——
现在是全局 `JpWL_*`，第二个语言接进来必然撞车。

### 3.2 数据库是单文件单主键 → 必须停止写入

`wcpFullEng.db` 的 `pron` / `sentence2` 都以 `word` 为主键。
四语言同时写入 → 跨语言同形词互相覆盖（法语实测 1,638/8,116 与英语同形）。

**否决方案**：按语言分表（`pron_ja` / `pron_fr`）。
理由：游戏侧查询代码不受我们控制，改表名等于全面 patch 游戏的 SQL，脆弱且不可维护。

**采纳方案**：宿主自服务。查词/例句不再从游戏 DB 取，宿主按
`(activeLang, word)` 从 pack 读取，并 patch 查词面板与例句渲染的返回值。
依据：`JpWordListMod.cs` 的 M① 段已经在做这件事的雏形
（"受管词书下改用本书释义，音标位填假名读音"）—— 已经是同一个形状，泛化即可。

副作用：`HealExampleDatabase`（自愈写库）整个机制变成**历史遗留**，可以退役。
顺带消除 2026-09-14 审计发现的**例句音频 386 条契约漂移**问题：
根因是"写进游戏 DB → 游戏渲染文本 → 反推 md5"这条链路里，
中文译文含全角括号时被 `rfind('（')` 误切。停止写 DB 就直接从根上断掉这条链路。

### 3.3 槽位只有 4 个

每语言合并成一本书占一槽。当前 JA=1、FR=1、RU=1 → 用了 3 个。
**接入第 5 种语言时无处可放，必须做取舍**（合并语言 / 卸载旧语言）。
这必须写进接入契约，否则新语言接入时会假设可以无限共存。

另注：`SelfBookMeaningDictionary` 靠**精确书名**匹配，
所以路由不能改书名，只能按名字识别（`BookNameMod` 已正确实现：只改显示、不动落盘值）。

---

## §4 迁移路径（分 4 阶段，每阶段独立验收）

原则：**每阶段都不破坏现有可用状态**，且不依赖后续阶段。

### 阶段 0（已完成）— 身份层
`BookProfile` + SHA-256 指纹 + 负面识别 + fail-closed 门。保留，不动。

### 阶段 1 — 资源命名空间化（低风险，收益最大）
1. `BookProfiles.cs` 收敛为**单份共享**（消除 FR/RU 的 3 副本漂移）。
2. 例句音频：`sentence_audio\` → `packs\<lang>\audio\sentence\`。
3. 单词音频：三语言统一私有 `packs\<lang>\audio\word\`；
   **停止把日语音频倒进游戏原生 `vocabulary\`**。
4. 安装器支持 `--lang <code>`，只写自己的 pack 目录。
5. 迁移脚本：把现有共享目录里的文件按语言分流到各自 pack。

验收：德语包（纯拉丁）与法语包同时安装，法语词书下点例句 ▶ 播放的是法语音频。
**不动游戏 DB，不动运行态协议。**

### 阶段 2 — 抽宿主
1. 新建 `WcpHost.dll`：合并三套补丁点 + 反射适配层。
2. 语言策略实现 `ILanguageStrategy`，编成 `WcpPack.<Lang>.dll`。
3. 注册表外置到 `packs\*/manifest.json`。
4. 保留作用域门与接管协议，接管键加 `es3_prefix`。

验收：删除任一语言包，其余语言功能无损；宿主日志不含任何语言专属字符串。

### 阶段 3 — 切断 DB 依赖（风险最高，需实机验证）
1. 宿主自服务查词面板与例句渲染。
2. `HealExampleDatabase` 退役。
3. 从 `wcpFullEng.db` 移除已注入的语言内容，恢复到干净游戏原版。

验收：干净游戏原版 DB 下，日语词书查词/例句/音频全部正常。

### 阶段 4 — 接入第 5 语言（验证架构）
用一个纯拉丁语系语言（德语）走完整流程，验证：
新增一种语言**不需要修改任何现有语言的代码**，只需要：
`manifest.json` + 数据 + 一个 `ILanguageStrategy` 实现。

---

## §5 验收标准（可执行检查）

| # | 检查 | 命令 / 方法 | 期望 |
|---|---|---|---|
| 1 | 宿主无语言专属字符串 | `grep -iE '假名\|kana\|汉字\|西里尔\|cyrillic\|拉丁\|latin' WcpHost.dll 的源码` | 0 命中（注释除外） |
| 2 | 资源读取域物理隔离 | 静态审查 `ResourceRouter` | 语言 A 代码路径不含语言 B 的路径串 |
| 3 | 策略接口完备 | 编译全部 pack | 每个 pack 只实现 `ILanguageStrategy`，不重写补丁 |
| 4 | 补丁点固定 | 统计宿主 `Harmony.Patch` 调用数 | 不随语言数量增长 |
| 5 | 作用域门 fail-closed | 制造"内存/槽位/落盘"三者不一致 | 一律不接管，零副作用 |
| 6 | 接管键命名空间 | 列出 ES3 键 | 全部带 `<lang>_` 前缀，无全局键 |
| 7 | 槽位压力可见 | 读全部 manifest 的 `slot_hint` | 不重复且 ≤ 4 |
| 8 | 跨语言不串音 | 装 2 种拉丁语系语言，互相切换 | 例句/音频/释义各归各 |
| 9 | 游戏更新可恢复 | 用原版 `wcpFullEng.db` 覆盖 | 全部功能正常（因为已不依赖它） |

---

## §6 否决方案

| 方案 | 否决理由 |
|---|---|
| 按语言分表写游戏 DB（`pron_ja`/`pron_fr`） | 游戏侧 SQL 不受控，等于全面 patch 查询逻辑，脆弱 |
| 语言包做成纯数据、零代码 | 4 类行为逻辑无法数据化（§2.3），强行做会变成"配置里塞代码" |
| 多语言运行态并存 | 游戏内存是全局单例，物理上不可能；只能串行 + 接管-还原 |
| 保留共享 `sentence_audio\`，靠提取器谓词互斥 | 逻辑巧合式隔离。新增同字母系统语言即失效（§1.2 #2） |
| 改名区分词书身份 | 会切断 `SelfBookMeaningDictionary` 的加载路径（精确书名匹配） |

---

## §7 改动量估算与收益

| 项 | 现状 | 目标 |
|---|---|---|
| 插件 DLL 数 | 4 语言 × 3 = **12** | 1 宿主 + N 策略（4 语言 = 5） |
| 接入逻辑代码 | ~11,700 行（80% 重复） | ~1,500 行宿主 + 4 × ~150 行策略 |
| 新增一种语言 | 改 3 个插件源码 + 重编全部 DLL | 加数据 + 写 1 个 manifest + 1 个策略类 |
| 资源隔离 | 逻辑巧合（谓词互斥）+ 3 套不一致策略 | 文件系统物理隔离 |
| 游戏 DB 依赖 | 写入 `pron`/`sentence2`，更新即被覆盖 | 不依赖，更新无损 |

---

## §8 未验证边界（不声称）

- 阶段 3（宿主自服务）的实机表现**未验证**。风险最高，需要干净 DB 上跑完整流程。
- 德语/俄语策略的具体差异面**未逐行读完**；差异量只对 JA/FR/RU 三对做了实测 diff。
- 运行态接管协议在多语言间切换的**完整矩阵未验证**（JP 单语言路径已有实机依据）。
- 槽位上限 4 是静态代码结论（`_slotProfiles[4]` + 硬编码字面量），
  **未做"第 5 本书导入"的实机验证**。
- 审计期间 `LocalLow\WCP\wcp\` 目录计数持续变化（19,141→19,196），
  存在第二方进程并发写入，部分计数可能取自不一致时点。

---

## 附录 A：勘查命令

```bash
# fork 差异量
diff --strip-trailing-cr D:/Japanese/mod_jp_wordlist/JpWordListMod.cs \
     D:/French/mod_fr_wordlist/FrWordListMod.cs | grep -cE '^[<>]'

# 补丁点
grep -nE '\[HarmonyPatch|Harmony\.Patch' mod_*/*.cs

# 共享资源目标
grep -nE 'vocabulary|sentence_audio|_db_payload|_word_audio' \
     D:/French/installer/Install-WCP-French.ps1

# 身份注册表
cat mod_book_name/BookProfiles.cs
```

## 附录 B：相关文档

- `IMPLEMENTATION.md` — 复刻契约（当前 fork 架构的完整说明）
- `wcp_wordbooks/AUDIT-2026-09-14.md` — 2026-09-14 审计与核对记录
- `wcp_wordbooks/README.md` — 逆向契约
