# 2026-07-29 下载管理闭环验收

## 范围

- schema v24 下载任务审计事件及 v23 升级回填。
- 下载任务服务端搜索、状态/来源/实例筛选和分页。
- 下载详情、SQLite/qB 实时文件并集、priority/wanted/进度与状态时间线。
- qB 暂停/恢复、AnimeGoNet 准备/整理/下载错误安全重试。
- 静态 TypeScript WebUI 的筛选、分页、详情和控制入口。

## 安全边界

- 自动化测试全部使用临时 SQLite 与 fake `IDownloadClient`，不连接或修改
  `TestSpace` 中的真实 qBittorrent 任务。
- 列表和详情不返回 Torrent URL、passkey、下载器凭据、异常正文或下载/媒体绝对路径。
- qB 不可用时，文件详情保留 SQLite 快照并返回稳定失败码；写操作失败单独写审计事件。
- 所有写操作携带 `expected_revision`，陈旧操作返回 `409`，不会覆盖已变化的任务。

## 自动化验收

- `DownloadManagementApiTests`：筛选/分页、实时文件并集、路径与 passkey 防泄漏、
  pause/resume revision 冲突、业务重试、qB 离线降级。
- `DownloadJobStoreTests`：状态投影、离线 stale、恢复、筛选、文件详情、审计事件和
  乐观并发控制。
- `SchemaMigrationTests.DownloadAuditMigrationPreservesExistingJobAndCreatesInitialEvent`：
  从 schema v23 数据升级到 v24，保留 job 并建立 `projection_initialized` 初始事件。
- `StaticWebUiTests`：生成静态资产包含详情、筛选、revision 控制和时间线入口。

## 最终结果

- `npm run web:check` → `npm run web:build` → `npm run web:check`：通过，TypeScript
  源码和生成的 `wwwroot/app.js` 一致。
- `dotnet test AnimeGoNet.slnx --no-restore`：705/705 通过（App 351、Core 228、
  Data 115、插件契约 11），0 失败、0 跳过。
- `dotnet publish ... -r win-x64 --self-contained true -p:PublishAot=true`：通过，
  无 trim/AOT 警告。
- `eng/smoke-native.ps1 -ExpectedSchemaVersion 24`：发布后的原生进程启动成功，
  返回 `native_aot=true`，完成 schema v24/SQLite、静态 WebUI、兼容缓存 API 和安全
  导入拒绝检查；隔离进程与临时目录已回收。
