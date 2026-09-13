# WCP 日语词书项目 — 长期记忆

## 关键结论（跨会话必须记住）

- **四个词书槽位必须保持默认书名**「自定义词书一/二/三/四」。改名会切断
  `SelfBookMeaningDictionary` 内置释义字典的加载路径（精确匹配书名）。
  `rename_books.py` 的职责是**恢复默认名**，不是改名。
- **xlsx 导入绝对不能加表头行**。游戏 `LoadDataAndSaveFromExcel` 从第 0 行读，
  A 列=单词、B 列=释义。加表头 → 得到「表头当单词」的错误词书。
- **游戏释义/例句的真正来源是本地库**（`StreamingAssets/wcpFullEng.db` 的
  `pron` + `sentence2` 表），不是词书自带的字典。日文词必须用
  `patch_local_db.py` 灌进去，否则显示「本地暂未收录这个单词」。
  该脚本幂等（探针跳过），游戏无需重启（每次查词现开连接）。
- **sentence2 的合并入口是 `apply_sentences.py`**（读 work/gen_out_* +
  work/tr_out_* + logs/sentence_worklist.tsv → 产出
  `data/translations/sentences_master.json`，patch_local_db 读它灌库）。
  全量落库要 6+ 分钟，**必须后台 -u 跑**，前台 2min 超时会 SIGTERM
  （单事务未 commit 无损，重跑即可）。
- **2026-09-11 例句全覆盖已达成**: 15,812 词 × 3 条 + 全部中文翻译，
  DB 逐词全查通过。只有新增词才需要重走这条管线
  （build_sentence_jobs.py → gen_out 生产 → apply_sentences.py）。
- **项目最重要的文档是** `wcp_wordbooks/README.md`（含全部逆向契约）。
  接手任何工作前先读它。例句生产契约在 `work/AGENT_SPEC.md` +
  `work/PATCH_SPEC.md`。

## 架构基线（2026-09-14 实测，做任何重构前先读）

- **统一接入架构设计在** `D:/Japanese/ARCHITECTURE-UNIFIED.md`（Host / Pack /
  Strategy 三层，状态：设计稿未实施）。审计证据在 `wcp_wordbooks/AUDIT-2026-09-14.md`。
- **复刻成本是 fork 不是复用**（实测 diff）：词表插件复用率 11%、例句 21%、
  **书名插件 96%**。四语言 × 3 = 12 个 DLL ≈ 11,700 行，真实独立逻辑 <1,500 行。
- **五处共享资源**：`BepInEx/plugins/`、`LocalLow/WCP/vocabulary/`、
  `LocalLow/WCP/wcp/sentence_audio/`、`LocalLow/WCP/wcp/*.xlsx`、
  `StreamingAssets/wcpFullEng.db`。仅 `<lang>_db_payload/` 已按语言命名空间化。
- **单词音频策略三语言互不一致**（JA 用游戏 `vocabulary/`、FR 私有、RU 私有+回退）
  → 这是"未解耦"的硬证据。例句音频全共享，隔离靠提取器谓词互斥（逻辑巧合）。
- **`wcpFullEng.db` 是单文件单主键（`word`）**，多语言同写会跨语言覆盖
  （法语 1,638/8,116 与英语同形）。按语言分表不可行 → 正解是宿主自服务。
- **槽位硬上限 4**（`_slotProfiles[4]` + 硬编码「自定义词书一~四」）。
  每语言合并成一本书占一槽 → **最多 4 语言共存**。
  `日语词库(猫条版).xlsx` 7,922 词 = 1 本书 = 1 槽 = 1 指纹；
  `JLPT_N1..N5N4/IT用语` 是合并前源片段，不单独占槽。
- **`BookProfiles.cs` 副本已漂移**：JA 单份共享（`build.cmd` 引
  `..\mod_book_name\BookProfiles.cs`），FR/RU 各 3 份。
- **运行态是全局单例**（`allTestWordsS10_Para` / `S8needToLearnWordList_Para` /
  `exmplesentences`）→ 多语言只能资源共存、运行态串行；
  接管-还原协议（JP 的 v1.7.2/1.7.3）要提升为宿主级，ES3 键加 `<lang>_` 前缀。
- **跨语言复刻契约在 `D:/Japanese/IMPLEMENTATION.md`**（2026-09-14 重写过）。
  三条容易漏的：① 例句音频文件名 = `md5(ExtractJa 剥离结果)`，`ExtractJa` 有 8 步语义
  （含 `\n` 截断、**取最后一个 `（` 切尾**、必须含假名/汉字码点）；② `vocabulary/` 与
  `sentence_audio/` 是**多语言共享**目录，`<lang>_word_audio/`、`<lang>_db_payload/`、
  `<lang>mod_backups/` 是**每语言私有**；③ 拉丁语系与英语同形词多（法语 1,638/8,116），
  共享 `vocabulary/` 会让英/日词书串音 → 必须私有化 + 拦截 `PlayWordAudio`。
- ⚠️ **已知缺陷（未修）：日语侧 386 条例句音频「按钮在但点不响」**。
  根因：中文译文含全角括号 → `ExtractJa` 取最后一个 `（` 切尾 → 剥离结果 ≠ 原文 →
  md5 不匹配（mp3 文件其实存在）。386 条 = 译文含全角括号的全部集合。
  修复方向：例句契约加硬规则「**译文不得含全角括号/换行**」（法语侧已加），
  再重算这批例句的音频文件名。三处现有检查都不覆盖这条（gen_pipeline 只拦假名、
  verify_all 只数条数、音频生成只报写盘成功）。

## 目录约定

- 主战场：`D:/Japanese/wcp_wordbooks/`
- 脚本：`tools/`（45 个）；产物：`output/`（可由脚本重建，不入 git）
- 输入数据：`data/`；中间产物与生成作业：`work/`
- 外部数据源（JMDict/kanjidic2 等，不入 git）也在 `data/`

## 环境

- Python 优先用托管版：
  `C:/Users/hanserdesu/.workbuddy/binaries/python/versions/3.13.12/python.exe`
- 脚本均带 `sys.stdout.reconfigure(encoding='utf-8')`，Windows 控制台中文正常
- 会话级坑（2026-09-11 实测）：PowerShell stdout 捕获可能整个坏掉（连 echo 都
  空）→ 只用 bash；bash PATH 缺 coreutils（ls/head/dirname 挂），需要时先
  `export PATH="/c/Users/hanserdesu/.workbuddy/binaries/PortableGit/versions/1.2.0/usr/bin:$PATH"`

## 工作方式约定

- 生成类任务：写完 **立即** 跑校验（如 `gen_pipeline.py check-one NN Y`），
  不批量攒完再验。
- 并行子代理的 PASS 自述**不算数**，每波结束主会话自己跑 summary 复核；
  子代理偶发重复实例/越界做批次，靠 check_slice 统一收口，无损。
- 不声称跑过的检查没跑过；无法复现的通道要明说「无法复现」，不编造。
