# Mikan RSS 批次持久化与副作用门禁（2026-07-22）

## schema v14

新增三张 STRICT 表：

- `mikan_rss_batches`：来源 profile、规则 revision、优选开关、mikanid、entry 数量、幂等 SHA-256 指纹和时间。
- `mikan_rss_batch_entries`：原顺序、title/Mikan URL、Torrent URL SHA-256、来源 EP、完整 decision/winner/reason、副作用状态与租约。原始 Torrent URL 不入库。
- `mikan_rss_decision_groups`：按实际执行顺序规范化保存优先级组，避免反射 JSON 数据层。

数据库 CHECK 约束强制 `Winner` 只能是 `ready/claimed/ingested`，所有 blacklist/whitelist/priority loser 只能是 `blocked`。claimed 必须同时有 token/到期时间，ingested 必须关联真实 ingest task。应用层无法把 loser 普通更新为 ready。

`SaveAsync` 在单事务写 batch、entries 和 groups；相同 profile + rule revision + 计划得到相同指纹并幂等返回原 batch。`TryClaimWinnerAsync` 只对 winner 原子 `ready→claimed`，租约期内重复领取失败，到期可用新 token 恢复。

## 验收

- `dotnet test tests/AnimeGoNet.Data.Tests/AnimeGoNet.Data.Tests.csproj --no-restore --verbosity minimal`：55/55 通过。
- 覆盖 schema v14 幂等迁移、完整 round-trip、原始 Torrent URL 不返回/不入列、重复保存幂等、loser 领取失败、winner 单租约、到期恢复，以及直接 SQL 把 loser 改为 ready 被 SQLite 拒绝。
- `dotnet test AnimeGoNet.slnx -c Release --no-restore --verbosity minimal`：Core 137、Data 55、App 127，共 319/319 通过。
- `dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 -p:PublishAot=true --no-restore -o artifacts/mikan-rss-batch-storage-win-x64-final`：NativeAOT 发布通过，无裁剪警告。

本提交还不获取 Torrent、不创建 ingest task，也不调用 TMDB/AI/qBittorrent。下一层必须先持有 winner lease，使用仍在内存中的原始 URL 调统一 ingest，并在同一业务收尾中把 lease 标记为 ingested；失败只允许显式释放或租约到期重试。
