# legacy MikanTool 配置存储（2026-07-22）

## AOT-safe codec

`LegacyMikanFilterCodec` 使用 `JsonDocument` 按属性顺序枚举和 `Utf8JsonWriter` 手工输出，不使用反射 DTO。它固定输出 `Filiter0`～`Filiter4` 旧拼写，保留规则键顺序、空字符串、重复关键词、大小写和 Unicode；缺失 tier/规则字段按上游空配置处理，错误类型结构稳定拒绝。

## schema v15

- `legacy_mikan_filter_sets`：当前 revision、修改来源、创建/更新时间。
- `legacy_mikan_filter_rules`：tier 0..4、legacy key、position、白/黑开关。
- `legacy_mikan_filter_values`：白/黑列表 position 和原始 value；不增加去重/非空/lowercase 约束。
- `legacy_mikan_filter_snapshots`：每个 revision 的 canonical JSON、来源与时间，用于审计和回滚。

初始化 revision 1 为空配置且幂等。legacy 完整上传读取当前 revision 后替换并标记 `legacy_api`；Web 保存要求 expected revision；回滚读取目标快照但创建新的 `rollback` revision，不修改或删除历史。

## 验收

- `dotnet test tests/AnimeGoNet.Core.Tests/AnimeGoNet.Core.Tests.csproj --no-restore --verbosity minimal`：156/156 通过。
- `dotnet test tests/AnimeGoNet.Data.Tests/AnimeGoNet.Data.Tests.csproj --no-restore --verbosity minimal`：60/60 通过。
- 覆盖 JSON 顺序/空词/重复/大小写 round-trip、结构错误、schema v15 幂等迁移、规范化行、完整替换、stale revision、legacy 来源、快照计数和回滚新 revision。
- `dotnet test AnimeGoNet.slnx -c Release --no-restore --verbosity minimal`：Core 156、Data 60、App 132，共 348/348 通过。
- `dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 -p:PublishAot=true --no-restore -o artifacts/legacy-mikan-filter-storage-win-x64`：NativeAOT 发布通过，无裁剪警告。

本提交不提供 HTTP API，也不改变 RSS 过滤结果；下一提交把 `/api/plugin/config` 的 Base64 envelope 映射到本 store，不创建或读取 Python 文件。
