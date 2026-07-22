# RSS winner → 统一导入原子编排（2026-07-22）

## 边界

`UnifiedIngestProcessor` 从 Minimal API 私有函数提取为显式应用服务。现有 `/api/v1/ingest` 和 `/api/download/manager` 继续使用完全相同的规范化、SourceProfile 路由、SSRF/Host allowlist Torrent staging、Torrent 元数据校验和 `IngestTaskStore`，没有为 RSS 复制第二套导入路径。

`MikanRssIngestProcessor` 读取启用的 Mikan SourceProfile 与当前规则 revision，生成/幂等保存 batch，然后只为 decision=`Winner` 的 entry 领取租约。blacklist、whitelist 和 `SuppressedByHigherPriority` entry 永远不调用 staging。

winner 的 task、task_files、staged_torrents 与 batch entry=`ingested`/ingest_task_id 在同一 SQLite 事务提交。更新必须匹配 batch ID、candidate ID、`claimed` 状态和 lease token；租约已失效时整个 ingest 事务回滚，临时 Torrent 由统一处理器释放。可分类 staging 失败返回 rejected 并释放，取消或意外异常释放后继续抛出；同批重复调用返回原 ingest task，不重复 staging。

## 验收

- `dotnet test tests/AnimeGoNet.Data.Tests/AnimeGoNet.Data.Tests.csproj -c Release --no-restore --verbosity minimal`：57/57 通过。
- `dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj --no-restore --verbosity minimal`：130/130 通过。
- 新增 token 错误拒绝、显式 release、真实 task 完成绑定、完成后不可重领、失效 token 下 task/files/staged 三表全回滚、loser staging=0、重复 batch staging=0、网络失败重试和意外异常释放测试。
- 既有 unified/legacy Minimal API、staging、metadata、download 和整理测试全部继续通过。
- `dotnet test AnimeGoNet.slnx -c Release --no-restore --verbosity minimal`：Core 137、Data 57、App 130，共 324/324 通过。
- `dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 -p:PublishAot=true --no-restore -o artifacts/rss-winner-unified-ingest-win-x64`：NativeAOT 发布通过，无裁剪警告。

本模块尚未公开 `/api/rss`；下一提交只增加兼容 HTTP 契约、source-generated JSON 响应和 raw XML 请求边界，内部直接调用本处理器。
