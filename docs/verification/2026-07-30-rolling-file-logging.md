# 轻量滚动文件日志验收

日期：2026-07-30

## 上游基准与实现

`wetor/AnimeGo@c7475df` 使用 zap + lumberjack 写
`data/log/animego.log`：文件日志只保留 Info 以上，单文件 2 MiB，最多 14
份、最长 14 天。AnimeGoNet 保留这些可观察参数，但沿用已经统一的
`data_path/logs` 目录，因此文件为 `data_path/logs/animego.log`。

实现不引入第三方日志框架：

- `RollingFileLoggerProvider` 直接实现 `ILoggerProvider`，使用 UTF-8 文本；
- Information 及以上与 WebSocket 共用同一个安全格式和脱敏器；
- 写入、flush、数字后缀轮转和保留清理在 provider 内串行；
- 只删除当前日志名后跟正整数的受管备份，不删除相似的其他文件；
- 运行时 I/O 故障关闭该 provider，不让日志故障反向中断业务请求；
- Unix 新日志权限为 `0640`；
- DI 工厂拥有 WebSocket/文件两个 provider，并把 `ILoggerProvider` 解析到
  同一实例；停止和释放宿主后文件句柄已关闭。

## 自动验证

定向测试覆盖：

- Debug 不入文件，Info/EventId 写入；
- URL、password 与 JSON api key 脱敏；
- 小尺寸边界轮转、最多备份数；
- 过期/越界数字备份清理且相似文件保留；
- 100 个并发日志各形成一条完整行；
- 真实 `RunningApp` 写入其独立 `data_path/logs`，宿主释放后目录可删除；
- 文件大小、备份数和保留期边界校验；
- 既有 WebSocket 7 项回归继续通过。

```text
dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj \
  --filter "FullyQualifiedName~RollingFileLoggerProviderTests|FullyQualifiedName~WebSocketLogApiTests"
Passed: 16, Failed: 0
```

完整门禁：

```text
dotnet test AnimeGoNet.slnx --no-restore --no-build
Plugin abstractions: 11 passed
Core: 264 passed
Data: 146 passed
App: 448 passed
Total: 869 passed, 0 failed

dotnet build AnimeGoNet.slnx --configuration Release --no-restore
0 warnings, 0 errors
```

定向 formatter 覆盖本模块 C# 文件并通过，`git diff --check` 通过。

## NativeAOT

`win-x64` Release 使用 `PublishAot=true` 成功生成原生代码，没有 trim/AOT
warning。更新后的 `eng/smoke-native.ps1` 验证原生进程在随机隔离
`data_path/logs/animego.log` 写入非空内容，并继续通过 schema v29、SQLite、
安全 ingest、静态 WebUI 和 WebSocket control smoke。

文件日志加入后，Windows 强制终止进程到句柄实际释放之间出现了可观察的短暂
窗口；smoke finally 现明确等待进程退出后再删除隔离目录。修正后完整 smoke
与清理均通过，不遗留原生进程或临时目录。
