# Mikan RSS 规则 API 与批次预览（2026-07-22）

## 端点

- `GET /api/v1/rss-rules/{sourceProfileId}`：返回两个 SourceProfile 开关、规则 revision、名单、组/数组/values 顺序和更新时间。
- `PUT /api/v1/rss-rules/{sourceProfileId}`：以 `expected_revision` 全快照保存；服务端统一规范 ID/value，冲突返回 409，非法结构返回 400。
- `POST /api/v1/rss-rules/{sourceProfileId}/preview`：使用已保存规则纯计算候选决策，返回名单拒绝、winner/loser、决胜 winner 和实际执行过的 group ID。

preview 要求候选 ID 唯一、title 非空。只有可靠的 `mikanid + source_episode_kind + source_episode` 会分组；不可靠条目旁路，不猜测。`rss_priority_enabled=false` 时每条候选返回 winner + `SkippedByConfiguration`，规则内容保持不变。

这些端点不抓 RSS/Torrent、不调用 TMDB/AI、不写 ingest task，也不连接 qBittorrent；它们是可在真实编排前复用的配置与决策边界。

## 验收

```powershell
dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj --no-restore
```

App 118 项通过。新增用例覆盖默认 GET、保存归一、revision 冲突、720p 黑名单、同集简繁候选逐组短路、决胜组详情、开关禁用旁路及重复候选 ID 拒绝。请求和响应不含 passkey、Cookie 或 qB 凭据。

全量 Release 回归为 Core 104 + Data 53 + App 118 = 275 项通过，TypeScript strict 检查通过；`win-x64`、`.NET 10`、`PublishAot=true` 发布成功，0 warning/0 error。
