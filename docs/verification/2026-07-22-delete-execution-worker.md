# 可恢复删除执行器（2026-07-22）

## 安全语义

- qBittorrent 任务目标先执行，并固定调用 `DeleteAsync(hash, deleteFiles: false)`；qB 无权代替主程序删除源文件。
- 下载源和媒体库文件是两类独立目标，只删除计划中冻结的绝对文件；路径必须位于任务捕获的 download/save root 内。
- 文件执行拒绝根目录、目录目标、越界和符号链接/重解析点穿越，不递归删除目录，也不删除空父目录。
- 文件不存在视为幂等 `skipped`，支持进程在实际删除后、SQLite 确认前崩溃再执行。
- 业务记录最后执行；completion 删除时 alias 由外键级联清理，并同步删除该 TMDB Series/Season/Episode 的 `completed` claim，使这一集可重新导入且不影响其他 EP。
- 执行使用五分钟 SQLite 租约；过期租约恢复为 pending。失败项目记录稳定错误码并在 30 秒后继续，已完成项目不会重做。

## 验收

```powershell
dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj --no-restore
```

结果：112/112 通过。新增用例使用 fake qB 和临时文件，验证四类组合成功、`deleteFiles=false`、精确文件删除、根目录/越界/目录拒绝、缺失文件幂等，以及 qB 失败时后续文件与业务记录完全保留。未连接本机 qBittorrent，未触碰 `TestSpace`。

全量 Release 回归为 Core 99 + Data 50 + App 112 = 261 项通过。`win-x64`、`.NET 10`、`PublishAot=true` 发布成功，编译和 NativeAOT 分析均为 0 warning/0 error。
