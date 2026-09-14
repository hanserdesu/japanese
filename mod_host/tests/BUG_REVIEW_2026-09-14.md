# 2026-09-14 bug 复查

本轮检查统一宿主的队列接管/恢复、身份与资源路由、既有日语测试脚本、数据库自愈载荷和安装脚本语法。未执行游戏内切书或完整安装，不能据此断言项目不存在其他 bug。

## 已修复

生产源码 `../TakeoverScope.cs`：

- 首次接管先成功保存基线，再成功保存所有权标记；任一步失败时不修改游戏队列。旧实现先写标记，且忽略保存失败。
- 恢复合并本次会话与磁盘所有权，优先使用内存基线。旧实现完全依赖磁盘，记录丢失会导致漏恢复或清空原队列。
- 恢复只接受预定义的队列字段；缺失备份时不生成空队列，保留待恢复标记。恢复保存失败也保留标记供后续重试。
- 重启后进入同一 profile 时先恢复旧基线，防止把上次过滤后的队列当作原始基线。
- 仅恢复测试词池时检查测试完成标记；恢复其他数组不修改测试状态。

## 验证

`TakeoverScopeTest.cs` 直接编译生产 `TakeoverScope.cs`，仅替换游戏字段和 ES3 存储边界；不复制接管算法，不读写用户存档。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File mod_host/tests/run_takeover_test.ps1
$env:WCP_NO_DEPLOY = '1'
& .\mod_host\build.cmd
& .\mod_host\tests\run_registry_test.cmd
```

- 修改前首批 8 项断言中 6 项失败；修改后扩展为 14 项，全部通过。
- 最终宿主构建和 RegistryTest 通过；四语言身份、资源边界和日语策略实际加载通过。构建保留一个既有 Unity API 弃用警告。
- `portable_scope_test.py`、`quicktest_logic_test.py`、`s9_stale_pool_test.py`、`kana_render_test.py`、`order_setting_test.py`、`word_audio_test.py` 全部报告通过。这些既有脚本含模拟/静态验证，不等同于 Unity 运行时测试。
- `db_payload_roundtrip_test.py` 在临时数据库副本恢复成功：FullEng pron 7,922 条、sentence2 30,894 条、OnlyWord pron 7,922 条，三个探针各一条。
- 安装目录下 5 个 PowerShell 脚本 AST 解析通过；修改路径 `git diff --check` 通过。

## 尚未验证与现存限制

- `arch_check.py` 返回 1：本地宿主 DLL 与部署版本不一致；另提示德语 manifest 未部署。本轮只构建本地 DLL，没有部署或发布安装包。
- RegistryTest 只加载了日语策略，其他三种语言没有可用策略；身份/路由通过不代表这些语言已完成宿主功能迁移。
- 旧 `kana_logic_test.py` 虽返回 0，但读到 8,451 个俄语词、汉字词覆盖为 0；该脚本结果不计入日语功能通过证据。
- 仍需对最终 DLL 执行日语 → 其他词书 → 日语、插件停用、退出再启动的实机验证；语法检查不代表安装器完整下载/安装过程已验证。
