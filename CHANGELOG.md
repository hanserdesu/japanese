# 更新日志

## 2026-09-23

### 日语独立安装包 v1.2.9.2 / 同步统一安装器 v0.1.9

- 独立安装包更新日语宿主与策略程序集：小游戏的单词发音经当前词书的私有音频路由；日语释义、假名和词形优先由包内 `meaning.sqlite` 解析。沿用现有音频资源，不重复上传大音频包。
- 多语言统一安装器 v0.1.9 内置 `wcp-mods-v1.3.2`，其中共享宿主会在战斗出题前核对存档词书与原生槽位指纹，恢复正确词表并隔离异语言旧队列。此修复适用于九种受管语言，但尚未完成逐语种实机验收。
- 独立日语包、统一安装器及相应索引分别校验了 ZIP 完整性、包内 DLL、版本和 SHA-256。独立日语宿主编译、策略加载及音频兼容离线测试通过；当前本机存档的四个原生槽位均不是日语，因此日语仓库注册表测试的现场槽位匹配部分不适用。

### 此前同步统一安装器 v0.1.8

- 日语词库（`ja`）已接入 WCP 多语言统一安装器，可从安装列表中选择安装或更新。
- 更新首页下载指引：已有 BepInEx 的用户可使用[统一安装器 v0.1.8](https://github.com/hanserdesu/WCP-MultiLanguage/releases/tag/wcp-installer-v0.1.8)；首次安装且没有 BepInEx 的用户仍可使用日语独立安装包 v1.2.9.1。
- 统一安装器下载与 ZIP 解压时显示百分比和已处理 / 总字节数；未知 HTTP 总长度时使用资源目录登记的大小，阶段结束显示 `100%`。
- v0.1.8 发布验证：Windows PowerShell 5.1 下安装器测试 118/118、自更新测试 10/10、双击入口测试 5/5 通过。
- 日语词库资源仍由本仓库发布：核心包 [wcp-jp-resources-v1.1.1](https://github.com/hanserdesu/japanese/releases/tag/wcp-jp-resources-v1.1.1)，词音及例句音频 [wcp-jp-resources-v1.0.0](https://github.com/hanserdesu/japanese/releases/tag/wcp-jp-resources-v1.0.0)。
