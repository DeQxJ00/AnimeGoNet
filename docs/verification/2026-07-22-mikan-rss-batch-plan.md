# Mikan RSS 批次计划验证（2026-07-22）

## 流程

`MikanRssBatchPlanner` 把一个已解析的 `RssFeedDocument` 按原 RSS 顺序转换为候选：

1. 从 entry title 取得普通/小数/特别篇/未知来源 Episode 分类。
2. 使用 RSS 级 `mikanid`，构造 `(mikanid, kind, source episode)` 可靠分组键。
3. 候选 ID 优先对不含下载凭据的 Mikan Episode URL 做 SHA-256；缺失时才哈希 Torrent URL。重复 entry 使用确定性序号保持批内唯一，原始 URL 不进入 ID。
4. 优选启用时调用 `MikanRssRuleEngine`：黑名单、白名单、可靠同集多候选有序组、剩一立即停止、最终按 RSS 原顺序稳定选择。
5. 优选禁用时所有候选均为 winner，原因固定 `SkippedByConfiguration`，规则快照不变。

纯计划完整保留原 `RssFeedItem`、规范候选和最终决策，供下一层一次性持久化。loser 只有决策，不会自动晋级。

## 验收

- `dotnet test tests/AnimeGoNet.Core.Tests/AnimeGoNet.Core.Tests.csproj --no-restore --verbosity minimal`：137/137 通过。
- 新增 6 个 batch cases：同集优选、单候选黑名单、不同 EP/未知旁路、小数与整数隔离、禁用开关、稳定脱敏 ID/重复 ID。
- `dotnet test AnimeGoNet.slnx -c Release --no-restore --verbosity minimal`：Core 137、Data 53、App 127，共 317/317 通过。
- `dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 -p:PublishAot=true --no-restore -o artifacts/mikan-rss-batch-plan-win-x64`：NativeAOT 发布通过，无裁剪警告。

本模块不读取网络/文件、不访问 SQLite、不获取 Torrent、不创建 unified ingest task，也不调用 TMDB/AI/qBittorrent。下一提交将增加 batch/entry/decision 显式 SQL 模型和原子持久化，再以 winner 决策作为昂贵副作用的唯一入口。
