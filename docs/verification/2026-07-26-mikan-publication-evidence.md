# Mikan 发布时间证据持久化验证（2026-07-26）

## 范围

schema v19 在 `ingest_tasks` 增加：

- `source_published_at_raw`：RSS 中的原始 `pubDate`，仅用于审计；
- `source_published_at`：带偏移的规范时间，供后续 Bangumi 普通 Episode 日期候选计算。

Mikan RSS 不再截断 `T` 后的时分秒。显式 `Z`、UTC、GMT 或数字偏移按来源值解析；没有偏移时默认按 `Asia/Shanghai` 解析。缺失、非法和本地时区不存在的时间不会产生规范值。

发布时间证据不是统一导入公开 API 的字段。`IngestItemRequest` 使用独立 DTO，内部 `IngestItemCommand.SourceEvidence` 另有 `JsonIgnore` 防线；API 即使额外发送同名 JSON 字段，数据库两列仍为空。因此外部 Mikan/U2 程序不能伪造 AI 的发布日期最终门禁。

## NativeAOT 边界

- 日期只使用 `DateTimeOffset`、`DateTime` 和编译期 `GeneratedRegex`，不使用反射。
- SQLite 继续使用显式 migration、参数化 SQL 和 ISO 8601 带偏移文本。
- 原始值不会交给模型；后续门控只读取已规范化的可信内部值。

## 验收

- RSS 完整时间保留、显式偏移、无偏移上海时区、非法时间均有 Core 单元测试。
- 统一导入规范化器只接受 Mikan 内部证据，非 Mikan 证据会被拒绝。
- Mikan RSS 集成测试验证原值和 `+08:00` 规范值写入 schema v19。
- Minimal API 测试验证公开导入请求附带伪造 `source_evidence` 不会入库。
- schema migration 测试验证两列存在、版本连续且 migration 幂等。
- `dotnet test AnimeGoNet.slnx -c Release --no-restore --verbosity minimal`：Core 195、Data 71、App 216，共 482/482 通过；补充 schema v18→v19 生产数据回放后 Data 72/72 通过（当前总计 483）。
- `dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 -p:PublishAot=true --no-restore -o artifacts/mikan-publication-evidence-win-x64`：通过，无裁剪或 AOT 警告。

本模块不查询 Bangumi、不调用 AI/TMDB/qBittorrent，也不触碰本机 `TestSpace`。Bangumi Episode 日期候选及最终门禁在下一独立模块实现。
