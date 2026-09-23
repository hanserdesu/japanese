# WCP《万词破-单词女友》日语词书

这是一个给 Steam 游戏《万词破-单词女友》（WCP-WordGirlfriend）增加日语学习功能的 mod 项目。

它把日语词书、中文释义、读音、例句和音频接入游戏，让万词破也可以用来学习日语。

> **统一安装器已发布：** 多语言一键安装器 [v0.1.8](https://github.com/hanserdesu/WCP-MultiLanguage/releases/tag/wcp-installer-v0.1.8) 已支持安装和更新日语词书（`ja`）。安装时下载和解压会显示百分比及已处理 / 总字节数。日语核心资源仍由本仓库发布，当前版本为 [wcp-jp-resources-v1.1.1](https://github.com/hanserdesu/japanese/releases/tag/wcp-jp-resources-v1.1.1)。版本变化见[更新日志](CHANGELOG.md)。

> 多语言安装器和统一宿主由 [hanserdesu/WCP-MultiLanguage](https://github.com/hanserdesu/WCP-MultiLanguage) 发布；本仓库继续托管日语词书与资源。首次安装时如果游戏尚未安装 BepInEx，请使用下方的日语独立安装包；已有可用 BepInEx 的用户可以直接使用统一安装器。

## 普通用户怎么安装

### 已安装 BepInEx：使用统一安装器

1. 下载 [WCP 多语言一键安装器 v0.1.8](https://github.com/hanserdesu/WCP-MultiLanguage/releases/download/wcp-installer-v0.1.8/WCP-Wordbooks-OneClick-Installer-wcp-installer-v0.1.8.zip)，也可先查看[发布说明](https://github.com/hanserdesu/WCP-MultiLanguage/releases/tag/wcp-installer-v0.1.8)。
2. 解压后，在游戏已安装的前提下运行 `一键安装词书.cmd`。
3. 从列表选择 `日语词库(猫条版)`（`ja`），确认安装；更新时运行同目录的 `更新词书资源.cmd`。
4. 等待安装完成后重启游戏。下载和 ZIP 解压期间会持续显示百分比与已处理 / 总字节数，阶段完成时显示 `100%`。

统一安装器会把通用插件安装到游戏的 `BepInEx` 目录，但不负责首次安装 BepInEx 框架。

### 尚未安装 BepInEx：使用日语独立安装包

1. 下载 [WCP 日语词书一键安装包 v1.2.9.1](https://github.com/hanserdesu/japanese/releases/download/wcp-jp-v1.2.9.1/WCP-Japanese-OneClick-Installer-wcp-jp-v1.2.9.1.zip)（SHA-256 `b8cd38fd2c64a35c7469da8c8ca5347a1b77112ed9661120b1ff3d718956c9a5`，发布页见 [wcp-jp-v1.2.9.1](https://github.com/hanserdesu/japanese/releases/tag/wcp-jp-v1.2.9.1)）。
2. 解压安装包，关闭游戏，双击最外层的 `01_双击运行我.cmd`，不要进入 `support` 文件夹。
3. 等待安装器完成，然后重启游戏。该独立安装包附带 BepInEx 5.4.23.5 和 Doorstop；它与统一安装器 v0.1.8 属于不同发布线。

日语独立安装包 v1.2.9.1 相比 v1.2.1 的改进（含 v1.2.5 至 v1.2.9 的更新与性能优化）：

- **修复「部分机器上单词发音变成英语 AI 语音」**：单词音频除语言包外同步补进游戏原生目录，接管插件未生效（刚进游戏读档中 / 装完未重启 / 使用非受管词书）时也能播放本地日语发音；已安装的用户只需运行一次游戏，插件会自动补齐缺失音频，无需重装。
- 发音词形查不到时按写法差异（全角/半角、大小写、空格、句点）自动重试，并在日志中记录未命中原因，便于定位。
- 长时间游玩的内存占用更稳定（音频缓存加上限、离开词书自动释放）。
- 安装器的下载缓存/备份/解压临时目录移到 Steam 云同步范围之外（`LocalLow\WCP\jpmod_data`）：此前它们堆在 `wcp` 目录里被云同步反复扫描，把配额撑爆导致关键存档排不进同步队列。
- 解压/写入失败时显示具体原因（哪个文件、什么错误），不再只报「发生一个或多个错误」。
- 解压与文件写入遇到临时占用（最常见为杀毒软件实时扫描锁定音频文件）时自动重试，显著降低安装失败率。
- 安装失败时自动打开预填好的 GitHub Issue 反馈页面（已附完整错误链和日志），核对后点击 Submit 即可完成反馈。
- 自动清理旧版本遗留的过期下载缓存与旧备份目录；备份词书时不再复制音频，重装不再反复占用 1～2 GB 磁盘。
- **自更新**：启动时自动检查新版本并提示一键升级（下载带 SHA-256 校验，失败自动回退，不影响安装）。

日语独立安装器会保留 CMD 窗口，显示下载和解压进度、容量与速度，并使用多线程处理；它会检查磁盘空间、搜索 Steam 游戏库，并在条件不满足时显示原因。以上特性描述的是 v1.2.9.1 独立安装包；统一安装器 v0.1.8 的进度功能与发布测试结果见[其发布说明](https://github.com/hanserdesu/WCP-MultiLanguage/releases/tag/wcp-installer-v0.1.8)。

## 日语词库包含什么

- JLPT N5～N1 完整日语词库，共 7922 词。
- 中文释义、日语读音、单词音频。
- 查词例句、中文翻译和例句音频。
- IT 用语、商务日语、常用汉字、惯用句、拟声拟态、四字熟语等扩展词书。
- 日语词书、数据库修复和例句播放插件。

日语资源由本仓库发布。统一安装器按目录中的资源地址下载并校验 SHA-256；当前日语核心包为 [wcp-jp-resources-v1.1.1](https://github.com/hanserdesu/japanese/releases/tag/wcp-jp-resources-v1.1.1)，词音和例句音频沿用 [wcp-jp-resources-v1.0.0](https://github.com/hanserdesu/japanese/releases/tag/wcp-jp-resources-v1.0.0)。日语独立安装包 v1.2.9.1 也可通过 Gitee 分片路由获取音频，失败时切换到 GitHub；使用统一安装器的用户只需下载多语言安装器主 Release。

## 遇到安装失败

日语独立安装包 v1.2.5 起提供以下反馈通道：

1. 安装器会先自动重试一次（多数杀毒软件临时锁文件的场景能直接恢复）。
2. 重试仍失败时，窗口会显示具体是哪个文件、什么错误，并给出针对性建议（如加入杀软白名单）。
3. 同时会自动打开浏览器，呈现一个**已填好全部诊断信息的 GitHub Issue**（含安装器版本、系统环境、完整错误链和日志），核对后点击 Submit 即可。若浏览器没有打开，请把 `%USERPROFILE%\AppData\LocalLow\WCP\wcp\installer-error.log` 的内容发到 [Issues](https://github.com/hanserdesu/japanese/issues)。

## 会不会影响英语词库？

日语词书写入游戏的自定义词书槽，正常情况下不会替换官方英语词库。安装器会自动备份已有词书存档、插件和旧 BepInEx 文件（备份位于 `%USERPROFILE%\AppData\LocalLow\WCP\wcp\jpmod_backups`，自动保留最近 3 份；音频不进备份，因为每次安装都会重新下载解压）。

如果四个自定义词书槽都已占用，安装器会提示选择要替换的槽位。

已有其他 BepInEx mod 的用户会保留现有 BepInEx 核心，并只安装本项目自己的插件。

## 安装前需要

- 已安装 Steam 版《万词破-单词女友》游戏本体。
- Windows 系统。
- 安装时保持网络畅通。日语音频约 2.4 GB；统一安装器会在开始前计算并显示所需峰值空间，日语独立安装包 v1.2.9.1 建议在用户数据盘预留至少 10 GiB。
- 安装前完全退出游戏。
- **建议**：把游戏目录和 `%USERPROFILE%\AppData\LocalLow\WCP` 加入杀毒/安全软件白名单（或安装时暂时退出杀软），可避免实时扫描锁定上万个小音频文件导致安装失败。

## 常见问题

**找不到游戏目录：** 确认游戏已安装并至少启动过一次；如果存在多个游戏目录，安装器会让你选择。

**音频下载失败：** 使用统一安装器时，查看窗口中的资源名和错误信息，检查网络与磁盘空间后重试。日语独立安装包会在 Gitee / GitHub 路由间切换，并在 `%USERPROFILE%\AppData\LocalLow\WCP\wcp\jpmod_downloads` 中保留可复用的下载缓存。

**日语独立安装包提示「发生一个或多个错误」：** 请升级到 v1.2.7 或更新版本。v1.2.6 及更早版本可能隐藏内层错误原因；v1.2.6 起会显示具体错误并在失败时自动打开反馈页面。

**游戏启动后没有插件效果：** 确认游戏已完全退出后再安装，并检查游戏目录中是否存在 `BepInEx` 文件夹。安装器运行结束后要重新启动游戏。

**使用日语独立安装包后想恢复原来的词书：** 备份位于 `%USERPROFILE%\AppData\LocalLow\WCP\wcp\jpmod_backups`，自动保留最近 3 份。

## 开发者资料

详细的词书格式、数据库结构、数据来源和构建脚本见 [wcp_wordbooks/README.md](wcp_wordbooks/README.md)。

## 许可

本仓库采用分层许可：代码与内容各自适用不同条款，范围说明与上游署名见 [ATTRIBUTION.md](ATTRIBUTION.md)。

- 代码（宿主、插件、安装器、工具）：[PolyForm Noncommercial License 1.0.0](LICENSE)。个人学习、研究与其它非商业用途可以自由使用、修改和再分发；商业用途请联系作者另行授权。
- 内容（中文释义、例句、翻译、语音与分册编排）：CC BY-NC 4.0。使用时需署名，不得用于商业目的，不得对他人施加额外限制。
- 上游词表与读音（JLPT 取词与分级、假名读音、汉字音训读等由第三方数据集派生的部分，来源见 ATTRIBUTION.md）：继续按上游 CC BY-SA 4.0 提供，需保留署名，衍生数据以相同方式共享；整包因为包含上一条的内容，整体不可商用。

本仓库已冻结，后续开发在 [hanserdesu/WCP-MultiLanguage](https://github.com/hanserdesu/WCP-MultiLanguage)，适用同一套许可。
