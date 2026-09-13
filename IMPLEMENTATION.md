# WCP 多语言词书扩展 —— 可复刻实现文档

> 目标读者：未来要把同一套实现逻辑复刻到其他语言（韩语/法语/西语/…）的开发者（或智能体）。
> 本文以日语实现为工作样本，把「游戏机制契约」（语言无关）与「语言相关适配点」分开讲清。
> 上一手逆向记录见 `wcp_wordbooks/README.md`；例句/语法生产契约在 `wcp_wordbooks/work/AGENT_SPEC.md`、`PATCH_SPEC.md`、`GRAMMAR_SPEC.md`。

---

## 0. 总体架构：一条数据流水线 + 四条游戏交付通道

```
数据源(CSV/XML/JSON)                      游戏（Unity, BepInEx 5）
        │                                        ▲
        ▼                                        │ ① MyBook.es3（槽位词表）
 [阶段1] 词书构建 build_books.py ──┐              │ ② xlsx / .db（游戏内导入）
        │                          │              │ ③ wcpFullEng.db（查词释义+例句）
        ▼                          ▼              │ ④ 音频目录（单词+例句）
 [阶段2] 释义分层翻译 ──────► output/*_books.json ─┘
        │                 （各语言的统一中间格式）
        ▼
 [阶段3] 质量校验（读音/字符白名单/去重）
        ▼
 [阶段4] 例句生产（子代理批量契约 + 逐批校验）
        ▼
 [阶段5] 音频生成（edge-tts, manifest 断点续传）
        ▼
 [阶段6] 交付落库（patch_local_db.py 幂等灌库 + 导入文件 + 写槽位）
```

**核心认知（决定整个项目成败的一条）**：词书文件本身只决定「学哪些词」，
游戏里**查词释义和例句的真正数据源是本地库** `wcpFullEng.db`（原库只有英文）。
只做词书不做灌库 → 游戏内每个词都显示「本地暂未收录这个单词」、例句栏空白。

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

写入工具 `tools/write_mybook.py`（写前自动备份到 `MyBook_backups/`，支持 `--dry-run`）。
MyBook.es3 只在创建词书时被游戏写入、平时只读 → **直接改是安全的**。

### 1.2 书名约束（本项目踩过最深的一坑，两轮更正后的最终结论）

- 书单显示名 = `"自定义词书N（" + SelfBookNameN + ")"`，括号内是**昵称/署名位**，
  存 `SaveFile.es3` 的 `SelfBookName1..4`。改昵称安全（`tools/rename_books.py`，
  仅在 wcp.exe 未运行时执行——游戏运行时用内存态覆盖 SaveFile.es3）。
- **决定「词书内置释义字典是否加载」的是槽位身份**：选书状态 `ChosenBook_Para`
  必须是 `自定义词书一~四`；游戏内改名 UI 只写 `SelfBookNameN`，不碰它。
- ⚠️ 四个槽位的书名（`SelfBookListN` 对应的槽位身份）**必须保持默认**
  「自定义词书一/二/三/四」。早期结论「书名必须精确匹配否则字典不加载」针对的
  就是这个；昵称怎么写都不影响。
- 换机 + Steam 云同步可能用云端旧存档覆盖 → 重跑 rename_books.py 即可。

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
- ⚠️ **Steam「验证游戏文件完整性」或游戏更新会把 StreamingAssets 下的 .db
  换回英文原库**。重跑 `patch_local_db.py` + `build_grammar_book.py --patch` 即可。

### 1.5 音频查找契约

**单词音频**：游戏在 `%USERPROFILE%\AppData\LocalLow\WCP\vocabulary` 找
`<单词文本>.mp3` / `.wav`，找不到才回退内置 AI（英语向）TTS。
- 非语言词必须生成本地音频，否则发音是错的。
- ⚠️ 含 `/\:*?"<>|` 等非法文件名字符的词无法生成（如日语「いい/よい」），
  记进 manifest 失败区即可，游戏自动回退。
- 特殊载体（如常用汉字）：文件名=汉字，**内容朗读该字的读音组合**
  （如 `愛.mp3` 读「アイ、いとしい」），避免单字 TTS 误读音。

**例句音频**：`%USERPROFILE%\AppData\LocalLow\WCP\wcp\sentence_audio\<md5(ja)>.mp3`
- 文件名 = `hashlib.md5(ja.encode('utf-8')).hexdigest()`（**小写 hex**），
  与 BepInEx 播放 Mod（`mod_sentence_audio/SentenceAudioMod.cs`）的取音逻辑
  **两端严格一致**，任何一端改动都失效。
- Mod 不做 Harmony 补丁：每 0.3s 反射扫描 `exmplesentences` 文本，剥前缀、按
  md5 命中文件挂 ▶ 按钮；游戏更新导致类型变化时自动降级为不显示。

### 1.6 游戏目录解析（多副本坑）

本机曾存在两份游戏副本，工具硬编码了**没在跑的那份**，灌库全白灌。
`tools/wcp_paths.py` 统一解析（所有写库脚本共用，**禁止再硬编码路径**）：

```
WCP_GAME_DIR 环境变量 → 注册表 HKCU\Software\Valve\Steam (SteamPath/InstallPath)
  → 各库 libraryfolders.vdf 的 "path" → 已知候选目录
只认「存在 wcp_Data/StreamingAssets/wcpFullEng.db」的那份。
真身验证：%USERPROFILE%\AppData\LocalLow\WCP\wcp\Player.log 第一行 Mono path。
```

---

## 2. 数据流水线（六阶段，含每阶段约束）

### 阶段 1：词源获取与构建

- 主词源用结构化公开数据（日语用 OpenJLPT CSV + Kaishi 1.5k + Bluskyo 交叉验证；
  主题书用 JMDict field/misc 标签提取；汉字书用 kanjidic2.xml）。
- 统一中间格式：`output/<lang>_books.json`（levels/books 分组，每条
  `{word, reading, meaning, 词性, example_ja, example_zh/en}`）。
- 提取过滤（`extract_themed.py` 的经验）：剔罕用词形标记（rK/uK/iK）、
  转义词、与主词书重复项；**读音必须匹配所选词形**。

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

### 阶段 6：交付落库

1. `patch_local_db.py`：汇总全部 books.json（按优先级去重合并）→
   灌 wcpFullEng.db + wcpOnlyWord.db 的 pron/sentence2（幂等，见 §1.4）。
   ⚠️ **全量落库要 6+ 分钟，必须后台 `-u` 跑**，前台 2min 超时会被 SIGTERM
   （单事务未 commit 无损，重跑即可）。
2. `make_import_files.py` / `make_import_files_themed.py`：生成无表头
   A词/B义 xlsx + 外接词库 .db（pron 表 + 明细表）→ 复制到 persistentDataPath。
3. `write_mybook.py`：写槽位（或让用户游戏内导入，槽位占用时安装器让用户选替换）。
4. `rename_books.py`：写署名昵称（保持槽位身份不变，见 §1.2）。
5. 语法路线类「融合词书」（可选模式）：词按学习路线重排，每块后插语法占位词
   （G001...），**讲解写在占位词的例句栏**（3 行 = 接续/用法/注意），
   契约见 `work/GRAMMAR_SPEC.md`（zh 首词前缀、ja 可朗读、白名单等硬规则同理）。

---

## 3. 扩展到新语言的适配清单

### 不变的部分（纯复用）

| 组件 | 说明 |
|---|---|
| MyBook.es3 ES3 写入逻辑 | `write_mybook.py`，类型包装串照抄 |
| xlsx 无表头 A/B 列契约 | `make_import_files.py` |
| 灌库幂等框架 | `patch_local_db.py`（探针词换成新语言的词） |
| 游戏目录解析 | `wcp_paths.py` 原样复用 |
| 例句校验框架 | `gen_pipeline.py`（换字符白名单 + 词形匹配规则） |
| 音频 manifest 断点续传 | `gen_audio.py` / `gen_sentence_audio.py`（换 voice） |
| 例句播放 Mod | SentenceAudioMod 不用改（它只认 md5(ja文本).mp3，语言无关——
  ja 变成新语言例句文本即可） |
| 安装器 | `wcp_wordbooks/installer/`（PowerShell 一键安装 + Steam 库扫描 + 备份） |

### 必须换的部分

| 项 | 换成什么 |
|---|---|
| TTS voice | 目标语言的 edge-tts voice |
| 字符白名单 | 目标语言的字符集（例句校验 + 简繁/变体拦截） |
| 词形出现规则 | 目标语言屈折规则（日语是「原形/词干字面出现」；韩语近之，
  拉丁语系可放宽为去词尾屈折后的词干匹配） |
| 释义显示格式 | 音标体系（IPA/罗马音/谚文）+ 格式模板 |
| 读音校验器 | 目标语言的读音自洽规则（可先跳过，只保留白名单校验） |
| 词源数据 | 对应语言的公开词表 + 目标语翻译资源 |
| 翻译兜底链 | 目标语机翻端点 + LLM 精翻批次 |

### 新语言接入顺序（照做即可）

1. 建词源 → `build_books.py` 式构建，产出 `<lang>_books.json`。
2. 释义三层翻译链（LLM 精翻批次 → 机翻兜底），跑 `check` 键校验。
3. 搭字符白名单 + 例句契约 → 子代理批量生产例句 → `check-one` 逐批收口。
4. 音频（单词 + 例句 md5 命名）。
5. `patch_local_db.py` 换探针词 → 灌库（后台跑）→ `verify_all.py` 复核。
6. 导入文件 + 写槽位 + 署名 → 游戏内实测抽词（释义/注音/例句/发音四项）。

---

## 4. 运维约束与已踩过的坑（复刻时逐条对照）

**游戏侧**
- Steam 验证完整性/更新会还原 StreamingAssets 的 4 个 .db → 重跑灌库脚本。
- 游戏运行时内存态覆盖 SaveFile.es3 → rename 只在 wcp.exe 关闭时执行。
- persistentDataPath 的词书文件与音频不受 Steam 还原影响。
- 备份点：`MyBook_backups/`（写槽位前）、项目 `backups/`（灌库前，
  按库路径 hash tag）、安装器 `jpmod_backups/`（每次安装带时间戳）。

**数据侧**
- sentence2 曾按旧 worklist 的 rowid UPDATE 导致 10,251 条例句写串到别的词
  （词数条数全对、内容全错）→ 已改为按「词+原句」定位，**幂等且不跨词覆盖**。
  复刻时禁止用易漂移的 rowid 做更新键。
- 外接词库 .db 的 pron 表与游戏内导入 xlsx 保持同源生成，避免两通道数据漂移。

**生产侧**
- 并行子代理的 PASS 自述不算数，主会话每波跑 summary 复核；
  偶发重复实例/越界做批次，靠 check_slice 统一收口，无损。
- 生成类任务写完立即校验，不批量攒完再验。
- 长事务落库后台 `-u` 跑。

**环境侧（本机）**
- Python 用托管版：`C:/Users/hanserdesu/.workbuddy/binaries/python/versions/3.13.12/python.exe`；
  脚本统一带 `sys.stdout.reconfigure(encoding='utf-8')`。
- bash PATH 缺 coreutils：命令前
  `export PATH="/c/Users/hanserdesu/.workbuddy/binaries/PortableGit/versions/1.2.0/usr/bin:$PATH"`。
- 不要用 PowerShell 工具跑脚本（stdout 捕获恒为空），只用 bash。

---

## 5. 交付物清单（日语版现状，作为复刻基线）

| 类别 | 内容 |
|---|---|
| 槽位词书 | 自定义词书一~四 = JLPT N5+N4 / N3 / N2 / N1（8331 词） |
| 扩展词书 | 主题 9 册 + SetB 5 册 + IT/商务 + 常用汉字 + 语法路线（8640 行） |
| 本地库 | wcpFullEng.db pron 15,812 词 + sentence2 每词 ≥3 条（含翻译） |
| 音频 | 单词 MP3 15,812 + 例句语音 56,941（md5 命名） |
| BepInEx 插件 | JpWordListMod（词表防串扰/日语出题）+ SentenceAudioMod（例句朗读）
  + BookNameMod；BepInEx 5.4.23.5，NETFX csc 编译，`build.cmd` 一键构建部署 |
| 安装器 | `installer/` PowerShell：Steam 全库扫描 → BepInEx 引导 → 备份 →
  装插件词书 → 下载音频（2.4GB，断点续传+校验+多线程解压） |

**当前完成度基线**（复刻新语言时对标的验收口径）：
`gen_pipeline.py summary` 达标 15812/15812；`verify_all.py` 四项全过
（pron / sentence2 每词≥3条 / 音频 / 外接库同步）。

---

*本文档基于 2026-09-13 项目状态整理；契约细节以 `wcp_wordbooks/README.md`
与 `work/*_SPEC.md` 的最新版为准，冲突时以代码行为为准。*
