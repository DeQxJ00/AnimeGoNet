# 优雅退出与取消传播验收

日期：2026-07-30

## 行为边界

- ASP.NET Core 宿主停止期限固定为 5 秒。
- 十个后台 HostedService 继续把同一个停止令牌传入调度、SQLite、HTTP、
  qBittorrent、TMDB/Bangumi/AI 和文件处理调用。
- 正在等待 qBittorrent 响应的下载快照 Worker 收到停止令牌后退出。
- Cron 等待、重试和已经触发的插件调用都收到停止令牌，Coordinator 在退出前
  回收已经跟踪的调用。
- 配置保存后的 data-update 热应用不受浏览器断开影响，但受
  `ApplicationStopping` 约束。
- RSS winner 失败后的租约释放可跨越请求取消；如果宿主已经停止，则由持久化
  的 10 分钟租约过期机制恢复，不启动无期限的关机清理。
- 日志 WebSocket 链接请求断开和宿主停止信号，停止时取消收发并发送正常关闭
  输出，长连接不再迫使 Kestrel 等到停止期限。

## 自动测试

定向测试覆盖：

- `HostOptions.ShutdownTimeout` 精确为 5 秒；
- 真实 `RunningApp` 开启全部后台 Worker，用阻塞 fake qBittorrent 证明
  `StopAsync` 取消活动调用；
- 同一宿主上的真实 WebSocket upgrade 在停止时关闭；
- Cron 热增删、等待、重试和两个并发长任务的取消与回收；
- Mikan RSS 和配置 API 回归。

```text
dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj \
  --filter "FullyQualifiedName~GracefulShutdownTests|FullyQualifiedName~WebSocketLogApiTests|FullyQualifiedName~MikanRssIngestProcessorTests|FullyQualifiedName~DataUpdateScheduleManagerTests|FullyQualifiedName~PluginScheduleCoordinatorTests"
Passed: 24, Failed: 0
```

完整 Release 门禁：

```text
dotnet test AnimeGoNet.slnx -c Release --no-restore
Plugin abstractions: 11 passed
Core: 264 passed
Data: 146 passed
App: 450 passed
Total: 871 passed, 0 failed
```

## 发布产物门禁

`eng/smoke-native.ps1` 保留既有 `/ping`、SQLite、静态 WebUI、安全 ingest、
WebSocket 和滚动日志 smoke。所有业务检查成功后：

- Linux 与 macOS 使用 `/bin/kill -TERM`；
- 要求发布进程在 7 秒内退出且退出码为 0；
- 超时后才强制终止，并让 smoke 失败；
- 最后递归删除随机隔离目录，继续验证 SQLite/日志句柄已经释放；
- Windows 当前由宿主测试证明取消传播，NativeAOT smoke 证明强制停止后的句柄
  清理；CTRL+C 仍保留为发布实机门禁。

本机 `win-x64` 使用 .NET 10 NativeAOT 发布成功，无 trim/AOT warning；
更新后的完整 smoke 通过，隔离目录清理成功。Unix SIGTERM 分支由
`animegonet-native-aot.yml` 的 `linux-x64`、`linux-arm64` 和 `osx-arm64`
原生 runner 执行，CI 实机结果返回前不把该项标记为全部完成。
