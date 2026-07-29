# WebSocket 实时日志验收

日期：2026-07-30

## 已实现行为

- 保留上游 `GET /websocket/log` 路由；非 upgrade 请求继续返回空 `200`。
- 使用既有 access-key 中间件保护 WebSocket。直接
  `X-AnimeGo-Access-Key`、旧 `Access-Key`/`access_key` SHA-256 都与 API
  使用同一验证逻辑。
- 日志继续使用上游 `type=log/count` 文本帧；支持 `pause`、`resume` 和
  `terminate` 文本命令。新增 control ack 不会被只处理 `type=log` 的旧客户端
  误认为日志。
- pause 改为逐连接状态，避免一个管理员暂停所有订阅者。每个连接缓存最新
  1000 条；live outbound channel 最多 256 帧并丢弃最旧，慢客户端不会导致
  无界内存增长。
- 日志统一为 UTC、级别、category、event id、message 和安全异常摘要。URL
  path/query、Bearer、Cookie、Authorization、password、passkey、api key、
  access key 与 token 在 fan-out 前脱敏；单行上限 2048 字符。
- WebUI 只用 `textContent` 渲染，保留最新 500 条，提供最低级别过滤、
  pause/resume、清空、手动重连和 1～30 秒指数退避自动重连。

## 自动验证

```text
dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj \
  --filter FullyQualifiedName~WebSocketLogApiTests
Passed: 7, Failed: 0

dotnet test AnimeGoNet.slnx --no-restore --no-build
Plugin abstractions: 11 passed
Core: 264 passed
Data: 146 passed
App: 439 passed
Total: 860 passed, 0 failed

npm run web:check
npm run web:build
Passed; regenerated app.js SHA-256 unchanged.
```

定向 `dotnet format --verify-no-changes` 覆盖本模块全部 C# 文件并通过；
`git diff --check` 通过。

## NativeAOT

`win-x64` Release 使用 `PublishAot=true` 成功生成原生代码，没有 trim/AOT
warning。更新后的 `eng/smoke-native.ps1` 从发布后可执行文件完成 WebSocket
upgrade，发送 `{"action":"pause"}` 并验证 control success 帧；既有 `/ping`、
schema v29、SQLite、安全 ingest、qB capability 和静态 WebUI smoke 同时通过。

## 隔离浏览器验收

从本次 NativeAOT 产物启动独立端口、独立 data/download/save 目录并关闭
后台 worker：

1. 页面自动连接，实时日志区域显示 connected，初始 pause 按钮可用。
2. 点击 pause 后状态变为“已暂停”，页面计数保持不变；另发三个只读 status
   请求只进入服务端缓存。
3. 点击 resume 后恢复 connected，缓存帧补发且页面计数增加。
4. 最低级别切换到 Error 后只显示符合级别的条目。
5. 点击“重新连接”后立即恢复 connected。
6. 浏览器 console 没有 warning/error。

验收未访问主预览实例、真实 qBittorrent 或 TestSpace；隔离 tab、NativeAOT
进程和临时目录均已清理。
