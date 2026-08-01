# SourceProfile qB 下载策略快照（2026-07-26）

## 上游映射

基准为独立仓库 `wetor/AnimeGo@c7475df` develop：

- `setting.category` → SourceProfile `category`
- 下载时的 `Tag` → SourceProfile 静态 `tags` 加 AnimeGoNet 系统 tags
- `advanced.client.seeding_time_minute` → SourceProfile `seeding_time_minutes`
- qB 语义保持为 `0=不做种`、`-1=无限`、正数为分钟限制

上游动态 tag 支持 `{year}`、`{quarter}`、`{ep}` 等元数据变量。本次历史增量在 Torrent dispatch 时只发送静态 tags，不把未展开模板误写到任务；该边界后来由 schema v34 的元数据后置 qB tag 模块完成，见 `2026-08-01-dynamic-download-tags.md`。

## 数据与不可变边界

- schema v17 为 `source_profiles` 增加 `category`、`tags_json` 和 `seeding_time_minutes`；从 v16 升级保留 profile revision，并以 `animegonet`、空 tags、0 分钟安全迁移。
- API 创建未提供这些字段时使用相同默认值；更新未提供且文件策略不变时保留现值。
- `move` 强制 `seeding_time_minutes=0`。其他策略允许 `-1` 或 0～5,256,000 分钟。
- category 为 1～64 字符；静态 tags 最多 16 个、忽略大小写去重；category/tag 禁止控制字符和逗号，避免破坏 qB multipart 分隔。
- 创建 ingest task 时把 category、tags、做种分钟与 downloader/file strategy 一起写入 `route_snapshot_json`。之后修改 SourceProfile 不改变已创建任务。

## qB dispatch

- staged claim 只从不可变 route snapshot 读取策略，不回查当前 SourceProfile。
- qB add 的 category 使用快照值；tags 为 `animegonet`、来源 ID、文件策略和快照附加 tags 的忽略大小写并集。
- multipart 显式发送 `seedingTimeLimit`，并继续以 `stopped=true`/`paused=true` 添加。
- 相同 info-hash 已存在时保持幂等接管，不修改用户已有任务的 category/tag/做种限制。

## 自动验收

- schema v16→v17 迁移 fixture 验证默认值、原 revision 和数据保留。
- SourceProfile store/API 验证字段 CRUD、非法 category/tag/分钟拒绝，以及旧客户端省略字段的默认/保留行为。
- ingest store 验证 route snapshot 与 staged claim 精确投影。
- fake qB dispatcher 验证 category、系统 tags 和 `move=0`；HTTP handler 验证 `seedingTimeLimit=120` multipart。
- 原生 TypeScript WebUI 支持完整启用下载器候选、category、tags、做种分钟，并在选择 `move` 时锁定为 0。
- 完整解决方案测试通过：Core 169、Data 68、App 183，共 420 项；另有旧 API 省略新字段的保留/策略切换安全默认定向覆盖。
- `win-x64` NativeAOT 发布成功，schema v17、SourceProfile 新 DTO 和 qB multipart 字段未产生裁剪/AOT 警告。
