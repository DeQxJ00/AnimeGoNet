# 作品库季度元数据审计验收

日期：2026-07-30

## 投影边界

- 当前人工 EP offset 从 `mikan_work_rules` 读取，包含启停、作用域、revision
  和更新时间；页面明确称为当前规则，不声称它等同于历史 Run 实际值。
- 关联任务通过规范 `task_files` 或任一 `metadata_resolution_runs` 的
  `tmdb_series_id + tmdb_season_number` 建立，最多返回最近 50 条。
- 季度时间线读取这些关联任务的全部历史 Run/attempt，而不只读取最新成功
  Run；最多返回最近 200 条。
- 时间线保留任务、Run 次数、阶段、策略、优先级、结果、稳定错误码、脱敏
  原因、可重试性、耗时和时间。两个有界列表都返回精确总数和截断标记。
- 不返回内部 Series/Season/attempt 行 ID、绝对路径、Torrent URL、passkey
  或凭据。

## SQLite

schema v30 只增加查询索引，不复制或反规范化已有审计数据：

```text
ix_task_files_tmdb_season_task
ix_metadata_runs_tmdb_season_task
ix_metadata_attempts_run_created
ix_mikan_work_rules_tmdb_season
```

迁移测试验证四个索引存在，并增加发布 smoke 默认 schema 与
`DatabaseSchema.CurrentVersion` 一致性断言，避免后续 schema 升级只在发布门禁
时才发现硬编码漂移。

## 自动测试

定向门禁：

```text
AnimeLibraryStoreTests + SchemaMigrationTests
Passed: 27, Failed: 0

AnimeLibraryApiTests + AnimeLibraryAdminApiTests + StaticWebUiTests
Passed: 87, Failed: 0

SchemaMigrationTests（含 smoke schema 同步）
Passed: 20, Failed: 0
```

完整 Release 门禁：

```text
dotnet test AnimeGoNet.slnx -c Release --no-restore
Plugin abstractions: 11 passed
Core: 264 passed
Data: 152 passed
App: 464 passed
Total: 891 passed, 0 failed, 0 skipped

dotnet build AnimeGoNet.slnx -c Release --no-restore
0 warnings, 0 errors
```

TypeScript 唯一源码重新编译到 `wwwroot/app.js`；`web:check` 和
`git diff --check` 通过。

## 浏览器与 NativeAOT

隔离进程使用随机临时目录、关闭后台 Worker，不读取 TestSpace 或 qBittorrent。
浏览器确认 schema v30 页面完成加载、作品库空态稳定，控制台
`warning/error` 为 0；带人工 offset、关联任务和多 Run 时间线的响应/DOM 契约
由真实 Kestrel API 和静态 WebUI 测试覆盖。

win-x64 NativeAOT 发布成功，无 trim/AOT warning。首次 smoke 准确发现脚本
仍期望 schema v29；将默认值升级为 v30 并增加同步测试后重跑：

```text
eng/smoke-native.ps1 \
  -Executable artifacts/publish/win-x64/AnimeGoNet.App.exe

Native smoke: passed
```
