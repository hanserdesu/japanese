# WCP《万词破-单词女友》日语词书项目

为 Steam 游戏《万词破 - 单词女友》(WCP-WordGirlfriend, appid 1981560)
自动生成 JLPT 日语分级词书 + 发音音频 + 外接词典。

## 词书机制 (逆向自 Assembly-CSharp.dll)

游戏的自定义词书有两个官方通道，本项目两者都支持:

| 通道 | 文件 | 说明 |
|------|------|------|
| 自定义词书1~4 | `%USERPROFILE%\AppData\LocalLow\WCP\wcp\MyBook.es3` | Easy Save 3 JSON: `SelfBookList1..4`(string[]词表) + `wordDictionary1..4`(词→释义) |
| 外接词库 | `StreamingAssets/wcpOnlyWord.db` 或 persistentDataPath 下 `.xlsx/.db` | SQLite `pron(word,meaning)` 表或 Excel, 游戏内 `SELECT <词列>,<释义列> FROM <表>` 导入 |

发音: 游戏在 `%USERPROFILE%\AppData\LocalLow\WCP\vocabulary` 下找
`<单词>.mp3` / `<单词>.wav`，找不到才回退内置AI(英语向)TTS。
日语词必须生成本地音频，本项目用 edge-tts (ja-JP-NanamiNeural)。

书名: SaveFile.es3 的 `SelfBookName1..4` 键(游戏内改名UI写这里)。

## 游戏内导入契约 (逆向自 SaveSourceType, 2026-09-06 会话B确认)

- **Excel 导入** (`LoadDataAndSaveFromExcel`, NPOI): 读第一个 sheet,
  **从第 0 行开始**, 只读 **A列=单词, B列=释义**(游戏内显示文本);
  C 列以后忽略。**因此 xlsx 不能加表头行** —— 本项目生成的 xlsx 均为
  无表头格式, C/D 列放读音/英文释义仅供人工参考。
- **外接词库** (`testConnect`/`StartSaveDataBase`): 文件须放
  `%USERPROFILE%\AppData\LocalLow\WCP\wcp\`(persistentDataPath),
  游戏内输入文件名; .db 走 `SELECT <词列>,<释义列> FROM <表>`,
  默认兼容 `pron(word, meaning)`; .xls/.xlsx 亦从此目录读取。
- 全部交付文件已复制到 persistentDataPath (见下方清单)。

## Set B-1 主题词书: 惯用句/拟声拟态/四字熟语 (2026-09-06 会话A新增)

| 词书 | 词数 | 词源 | 交付 |
|------|------|------|------|
| 惯用句·谚语 | 533 | IdiomKB 词表(270)+自选扩充, 释义全部重写 | `惯用句谚语.xlsx` + `wcp_setb.db` |
| 拟声拟态 | 638 | node-jp-giongo 词典(1649条筛选), 释义全部重写 | `拟声拟态.xlsx` + 同 db |
| 四字熟语 | 260 | 自选常用词表, 读音经 yoji_df.csv 逐条校验(修正13处) | `四字熟语.xlsx` + 同 db |
| 高级词汇 | 234 | N1+/文语/新闻语自选词表, 已对JLPT主词书去重 | `高级词汇.xlsx` + 同 db |
| 商务敬语 | 636 | 敬语・邮件套话・商务术语自建词表 | `商务敬语.xlsx` + 同 db |

- 词表: `data/topic/{idioms,giongo,yoji}.json` (LLM 精翻: 中文释义+词性+读音)
- 读音 QA: `tools/setb_check_readings.py` — kanjidic2 音训读分解校验
  (容忍促音/连浊/送假名); 1562 汉字词全部过检, 修正 虚栄心・再現手順 等;
  残留 flag 均为約音(お客→おきゃく)・転呼・熟字訓(塩梅/為替/十八番)・
  敬语特殊读音(仰る/召し上がる) 等合法现象, 人工逐条复核无错误
- 高频惯用句86条附日文例句+中文翻译 (惯用句谚语.xlsx E/F列, 游戏忽略,
  供人工学习参考; 源: data/topic/idioms_examples.json)
- 构建: `tools/setb_build.py` → `output/setb_books.json`;
  导入: `tools/setb_import.py` (无表头 A词/B义, 符合游戏契约) →
  `output/import/setb/` + `wcp_setb.db` (pron 1431 词 + setb_all 明细)
- 已复制到 persistentDataPath: 惯用句谚语.xlsx / 拟声拟态.xlsx /
  四字熟语.xlsx / wcp_setb.db
- 发音: `tools/setb_audio.py` (独立 manifest `setb_audio_manifest.json`,
  与 JLPT / topic 音频管线互不干扰)
- 夜间循环: `tools/night_loop.sh` → `setb_worker.py` (每5分钟一轮:
  补音频+重试失败+游戏关闭后改名, 止于 2026-09-07 09:00)

## 槽位规划

| 槽位 | 词书 | 词数 |
|------|------|------|
| 自定义词书一 (JLPT N5+N4) | N5(661) + N4(632) | 1293 |
| 自定义词书二 (JLPT N3) | N3 | 1784 |
| 自定义词书三 (JLPT N2) | N2 | 1791 |
| 自定义词书四 (JLPT N1) | N1 | 3463 |

合计 8331 词, 中文释义覆盖率 100% (en_only=0), 词性标注覆盖率 100%,
读音校正 83 条, 本地日语发音 MP3 8079 条 (0 失败)。

## Set B 专业词书 (2026-09-06 会话B新增)

| 词书 | 词数 | 词源 | 交付 |
|------|------|------|------|
| IT 用语 | 662 | JMdict 3.6.2 field=comp | `IT用语.xlsx` + `wcp_topic.db` |
| 商务日语 | 458 | JMdict 3.6.2 field=bus/finc(+econ/trade) | 同上合并 `wcp_topic.db` |

- 管线: `build_topic_books.py`(提取) -> LLM 精翻
  (`data/translations/topic_zh_*.json`; 翻译文件即保留清单, 未翻译词自动过滤,
  已剔除 COBOL/JIS 博物馆级冷僻词) -> `merge_topic.py --write-import`
  -> `gen_audio_topic.py`(独立 manifest: audio_manifest_topic.json)
- 发音 MP3 与 JLPT 一样放入游戏 vocabulary 目录, 词即文件名。
- `wcp_topic.db`: `pron` 表两书合并(1118 词)可直接作外接词库启用;
  `topic_all` 表含 topic 字段区分两书。
- 读音: JMdict + Bluskyo/Jisho 缓存补全, 汉字词 100% 有读音。
- **四字熟语/拟声拟态/惯用句** 由并发会话A制作 (09:09-09:12 下载词源),
  本会话未涉及, 分工见 `logs/agent_worklog.md`。

### Set B 交付文件 (已复制到游戏 persistentDataPath)

```
%USERPROFILE%\AppData\LocalLow\WCP\wcp\
  ├─ IT用语.xlsx          游戏内导入槽位用 (无表头, A词/B义)
  ├─ 商务日语.xlsx
  ├─ wcp_topic.db         外接词库 (pron 1118 词)
  ├─ 常用汉字.xlsx        常用汉字表 2136 字 (音训+字义)
  ├─ wcp_kanji.db         常用汉字 外接词库 (pron 2136 字)
  ├─ JLPT_N5N4_初级.xlsx  JLPT 四书导入用 (已修正为无表头 2 列格式)
  ├─ JLPT_N3.xlsx / JLPT_N2.xlsx / JLPT_N1.xlsx
  └─ wcp_jlpt.db          JLPT 外接词库 (pron 7922 词)
```

## Set C 常用汉字书 (2026-09-06 会话B新增)

| 词书 | 字数 | 词源 | 交付 |
|------|------|------|------|
| 常用汉字 | 2136 | kanjidic2 (EDRDG, grade 1-8 = 常用汉字表) | `常用汉字.xlsx` + `wcp_kanji.db` |

- 显示格式: `音:ア 訓:つ.ぐ｜亚；次之` (音读取前3/训读取前3, kanjidic2 顺序)
- 2136 字中文核心字义全部由 LLM 逐字精翻 (`data/translations/kanji_zh_*.json`),
  含 JLPT 级别标记 (xlsx F 列: G<grade> N<jlpt>)
- 音频特殊处理: 文件名 = 汉字(如 `愛.mp3`), 内容朗读该字音训读音
  (如 `アイ、いとしい`), 避免单字 TTS 误读音; manifest:
  `audio_manifest_kanji.json`
- 管线: `build_kanji_book.py`(解析 kanjidic2.xml) -> `merge_kanji.py
  --audio`(合并+导入文件+音频)
- 学习价值: 漢検/常用汉字训练、汉字音训掌握, 是 JLPT 词书之外的
  独立维度 (非词而字)。

### 释义质量分层 (优先级从高到低)

1. **LLM 精翻** (`data/translations/zh_llm_refine_00~28.json`, 29 个批次,
   8076 个词键): 全部 8073 个去重词逐词校正的中文释义 + 词性
   (名/动1/动2/动3/イ形/ナ形/副/接/惯/接头/接尾/量/代 等) + 读音校正;
   异体字/熟字训变体 (如 相変わらず/其れ/何時/御免なさい) 单独给出释义与读音
2. Kaishi 1.5k zh-CN 中文释义 (初级命中 2170 条)
3. Google 翻译 gtx 英文释义→中文 (兜底层)

最终 `zh_source` 为 `llm` 的词即经过精翻; 各层统计见
`output/jlpt_books.json` 的 `meta.translation_layers`。

若想换成其他组合，用 `output/import/` 里的官方导入文件在游戏内重新导入即可:
- `JLPT_N5N4_初级.xlsx` / `JLPT_N3.xlsx` / `JLPT_N2.xlsx` / `JLPT_N1.xlsx`
- `wcp_jlpt.db` — 全级别 SQLite 外接词库(`pron` 表 + `jlpt_all` 表)

## 数据源

- [OpenJLPT](https://github.com/evanclan/OpenJLPT) (CC-BY-SA-4.0): 词表主数据
  (单词/读音/英文释义/例句)
- [Kaishi 1.5k zh-CN](https://github.com/maimemo/kaishi-zh-cn): 中文释义(初级)
- [Bluskyo/JLPT_Vocabulary](https://github.com/Bluskyo/JLPT_Vocabulary): 读音交叉验证
- Google 翻译 gtx 端点: 剩余英文释义→中文 (`data/translations/gtx_zh.json`)
- Jisho API: 缺失读音补全 (`data/readings_jisho.json`)
- LLM 精翻批次 (`data/translations/zh_llm_*.json`): 人工质量层, 优先级最高

释义显示格式: 汉字词 `【假名】中文〈词性〉`；假名词直接 `中文`。

## 工具脚本 (tools/)

```
build_books.py          数据源 -> output/jlpt_books.json (重建基础数据)
write_mybook.py         词书写入游戏 MyBook.es3 (自动备份到 MyBook_backups/)
                        --dry-run 预览; --file 指定其他目标
make_import_files.py    生成 output/import/ 的 xlsx + sqlite 外接词库
                        (2026-09-06 修正: 无表头, A列词/B列义, 匹配游戏契约)
build_topic_books.py    JMdict 提取 IT/商务词条 -> output/topic_books.json
merge_topic.py          合并 topic_zh_*.json 精翻 -> topic_books.json
                        --write-import 生成 IT用语.xlsx/商务日语.xlsx/wcp_topic.db
gen_audio_topic.py      Set B 发音MP3 (独立 manifest, 与 JLPT 管线不互斥)
check_topic_keys.py     校验 topic 翻译键与源词表拼写一致
build_kanji_book.py     解析 kanjidic2.xml -> 常用汉字 2136 字骨架
merge_kanji.py          合并 kanji_zh_*.json -> 常用汉字书 + xlsx/db
                        --audio 生成音训朗读 MP3
night_worker.py         夜间工兵(计划任务 WCP_NightWorker 每20分钟): 补音频+
                        重建导入文件+本地词库补丁+恢复默认书名+到期自删除
patch_local_db.py       【关键】日文词/例句灌入游戏本地词库 wcpFullEng.db/
                        wcpOnlyWord.db (游戏内释义例句的真正数据源; 幂等)
gen_audio.py            批量生成发音MP3 -> 游戏 vocabulary 目录 (断点续传)
                        --limit N 分批; --retry-failed 重试失败项
translate_gtx.py        机翻英文释义 (断点续传, data/translations/gtx_zh.json)
fetch_readings.py       Jisho 补读音 (data/readings_jisho.json)
merge_translations.py   合并翻译/读音 -> 重建 jlpt_books.json
                        --write-game 同时重写 MyBook.es3 + 导入文件
make_llm_batch.py       导出下一批 LLM 精翻任务 llm_batch_pending.json
rename_books.py         SaveFile.es3 书名改名 (仅游戏关闭时执行)
auto_worker.py          夜间自动化工人: 音频+机翻+读音+合并+改名一轮跑完
                        (带锁防重入; 进度写 logs/progress.json)
ildump.py               游戏DLL IL 反汇编工具(逆向用)
```

## 已备份

- `MyBook.es3` 原始文件: `%USERPROFILE%\AppData\LocalLow\WCP\wcp\MyBook_backups\`

## 注意事项

- 游戏(经提权的 Steam 启动)运行时会用内存态覆盖 SaveFile.es3，故
  rename_books.py 仅在游戏关闭时生效。
- 含 `/` 等非法文件名字符的词(如 いい/よい)无法生成本地mp3，游戏会回退AI发音。
- MyBook.es3 只在创建词书时被游戏写入，平时只读，直接改是安全的。

## Set B-2 专业主题词书 9 册 (2026-09-06 会话C新增, JMDict 管线)

| 词书 | 词数 | 词源 | 交付 |
|------|------|------|------|
| IT·计算机 | 800 | JMDict field=comp | `主题_IT计算机·例句版.xlsx` + `wcp_themed.db` |
| 医学 | 800 | JMDict field=med/pathol/anat | `主题_医学·例句版.xlsx` + 同 db |
| 商务·经济 | 419 | JMDict field=bus/econ + LLM 增补 118 | `主题_商务日语·例句版.xlsx` + 同 db |
| 法律 | 600 | JMDict field=law | `主题_法律·例句版.xlsx` + 同 db |
| 惯用句 | 683 | JMDict misc=id + LLM 增补 96 | `主题_惯用句·例句版.xlsx` + 同 db |
| 拟声拟态 | 551 | JMDict misc=on-mim + LLM 增补 60 | `主题_拟声拟态·例句版.xlsx` + 同 db |
| 四字熟语 | 500 | JMDict misc=yoji | `主题_四字熟语·例句版.xlsx` + 同 db |
| 敬语 | 76 | LLM 自建 (尊敬/谦让/郑重/商务) | `主题_敬语·例句版.xlsx` + 同 db |
| 谚语·格言 | 67 | LLM 自建 ことわざ | `主题_谚语格言·例句版.xlsx` + 同 db |

- 提取: `tools/extract_themed.py` (过滤 rK/uK/iK 罕用词形、中文式叠词、
  与 JLPT 重叠的转义词; 读音必须匹配所选词形)
- 精翻: 24 批 `data/translations/zh_llm_themed_00~23.json` — 全部 4496 词
  逐词中文释义+词性+日文例句, 0 机翻残留; 人工增补 417 条在
  `data/themed/extra_*.json` (新增 keigo/kotowaza 两主题)
- 校验: `tools/validate_themed.py` (JMDict 字符白名单+简体语料白名单,
  拦截简体字混入日文例句); `tools/themed_check_readings.py`
  (kanjidic2 音训分解校验, 残留 flag 均为熟字训/连浊/约音合法现象)
- 合并: `tools/merge_themed.py` -> `output/themed_books.json`;
  导入: `tools/make_import_files_themed.py` (无表头 A词/B义 契约格式)
- 音频: `gen_audio.py` 已扩展读取 themed_books.json (与 JLPT 共用
  `audio_manifest.json`), 4479 词 MP3 后台生成
- 文件名「主题_·例句版」前缀避免与会话A/B 同名交付互相覆盖

## ⚠️ 游戏内释义/例句机制与书名约束 (2026-09-06 晚 关键逆向)

**为什么之前游戏里"本地暂未收录这个单词":**
1. 学习界面 (DatabaseManagerS8) 查释义/例句用的是 **StreamingAssets/wcpFullEng.db**
   (`pron` 表 + `sentence2` 例句表), 原库只有英文 — 日文词查不到;
2. 词书内置释义字典 (SelfBookMeaningDictionary) **只在书名等于
   「自定义词书一/二/三/四」时才加载** (GetSelfBookMeaningAfterSet::SetSelfBook
   精确匹配), 书名改成别的名字 → 字典不加载 → 全部落本地库。

**修复 (patch_local_db.py, 已执行):**
- 15,812 个日文词条 (JLPT 8331 + 会话A SetB 2301 + 会话B IT/商务 1120 +
  常用汉字 2136 + 会话C 主题 9+1 册, 去重) 写入 wcpFullEng.db 与
  wcpOnlyWord.db 的 `pron` 表 (ukPhonic=[かな]);
- 10,499 条日文例句写入 wcpFullEng.db `sentence2` (格式对齐原库:
  `例句：日本語。（翻译）`);
- 原库备份在 `backups/`; 补丁幂等 (词数增长才重灌), 已挂入夜班工兵;
- **游戏无需重启** — 每次查词现开数据库连接, 下一个词立即生效;
- 模拟游戏 SQL 抽验 (低い/焼け石に水/正規表現/愛) 释义+注音+例句全部命中。

**书名约束:** 四个槽位请保持默认名「自定义词书一~四」(改名会切断内置字典路径;
本地库兜底后改名也能显示释义, 但内置路径响应更快)。曾被夜班改成的
「JLPT N5+N4」等名字, rename_books.py 已改为"恢复默认名", 游戏下次关闭后
自动还原。

## 尚未完成 (下一步)

- **~~书名改名~~**: ❌ 已取消并反转 — 改名会导致游戏不加载词书字典。
  rename_books.py 现在的职责是**恢复默认书名**(游戏关闭时自动执行)。
- **~~四字熟语/拟声拟态/惯用句词书~~**: ✅ 会话A已交付 (Set B-1, 共1431词,
  见上文), 音频由 night_loop 后台续传。
- **JLPT 四书的表头行问题已修正**: 旧版 xlsx 有表头且 B列=读音, 直接导入会
  得到"读音当释义"的错误词书, 现已重新生成并复制到 persistentDataPath。

## 例句全覆盖 (2026-09-11 完成)

全部 15,812 词每词 3 条日文例句 + 中文翻译, 已写入游戏本地库。

- 管线: `build_sentence_jobs.py`(词块 gen_words_XX + 翻译块 tr_chunk_XX)
  → 子代理生产 `work/gen_out_NN_Y.json`(每批100词×3句, 契约见
  `work/AGENT_SPEC.md` + `work/PATCH_SPEC.md`, `gen_pipeline.py check-one`
  逐批校验) → `apply_sentences.py`(合并 master + 落库)
- 旧库已有例句 10,251 句的中文翻译全部补齐 (`work/tr_out_*.json`)
- 最终状态: `gen_pipeline.py summary` = 达标 15812/15812, 剩余批次 0;
  `sentences_master.json` 15,812 词 缺翻译 0 <3条 0;
  wcpFullEng.db sentence2 逐词全查 15,812 词全部 ≥3 条
- master: `data/translations/sentences_master.json` {word: [[ja,zh],...]},
  是 `patch_local_db.py` 灌 sentence2 的数据源 (幂等, 游戏无需重启)
