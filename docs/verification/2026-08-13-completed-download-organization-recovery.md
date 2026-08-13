# 下载完成后整理恢复验收（2026-08-13）

## 故障

qBittorrent 报告 `seeding/complete` 后，快照同步把业务状态更新为 `downloaded`。旧领取条件会把它再次领取为新的元数据 Run；Series/Season 成功后状态停在 `metadata_season_resolved`，但文件早已由上一轮全部解析，因此 Episode Worker 无文件可领，整理 Worker 又只接受 `downloaded`，形成永久停滞。

## 修复

- 元数据领取只接受 `download_preparing`，或兼容旧流程时 `downloaded + preparation_state in (pending, preparing)`。
- `preparation_state=completed` 是不可逆门禁：下载完成后不再创建元数据 Run，只进入整理。
- schema v44 一次性恢复满足以下全部条件的旧任务：
  - 状态为 `metadata_season_resolved`；
  - qB 作业已完成且下载准备 completed、整理 pending；
  - 至少有一个任务文件，且没有 pending 文件；
  - 没有仍在运行的元数据 Run。
- 不满足任一条件的任务保持原状态，避免把真正等待 Episode 解析的任务误送整理。

## 自动验收

- `MetadataResolutionStoreTests.CompletedDownloadPreparationCannotStartASecondMetadataRun`
  验证准备完成后零 claim、零新 Run。
- `SchemaMigrationTests.CompletedMetadataOrganizationRecoveryOnlyRepairsResolvedDownloadedTasks`
  验证 v44 只恢复文件已经解析的任务，仍有 pending 文件的任务不变。
- 数据层完整测试与解决方案编译验证迁移、租约和既有状态机契约。

## 本机 TestSpace 实际恢复

发布并启动 `win-x64` NativeAOT 后，状态接口确认 `database_schema_version=44`、
`native_aot=true`，路径仍指向独立 TestSpace。迁移恢复了 3 个真实 qB 已完成任务，
正式整理 Worker 随后全部推进为 `organized`：

- TMDB 91768 S02E16，媒体与 sidecar 落入作品库；
- TMDB 30983 S01E118，媒体与 sidecar 落入作品库；
- TMDB 302051 S01E06，媒体与 sidecar 落入作品库。

升级前数据库已先复制为同目录的 `animegonet.db.before-schema44-*.bak`；没有直接
修改任务行，也没有用测试脚本伪造整理完成状态。
