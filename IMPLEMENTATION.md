# WCP 多语言词书扩展 —— 可复刻实现文档

> 目标读者：未来要把同一套实现逻辑复刻到其他语言（韩语/法语/西语/…）的开发者（或智能体）。
> 本文以日语实现为工作样本，把「游戏机制契约」（语言无关）与「语言相关适配点」分开讲清。
> 上一手逆向记录见 `wcp_wordbooks/README.md`；例句/语法生产契约在 `wcp_wordbooks/work/AGENT_SPEC.md`、`PATCH_SPEC.md`、`GRAMMAR_SPEC.md`。

---

## 0. 总体架构：一条数据流水线 + 三个运行时插件 + 两种交付形态

```
数据源 (CSV/XML/JSON)
        │
        ▼
 [阶段1] 词书构建 build_books.py ──┐
        │                          │
        ▼                          ▼
 [阶段2] 释义分层翻译 ──────► output/*_books.json ──┐
        │                 （各语言统一中间格式）   │
        ▼                                         │
 [阶段3] 质量校验（读音/字符白名单/去重）           │
        ▼                                         │
 [阶段4] 例句生产（子代理批量契约 + 逐批校验）     │
        ▼                                         ▼
 [阶段5] 音频生成（edge-tts, manifest 断点续传）  [阶段6] 交付物打包与运行时支持
                                                  │
        ┌─────────────────────────────────────────┴────────────────────────────────────────┐
        ▼                                                                                  ▼
【形态 A：开发者/本机直连】                                                       【形态 B：普通玩家一键分发（GitHub Release v1.2.1）】
• patch_local_db.py 幂等灌本地库                                                 • 01_双击运行我.cmd + Install-WCP-Japanese.ps1（防闪退/多驱动器扫描）
• write_catbar_combined.py 写自定义槽位                                          • WcpEs3Helper (C# 内存编译): 安全解析/反序列化 ES3，防元数据丢失并自愈
• rename_books.py 设外观昵称                                                     • payload: catbar_book.json (7922词单册), additions.db, jp_db_payload/
• 复制 xlsx / .db 导入文件与本地音频                                             • 远程分卷下载 + 多线程并发解压 + Windows 保留文件名（aux/nul）特殊拷贝
        │                                                                                  │
        └─────────────────────────────────────────┬────────────────────────────────────────┘
                                                  ▼
                              游戏运行时（Unity 2020.3 + BepInEx 5.4.23.5）
        ┌─────────────────────────────────────────┼────────────────────────────────────────┐
        ▼                                         ▼                                        ▼
  BookProfiles.cs + BookNameMod            SentenceAudioMod                         JpWordListMod (v1.7.6)
• SHA-256 词集指纹识别 (解耦槽位)         • exmplesentences 0.3s 反射扫描          • 词书绝对隔离与跨词书启动基线还原 (JpWL_*)
• 选书回读拦截 (SonBookChoose 前后临时    • md5(ja).mp3 语音播放                   • 假名题干 + 汉字/中文释义选项出题改造
  还原「自定义词书N」，防游戏按外观名查词崩溃) • 作用域严格 Fail-Closed (非日语词书不碰) • 全小游戏发音假名换回汉字取音 (防回退英文TTS)
                                                                                   • 官方更新覆写 StreamingAssets 后的离线自愈 (jp_db_payload)
```

**核心认知（决定整个项目成败的一条）**：词书文件本身只决定「学哪些词」，
游戏里**查词释义和例句的真正数据源是本地库** `wcpFullEng.db`（原库只有英文）。
只做词书不做灌库 → 游戏内每个词都显示「本地暂未收录这个单词」、例句栏空白。
同时，官方更新会重置本地库，必须有运行时离线自愈机制（`jp_db_payload`）形成闭环。

---

## 1. 游戏机制契约（逆向结论，语言无关，复刻必读）

全部逆向自 `Assembly-CSharp.dll`（工具 `tools/ildump.py` / ILSpy）。

### 1.1 自定义词书槽位（MyBook.es3）

| 项 | 值 |
|---|---|
| 文件 | `%USERPROFILE%\AppData\LocalLow\WCP\wcp\MyBook.es3` |
| 格式 | Easy Save 3 JSON，带 `__type` 类型包装 |
| 槽位 | `SelfBookList1..4`（string[] 词表）+ `wordDictionary1..4`（词→释义） |

ES3 的类型包装字符串（写文件必须精确匹配，游戏靠它反序列化）：

```json
{
  "SelfBookList1": {
    "__type": "System.String[],mscorlib",
    "value": ["単語1", "単語2", ...]
  },
  "wordDictionary1": {
    "__type": "System.Collections.Generic.Dictionary`2[[System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089],[System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]],mscorlib",
    "value": {"単語1": "【かな】释义〈词性〉", ...}
  }
}
```

写入工具：
- 本机开发：`tools/write_mybook.py` 或 `tools/write_catbar_combined.py`（写前自动备份，支持 `--dry-run`）。
- 玩家安装包：安装器内置 C# `WcpEs3Helper`（通过 PowerShell `Add-Type` 编译）。

⚠️ **ES3 序列化致命坑**：PowerShell 原生 `ConvertTo-Json` 会剥除或展平对象的类型元数据（丢掉深层 `__type`），导致游戏读档时反序列化失败，甚至将包含 150+ 字段的 `SaveFile.es3` 破坏成缩水残卷，导致开局剧情（`Initial_PlotDone`）重置、全成就与货币清空。**绝对禁止用简单 JSON 库直接回写 ES3 文件**，必须使用带完整类型保持的定制序列化器。

### 1.2 书名约束与选书回读拦截（核心深坑与解决方案）

- 书单显示名 = `"自定义词书N（" + SelfBookNameN + ")"`，括号内是**昵称/署名位**（存 `SaveFile.es3` 的 `SelfBookName1..4`）。
- **决定「词书内置释义字典是否加载」的是槽位身份**：选书状态 `ChosenBook_Para` 必须是规范槽位名 `自定义词书一~四`；游戏内改名 UI 只写 `SelfBookNameN`，不碰它。
- ⚠️ **选书回读深坑（为什么改按钮文字会崩游戏）**：
  `WordChooseButtonS10.SonBookChoose(int)` 在点击选书时，会**反向读取按钮标签的文字**，取第一个 `（` 之前的部分赋给 `tem_ChosenBook`，再写入 `ChosenBook_Para`！
  如果直接在 UI 上将按钮文本改成了「日语词库(猫条版)」，选书方法读到的书名就成了「日语词库(猫条版)」，与游戏的 `自定义词书N` 校验不匹配，导致选书彻底失效、后续测试词表无法生成。
- **优雅解法（BookNameMod 的 Prefix/Postfix 拦截）**：
  `BookNameMod` 在 `SonBookChoose` 执行前（Prefix）将标签临时换回 `自定义词书N`，执行完毕后（Postfix）再恢复显示为 `日语词库(猫条版)`。既保证了玩家看到的界面美观，又保证了游戏底层逻辑读到的永远是规范槽位名。
- **全局词书指纹识别架构（BookProfiles.cs）**：
  插件不再将逻辑写死在特定槽位（如必须在槽位1），而是提取词书全部词条的 FormC 规范化集合排序后计算 SHA-256 指纹（如猫条版 7922 词指纹为 `6d7a51c0c5d30dd63d8a6e3412bce1cf2419554fb8487cf5c8a205e43ac41663`）。无论是槽位 1、2、3 还是 4，只要词书指纹匹配，插件即自动识别其所属语言与档案，其他语言词书只需扩展 Profile 即可。

### 1.3 游戏内导入通道（SaveSourceType 逆向）

**Excel 导入**（`LoadDataAndSaveFromExcel`, NPOI）：
- 读第一个 sheet，**从第 0 行开始**，只读 **A列=单词、B列=释义**（游戏内显示文本）。
- C 列以后忽略，可放读音/例句等人工参考信息。
- ⚠️ **绝对不能加表头行** —— 加了表头，第一行「单词/释义」两个字符串会被当成词条收进词书。
  这是本项目出过的事故（旧版 xlsx 有表头且 B 列=读音 → 导入出「读音当释义」的废词书）。

**外接词库**（`testConnect`/`StartSaveDataBase`）：
- 文件放 `%USERPROFILE%\AppData\LocalLow\WCP\wcp\`（persistentDataPath），游戏内输文件名。
- `.db` 走 `SELECT <词列>,<释义列> FROM <表>`，默认兼容 `pron(word, meaning)` 表。

### 1.4 本地词库（查词释义/例句的真正数据源）

| 文件 | 用途 | 涉及表 |
|---|---|---|
| `wcp_Data/StreamingAssets/wcpFullEng.db` | 每日学习 + 词典查询（DatabaseManagerS8/S17） | `pron`（释义/音标）、`sentence2`（例句） |
| `wcp_Data/StreamingAssets/wcpOnlyWord.db` | 其他查词界面 | `pron` 同构 |

写入格式（`tools/patch_local_db.py`）：
```
pron:      (word, ukPhonic='[かな]', usPhonic='', meaning=游戏显示释义)
sentence2: (word, '例句：日本語。（中文翻译）')
```
- 例句字符串格式必须对齐原库：**前缀 `例句：` + 日文 + `。（中文）`**，
  例句播放 Mod 靠剥这个前缀提取 ja 语音。
- **灌库幂等**：先 DELETE 同批 word 再 INSERT；探针词（低い/貶す/見積もり）释义
  全命中则跳过；`--force` 强制。
- **写前备份**：按「库文件绝对路径」SHA1 前 6 位做 tag（多副本安装互不覆盖），
  备份在项目 `backups/`。
- **游戏无需重启**：游戏每次查词现开数据库连接，灌完下一个词立即生效。
- ⚠️ **Steam 更新覆写问题与运行时离线自愈（核心落地保障）**：
  Steam「验证游戏完整性」或官方发补丁更新时，会将 `StreamingAssets` 下的 `.db` 还原回纯英文官方库。
  - **开发环境**：重跑 `patch_local_db.py` + `build_grammar_book.py --patch`。
  - **玩家环境**：通过 `export_jp_db_payload.py` 将日文字典导出为 TSV 补丁包（`jp_pron.tsv`、`jp_sentences.tsv`、`jp_only_pron.tsv`），随安装器置于不受 Steam 更新影响的 `%USERPROFILE%\AppData\LocalLow\WCP\wcp\jp_db_payload/` 目录。`JpWordListMod`（v1.7.6+）在游戏启动后后台检查探针词，若检测到官方库已被覆写重置，会自动在后台静默重灌还原，且在写入前自动备份当前 DB 到 `jp_db_payload\backup`。

### 1.5 音频查找契约与两大避坑点

**单词音频**：游戏在 `%USERPROFILE%\AppData\LocalLow\WCP\vocabulary` 找
`<单词文本>.mp3` / `.wav`，找不到才回退内置 AI（英语向）TTS。

⚠️ **避坑点 1：Windows 保留设备名（CON, PRN, AUX, NUL, COM1..9, LPT1..9）**
- 日语词汇中存在罗马音或缩写刚好与 Windows 保留设备名重合的词（如 `aux.mp3`、`nul.mp3`）。
- 使用常规资源解压、资源管理器或 PowerShell `Copy-Item` 操作此类文件时会直接抛出非法参数或拒绝访问异常。
- **解法**：在解压工具与安装器文件拷贝时，针对此类保留文件名统一自动转换加上 `\\?\` 扩展路径前缀（见安装器 `Copy-TreeNet` 与 `ExtendedPathIfNeeded`），并使用底层 .NET `FileStream` 写入。

⚠️ **避坑点 2：假名出题与发音查词冲突（全小游戏假名换回汉字取音）**
- 为了契合学习逻辑，Mod 将题目题干改造为了假名读音（如「ぜんぶ」），但在磁盘上，发音音频是按汉字词形命名的（`全部.mp3`）。
- 玩家在测试界面或各类小游戏中点击发音喇叭时，原版逻辑 `VocabularyAudioPlayer` 会直接拿当前题干的文字去搜 `ぜんぶ.mp3`，导致查词落空并错误地回退到了官方内置英文 AI TTS 朗读假名。
- **解法**：`JpWordListMod`（v1.7.5+）对 `VocabularyAudioPlayer.PlayWordAudio` 实施运行时拦截，在发音前将当前题目的假名临时映射换回对应的汉字形（`ぜんぶ` → `全部`），调用原生发音后再还原。此方案覆盖了 Scene7 战斗机、Scene8 每日复习、Scene9 选词、Scene13 水果忍者、Scene15 棒球、Scene16 自由复习、Scene17 拼写等游戏内的所有小游戏场景。

**例句音频**：`%USERPROFILE%\AppData\LocalLow\WCP\wcp\sentence_audio\<md5(ja)>.mp3`
- 文件名 = `hashlib.md5(ja.encode('utf-8')).hexdigest()`（**小写 hex**），
  与 BepInEx 播放 Mod（`mod_sentence_audio/SentenceAudioMod.cs`）的取音逻辑
  **两端严格一致**，任何一端改动都失效。
- Mod 不做 Harmony 补丁：每 0.3s 反射扫描 `exmplesentences` 文本，剥前缀、按
  md5 命中文件挂 ▶ 按钮；游戏更新导致类型变化时自动降级为不显示。
- 作用域严格 Fail-Closed：Mod 启动与每轮刷新均校验当前内存词表、当前槽位与存档书名是否一致匹配已注册的日语 Profile，任何不一致均不挂载按钮，绝不干扰官方英语词书。

### 1.6 游戏目录解析与多盘符容错

本机曾存在两份游戏副本，早期工具硬编码了没在跑的那份导致白灌。`tools/wcp_paths.py` 统一解析：

```
WCP_GAME_DIR 环境变量 → 注册表 HKCU\Software\Valve\Steam (SteamPath/InstallPath)
  → 各库 libraryfolders.vdf 的 "path" → 已知候选目录
只认「存在 wcp_Data/StreamingAssets/wcpFullEng.db」的那份。
真身验证：%USERPROFILE%\AppData\LocalLow\WCP\wcp\Player.log 第一行 Mono path。
```
- **安装器容错机制**：玩家安装器在遍历 Steam 库目录时，会自动捕获并跳过注册表中已不存在或被拔除的磁盘驱动器，防止因无效路径抛错中断安装；若存在多个有效副本，提供交互选择。

---

## 2. 数据流水线（六阶段，含每阶段约束）

### 阶段 1：词源获取与构建

- 主词源用结构化公开数据（日语用 OpenJLPT CSV + Kaishi 1.5k + Bluskyo 交叉验证；
  主题书用 JMDict field/misc 标签提取；汉字书用 kanjidic2.xml）。
- 统一中间格式：`output/<lang>_books.json`（levels/books 分组，每条
  `{word, reading, meaning, 词性, example_ja, example_zh/en}`）。
- 提取过滤（`extract_themed.py` 的经验）：剔罕用词形标记（rK/uK/iK）、
  转义词、与主词书重复项；**读音必须匹配所选词形**。
- **便携合并单册构建**：为了让玩家零门槛开箱即用，通过 `build_installer_payload.py` / `write_catbar_combined.py` 将 N5～N1 词库按顺序合并去重为完整的 7922 词单册（猫条版完整词库），输出为标准化 `catbar_book.json`。

### 阶段 2：释义分层翻译（质量分层优先级）

```
1. LLM 精翻（data/translations/zh_llm_*.json 批次文件，最高优先级）
2. 社区汉化数据（Kaishi zh-CN 等目标语资源）
3. 机翻兜底（Google gtx 端点，断点续传 → gtx_zh.json）
```
- 合并 `merge_translations.py`：低优先级在前、高优先级在后覆盖；
  键先经 `fix_word` 清洗迁移。
- **翻译文件即保留清单**：未翻译的词自动被过滤（剔除冷僻词的手段）。
- 显示格式契约：`汉字词→【假名】中文〈词性〉`；无汉字词直接 `中文`。
  新语言需设计等价格式（音标/罗马音 + 目标语释义 + 词性）。

### 阶段 3：质量校验

- **读音校验**（`setb_check_readings.py` / `themed_check_readings.py`）：
  kanjidic2 音训读分解校验，容忍促音/连浊/送假名/約音/転呼/熟字訓。
  新语言换成等价规则（如韩语汉字音、法语联诵）——校验目标是「读音与词形自洽」。
- **字符白名单**（`validate_themed.py`）：ja 每字符必须在 JMDict 日语用字白名单内，
  专拦**简体字混入日文**（选→選、气→気 这类），也拦例句里的整词英文漂移。
  新语言需建立自己的目标语言字符白名单。
- 去重：全部词书合并后对主词书去重（`高级词汇` 即按此规则产生）。
- 全量复核 `verify_all.py`：pron 覆盖数、sentence2 每词 ≥3 条、音频覆盖、外接库同步。

### 阶段 4：例句生产（子代理批量契约 —— 本项目最成熟的方法论）

规模参考：15,812 词 × 3 条 + 全部翻译，靠「契约文件 + 逐批校验」的并行生产模式完成。

流程：
1. `build_sentence_jobs.py` 把词表切块：词块 `work/gen_words_NN.json`（每块 100 词）
   + 翻译块 `tr_chunk_XX`。
2. 每个子代理拿一份**契约文档**（`work/AGENT_SPEC.md` + `PATCH_SPEC.md`），
   产出 `work/gen_out_NN_Y.json`（词 → [{ja, zh} × 3]）。
3. **写完立即自检**：`tools/gen_pipeline.py check-one NN Y` → 必须 PASS，
   FAIL 按提示修正重跑；**不许谎报 PASS**（主会话每波结束自己跑 summary 复核，
   子代理的自述不算数）。

硬性校验规则（`gen_pipeline.py` 逐条机检，可整体复用）：
1. 每词恰好 3 条例句
2. ja 以 `。`/`！`/`？` 结尾
3. ja 每字符在目标语言字符白名单内（禁混入简体字/英文漂移）
4. zh 为目标译文，**绝不含目标语言假名**（扫 `[\u3040-\u30ff]`）
5. 同词多条 ja 不得重复
6. **词形必须字面出现在例句中**（原形或去尾词干；活用词用
   `〜ために/〜べきだ/〜ことができる` 等能带出原形的接续）—— 最易 FAIL 的一条
7. JSON 合法 UTF-8，`ensure_ascii=False`
8. 质量红线（机检不到、契约里写死）：自然地道、三句不同场景/搭配/语体、
   长度 10~40 字、翻译通顺不硬译

PATCH 模式：文件已存在但不足 3 条时，只补差额，新 ja 不得与已有重复，
用 `gen_helper.py merge NN Y patch.json` 合并后再 check-one。

### 阶段 5：音频生成

- 引擎 edge-tts（日语 `ja-JP-NanamiNeural`；新语言换对应 voice）。
- `tools/gen_audio.py`（单词，并发 8）/ `gen_sentence_audio.py`（例句，并发 12）：
  - **manifest 断点续传**（按文件存在 + 大小阈值判完成），失败重试 3 次，
    `--limit N` 分批、`--retry-failed` 重试；
  - 多套词书用**独立 manifest**（audio_manifest.json / *_topic / *_kanji /
    setb_audio_manifest.json），互不干扰；
  - 生成量大（5.6 万条例句）时挂后台循环工兵（night_worker/auto_worker，
    带锁防重入，进度写 logs/progress.json）。
- 例句音频命名 md5(ja) 规则见 §1.5，语法词书 927 条讲解例句复用同一规则。

### 阶段 6：交付落库与发布打包

1. **本地开发落库**：
   - `patch_local_db.py`：汇总全部 books.json（按优先级去重合并）→ 灌 wcpFullEng.db + wcpOnlyWord.db 的 pron/sentence2（幂等，见 §1.4）。
     ⚠️ **全量落库要 6+ 分钟，必须后台 `-u` 跑**，前台 2min 超时会被 SIGTERM（单事务未 commit 无损，重跑即可）。
   - `make_import_files.py` / `make_import_files_themed.py`：生成无表头 A词/B义 xlsx + 外接词库 .db（pron 表 + 明细表）→ 复制到 persistentDataPath。
   - `write_catbar_combined.py` / `write_mybook.py`：安全写入槽位 MyBook.es3。
   - `rename_books.py`：写署名昵称（保持槽位身份不变，见 §1.2）。
2. **发布包构建（面向普通玩家）**：
   - `export_jp_db_payload.py`：将已灌好的数据库日文释义和例句导出为 `jp_db_payload/` 纯文本补丁包，供运行时自愈使用。
   - `build_installer_payload.py`：自动从本机提取纯净 BepInEx 运行时核心、编译后的 3 个 Mod DLL、词书文件、`catbar_book.json`（7922词完整指纹单册）与 `additions.db`，组装成结构严谨的 `installer_pkg/WCP日语词书安装包/`。
   - `build_release.py`：生成 GitHub Release 资产（主体一键安装包 Zip、单词音频 Zip、例句音频 Zip），自动生成 SHA-256 校验账本。

---

## 3. 扩展到新语言的适配清单与插件移植指南

### 3.1 词书指纹注册（BookProfiles.cs）

所有行为插件统一依赖 `BookProfiles.cs`。向其他语言扩展时，首要任务是在 `BookProfiles.cs` 中增加对应语言的 `BookProfile`，**无需侵入修改现有日语逻辑**：

```csharp
internal static readonly BookProfile[] All = new BookProfile[]
{
    new BookProfile("catbar-jlpt-complete", Japanese, "日语词库(猫条版)", 7922,
        "6d7a51c0c5d30dd63d8a6e3412bce1cf2419554fb8487cf5c8a205e43ac41663"),
    // 新语言扩展示例（法语）：
    new BookProfile("french-vocab-core", "fr", "法语基础词库", 5000,
        "<按词条 FormC 排序后算出的 SHA-256 指纹>")
};
```

### 3.2 资产与工具复用清单

| 组件 | 说明 |
|---|---|
| MyBook.es3 ES3 写入逻辑 | `write_mybook.py` 及安装器 `WcpEs3Helper`，类型包装串照抄 |
| xlsx 无表头 A/B 列契约 | `make_import_files.py` |
| 灌库幂等框架 | `patch_local_db.py`（探针词换成新语言的词） |
| 游戏目录解析 | `wcp_paths.py` 原样复用 |
| 例句校验框架 | `gen_pipeline.py`（换字符白名单 + 词形匹配规则） |
| 音频 manifest 断点续传 | `gen_audio.py` / `gen_sentence_audio.py`（换 voice） |
| 选书拦截 Mod | `BookNameMod` 原样复用，只认 Profile 显示名 |
| 例句播放 Mod | `SentenceAudioMod` 纯复用（按 md5(例句原语言文本).mp3 取音，语言无关） |
| 离线自愈架构 | `export_jp_db_payload.py` 换数据库提取条件，插件端自愈复用 |
| 安装器框架 | `installer/Install-WCP-Japanese.ps1` + `01_双击运行我.cmd`（修改文案与下载链接即可） |

### 3.3 必须换的部分

| 项 | 换成什么 |
|---|---|
| TTS voice | 目标语言的 edge-tts voice |
| 字符白名单 | 目标语言的字符集（例句校验 + 简繁/变体拦截） |
| 词形出现规则 | 目标语言屈折规则（日语原形字面出现；韩语变音较多；拉丁语系可放宽为去屈折词尾后的词干匹配） |
| 释义显示格式 | 音标体系（IPA/罗马音/谚文）+ 格式模板（如 `【音标】中文〈词性〉`） |
| 词书行为 Mod | `JpWordListMod` 中的日语特异性逻辑（假名题干/汉字选项、假名转汉字取音）换成该语言逻辑（例如法语：题干单词，选项给出中文释义+词性，若涉及重音字符则做特殊匹配） |
| 词源数据 | 对应语言的公开词表 + 目标语翻译资源 |
| 翻译兜底链 | 目标语机翻端点 + LLM 精翻批次 |

### 3.4 新语言接入顺序（照做即可）

1. 建词源 → `build_books.py` 式构建，产出 `<lang>_books.json`。
2. 释义三层翻译链（LLM 精翻批次 → 机翻兜底），跑 `check` 键校验。
3. 搭字符白名单 + 例句契约 → 子代理批量生产例句 → `check-one` 逐批收口。
4. 音频（单词 + 例句 md5 命名，使用对应语言 voice）。
5. `BookProfiles.cs` 注册新语言 Profile 并计算指纹。
6. `patch_local_db.py` 换探针词 → 灌库（后台跑）→ `export_jp_db_payload.py` 导出自愈包。
7. 编译 BepInEx 插件（CSC 一键编译）。
8. 运行 `build_installer_payload.py` 与 `build_release.py` 产出安装包。
9. 游戏内实测抽词验证（释义/注音/例句/发音/跨词书切换/小游戏发音六项）。

---

## 4. 运维约束与已踩过的坑（全量实战避坑手册）

### 4.1 游戏存档与状态机制坑

1. **PowerShell JSON 序列化损坏存档导致开局剧情重置（v1.2.0 恶性事故）**：
   - **原因**：旧版安装器使用 PowerShell 自带的 `ConvertTo-Json` 处理 `SaveFile.es3`，默认行为丢失了 Easy Save 3 所需的深层 `__type` 声明，并将复杂对象简化，导致游戏重新载入时识别为坏档，重置为新玩家（`Initial_PlotDone <= 1`）。
   - **解决**：安装器全部弃用 PowerShell 内置 JSON cmdlet，改用内嵌 C# 源码通过 `Add-Type` 编译定制的 `WcpEs3Helper`，具备严格的 ES3 AST 解析与序列化能力，百分之百保留 `__type` 与原始字段层级；同时增加 `TryRepairSaveFile` 自动自愈历史受损存档。
2. **游戏运行时内存覆盖 SaveFile.es3**：
   - 游戏运行中会维持一份庞大的内存数据，退出时直接整盘覆盖写入 `SaveFile.es3`。外部脚本任何改名、选书或修改存档的操作，**必须且仅在 wcp.exe 完全关闭时执行**，否则不仅修改被顶掉，还极易引发写冲突。
3. **未读档前的编译期默认值覆盖存档（v1.7.0 事故）**：
   - 游戏启动初期，`MyParameters` 静态类中的变量全为编译期默认值（默认英语书「四级大纲词汇」69,313 词）。如果在游戏正式从 ES3 读档完成之前就触发词表过滤和存档回写，这 69,313 个英语词会被误判为“异物”过滤并写回存档，造成不可逆损坏。`JpWordListMod` 必须在确认读档完成（比对内存词书名与存档记录）后才允许进行状态干预。

### 4.2 题干与选项状态不同步坑

1. **快速测试题干与选项严重错位（英语题干配日语选项）**：
   - **原因**：`clickChangeImageSource.StartQuickTest` 会从全局已学词典按等级和上次学习时间重采样生成选项词表 `allTestWordsS10_Para`；然而，题面显示的单词来自 `SetInputFieldValueS8.ShowTheWord`，它直接从存档重新读取 `S8needToLearnWordList_Para[0]`！如果玩家上一轮在英语书，存档里的 `need` 队列还留着上次的英语词，导致题面显示「superiority」，选项却是「全部 / 猪肉 / 破碎」，永远无法答对。
   - **解决**：`JpWordListMod` 介入 `SyncStemQueue`，只要词池一换，题干队列与进度必须同步重置，且必须在游戏读取 `need[0]` 之前完成并同步持久化到 ES3。
2. **词表剔空越界崩溃（ArgumentOutOfRangeException）**：
   - 游戏多选生成器 `MultipleChoiceGeneratorS9` 必须满足 `allTestWordsS10_Para.Count >= 5`（战斗模式需 >= 4）。如果过滤逻辑因跨词书冲突把词表剔空或少于 5 词，游戏会在生成选项时直接抛出 `ArgumentOutOfRangeException` 崩溃卡死。`JpWordListMod` 建立了安全兜底红线：绝不允许回写空表或少于 5 词的队列，若无合法词条则直接将本轮测试标记为已完成。
3. **跨词书切换与残留日语队列（JpWL_* 机制）**：
   - 玩家可能在日语词书中途关闭游戏，下次启动进入英语官方词书继续游戏。如果存档中留存着被 Mod 接管的日语队列，英语书就会崩溃。`JpWordListMod` 在每次改写队列前在 ES3 记录 `JpWL_*` 原始基线，一旦检测到当前不是日语词书，立即安全还原基线。

### 4.3 音频与平台特性坑

1. **Windows 保留文件名冲突（aux.mp3 / nul.mp3）**：
   - 英语和日语的单词音频包中合法包含如 `aux.mp3`、`nul.mp3` 等单词发音。Windows 传统文件系统接口视其为 DOS 硬件设备名拒绝写入。必须在路径前显式补全 `\\?\` 扩展前缀并使用 .NET `FileStream` 操作。
2. **假名出题导致原生发音查询落空**：
   - 假名题干直接传给原生发音接口会搜不到音频（本地音频名为汉字），导致回退英语 AI TTS。`JpWordListMod` 在所有小游戏的音频播放入口实施全局动态映射，播发音前把假名置换为汉字。
3. **PowerShell 5.1 堆叠 BOM 解析失败**：
   - 某些编辑器或工具在重复保存 UTF-8 脚本时会堆叠多个字节序标记（BOM），PowerShell 7 可以宽容处理，但 Windows 10/11 自带的默认 Windows PowerShell 5.1 会抛出语法解析错误。必须确保脚本文件头部只有一个标准的 UTF-8 BOM。
4. **安装器控制台异常关闭与闪退**：
   - 普通用户双击 `.ps1` 常因安全策略、默认关联或执行报错直接闪退，看不到任何日志。因此统一采用 `01_双击运行我.cmd` 入口，固定保留控制台会话，设置专属退出逻辑（右上角 X 关闭，按 Enter 不关闭）。

### 4.4 数据管线与落地坑

1. **例句灌库曾因 rowid 错位写乱 10,251 词**：
   - 早期 `apply_sentences.py` 曾使用自增 `rowid` 进行更新，由于原库数据删除重建导致 rowid 偏移，造成上万条中文翻译写串到别的单词上。目前所有 SQL 操作已全部彻底重构为**基于 `word + 原文` 的复合主键定位更新**，绝对幂等。
2. **Steam 更新覆写 StreamingAssets 本地库**：
   - 游戏安装目录下的所有文件随时可能被 Steam 完整性校验替换还原。解决方案是将持久化补丁包置于用户数据目录 `AppData\LocalLow\WCP\wcp\jp_db_payload/`，配合插件的 `HealExampleDatabase` 实现自愈。

---

## 5. 交付物清单（当前完成度基线）

| 类别 | 内容 |
|---|---|
| 核心单册词书 | 猫条版 JLPT 完整词库（7922 词，指纹 `6d7a51c0c5d30dd63d8a6e3412bce1cf2419554fb8487cf5c8a205e43ac41663`） |
| 扩展与主题词书 | JLPT N5N4/N3/N2/N1 分册 + 主题 9 册 + SetB 5 册 + IT/商务 + 常用汉字 + 语法路线（8640 词条） |
| 本地词库与补丁包 | `wcpFullEng.db`（pron 15,812 词 + sentence2 每词 ≥3 条）+ `jp_db_payload`（自愈离线 TSV 补丁） |
| 音频资源 | 单词发音 15,812 个 MP3 + 例句朗读 56,941 个 MP3（md5 小写 hex 命名） |
| BepInEx 插件集 | `JpWordListMod.dll`（v1.7.6，词表隔离/假名出题/全小游戏发音/自愈）<br>`BookNameMod.dll`（外观名与选书回读拦截）<br>`SentenceAudioMod.dll`（例句朗读播放）<br>（NETFX 4.0 CSC 编译，C# 5 语法，共享 `BookProfiles.cs`） |
| 玩家一键安装包 | `WCP-Japanese-OneClick-Installer-wcp-jp-v1.2.1.zip`：<br>含 `01_双击运行我.cmd`、内置 BepInEx 5.4.23.5 运行时、C# `WcpEs3Helper` 存档修复与安全写入、多驱动器扫描、多线程解压与校验 |

**验收指标**：
- `gen_pipeline.py summary` 达标 15812/15812
- `verify_all.py` 校验全过（pron / sentence2 每词≥3条 / 音频 / 外接库同步）
- 双击一键安装器在全新环境与含旧存档环境下均能无缝完成安装与存档自愈

---

## 附录 A：端到端 Runbook（命令级可执行序列）

工作目录均为 `D:\Japanese\wcp_wordbooks\`；Python 统一使用托管环境
`C:/Users/hanserdesu/.workbuddy/binaries/python/versions/3.13.12/python.exe`（下称 `py`）。
标记 [BG] 的步骤执行时间较长，建议后台运行。

```text
# ── 阶段1 词书构建 ────────────────────────────────────────────
py tools/build_books.py            # data/openjlpt_*.csv + kaishi + bluskyo
                                   #   -> output/jlpt_books.json

# ── 阶段2 翻译分层 ────────────────────────────────────────────
py tools/make_llm_batch.py         # 导出下一批精翻任务 llm_batch_pending.json
py tools/translate_gtx.py          # 机翻兜底 -> data/translations/gtx_zh.json（断点续传）
py tools/fetch_readings.py         # Jisho 补读音 -> data/readings_jisho.json
py tools/merge_translations.py --write-game   # 重建 jlpt_books.json
                                   #   + 重写 MyBook.es3 + 导入文件

# ── 阶段3/4 例句生产（子代理批量） ─────────────────────────────
py tools/build_sentence_jobs.py    # 词表切块 -> work/gen_words_NN.json
py tools/gen_dispatch.py status    # 看总进度
py tools/gen_dispatch.py batch NN Y     # 看某批词表+缺口
py tools/gen_pipeline.py check-one NN Y # 每批写完立即校验，必须 PASS
py tools/gen_helper.py merge NN Y work/patch_NN_Y.json   # PATCH 模式合并后重新 check
py tools/gen_pipeline.py summary   # 主会话复核（子代理自述不算数）
py -u tools/apply_sentences.py     # [BG] 合并 gen_out_* + tr_out_* + 翻译
                                   #   -> sentences_master.json + UPDATE/INSERT sentence2
py -u tools/patch_local_db.py      # [BG] 全部 books.json -> 灌 pron+sentence2

# ── 阶段5 音频 ────────────────────────────────────────────────
py tools/gen_audio.py --limit 400  # 单词 MP3 -> vocabulary/<词>.mp3
py tools/gen_audio.py --retry-failed
py tools/gen_sentence_audio.py --limit 2000   # 例句 -> sentence_audio/<md5(ja)>.mp3

# ── 阶段6 交付与安装包打包 ────────────────────────────────────
py tools/make_import_files.py      # output/import/ 无表头 xlsx + wcp_jlpt.db
py tools/write_catbar_combined.py  # 本地调试：写入 7922 词单册到槽位1并清理槽位2~4
py tools/export_jp_db_payload.py   # 导出本地数据库离线补丁包 -> output/jp_db_payload/
py tools/build_installer_payload.py # 收集 BepInEx 运行时、Mod DLL 与词书装配安装包
py tools/build_release.py --reuse-audio # 构建 GitHub Release 归档安装包与音频分卷

# ── BepInEx 插件编译 ──────────────────────────────────────────
# 在根目录或各 mod 目录下执行 build.cmd（使用 NETFX 4.0 csc 编译）
cmd /c "cd /d D:\Japanese\mod_jp_wordlist && build.cmd"
cmd /c "cd /d D:\Japanese\mod_book_name && build.cmd"
cmd /c "cd /d D:\Japanese\mod_sentence_audio && build.cmd"

# ── Steam 更新还原 .db 之后的本地重灌 ─────────────────────────
py -u tools/patch_local_db.py && py tools/build_grammar_book.py --patch
```

## 附录 B：中间文件 Schema（实测结构）

**`payload/catbar_book.json`**（安装包标准化单册词书）：
```json
{
  "id": "catbar-jlpt-complete",
  "language": "ja",
  "display_name": "日语词库(猫条版)",
  "word_count": 7922,
  "fingerprint_sha256": "6d7a51c0c5d30dd63d8a6e3412bce1cf2419554fb8487cf5c8a205e43ac41663",
  "words": ["単語1", "単語2", "..."],
  "meanings": {
    "単語1": "【かな】释义〈词性〉"
  }
}
```

**`output/jlpt_books.json`**（构建管线主中间形态）：
```json
{"meta": {"sources": [...], "stats": {"n5": {"count":661,"zh":661,"en_only":0}, ...},
          "translation_layers": {...}},
 "levels": {"n5..n1": [{"word","reading","meaning_en","example_ja","example_en",
                         "level","meaning_zh","meaning","zh_source","pos_zh"}]}}
```

**`jp_db_payload/` 离线自愈补丁包**：
- `jp_pron.tsv`: `word \t ukPhonic \t usPhonic \t meaning`
- `jp_sentences.tsv`: `word \t sentences`
- `jp_only_pron.tsv`: `word \t ukPhonic \t usPhonic \t meaning`

## 附录 C：脚本 CLI 速查

| 脚本 | 调用方式 |
|---|---|
| `gen_pipeline.py` | `summary`(默认) / `list` / `check [all]` / `check-one NN Y`；env `GEN_MIN` 可改每词条数 |
| `gen_helper.py` | `plan [N]` / `audit` / `merge NN Y <补丁.json>` |
| `gen_dispatch.py` | `status` / `claim [N]` / `batch NN Y` / `done` |
| `gen_audio.py` | `--limit N` / `--retry-failed` |
| `gen_sentence_audio.py` | `--limit N` |
| `write_catbar_combined.py` | 直接运行，清空槽位2~4并将 7922 词写入槽位1，设定外观名 |
| `export_jp_db_payload.py` | 导出当前游戏库中的日文数据至 `jp_db_payload` |
| `build_installer_payload.py` | `[--skip-audio]` 构建完整的一键安装器工作载荷 |
| `build_release.py` | `[--reuse-audio]` 压缩安装包并产出 Release 清单与哈希校验 |
| `patch_local_db.py` | `[--force]` 全量重灌本地 StreamingAssets 数据库 |

## 附录 D：BepInEx 插件构建

- 编译器：**NETFX 自带 csc**（`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`），
  `/target:library /langversion:5 /optimize+ /codepage:65001 /noconfig /nostdlib+`。
  不使用 dotnet/msbuild，游戏基于 Mono NETFX 运行时，源码刻意保持 C# 5 语法。
- 引用清单：
  `BepInEx\core\BepInEx.dll`、`0Harmony.dll`、`Assembly-CSharp.dll`、`netstandard.dll`、`mscorlib.dll`、`System.dll`、`Mono.Data.Sqlite.dll`（Sqlite 自愈使用）、`UnityEngine.dll`、`UnityEngine.UI.dll`、`Unity.TextMeshPro.dll` 等。
- 共享编译：三个插件的 `build.cmd` 均将根目录下的 `mod_book_name\BookProfiles.cs` 包含进 CSC 编译参数，确保词书指纹算法跨插件绝对一致。

## 附录 E：诚实边界 —— 文档无法 1:1 传递的部分

以下是**文档照抄也复刻不出来**、必须人工/会话内决策的点，列出来避免误估：

1. **LLM 精翻的 prompt 本体没有落盘**。例句/语法有契约文件（AGENT_SPEC 等），但释义精翻是交互式完成的，批任务文件只有词条没有完整 prompt，新语言需要自定精翻要求。
2. **词源人工筛选的判断**。SetB 的惯用句/四字熟语/高级词汇词表人工自选标准的细节散落在会话记录里。
3. **子代理编排策略**（每波代理数、波次复核节奏）是会话级实践，附录 A 固化的是工具链序列。
4. **安装器与自愈实现代码量**：`Install-WCP-Japanese.ps1` 现已接近千行，包含严密的 C# AST 嵌入式解析器、多线程 Zip 会话和异常保护。复刻到新语言时应直接基于该脚本做文案与资产替换，切忌从零凭空重写。

除以上四点外，本文档 + 仓库脚本 + 三份 SPEC 足以支撑新语言 1:1 复刻整条管线与运行时架构。

---

*本文档基于 2026-09-13 项目最新状态（Installer v1.2.1 / JpWordListMod v1.7.6）重新对齐；契约细节以代码与真实运行时行为为准。*
