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
