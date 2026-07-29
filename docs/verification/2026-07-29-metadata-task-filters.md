# 2026-07-29 元数据失败筛选验收

## 语义

- 列表投影读取每个任务最新的 `result=failed` 尝试，返回失败阶段、稳定错误码和
  可重试性；完整尝试历史仍由任务时间线 API 提供。
- `retryable=true` 分类为 `explicit_retry`，WebUI 显示“可安全重试（需显式）”，
  不伪造成已经进入自动重试队列。
- Authentication/Configuration/InvalidInput 分类为需修复配置；其余不可重试的
  `metadata_failed` 分类为需人工处理。
- `tmdb_completion_pending` 分类为已兜底/待补全 TMDB；重复下载跳过独立分类，
  不显示成 TMDB 失败。

## API 与 WebUI

- `GET /api/v1/metadata/tasks` 支持分页、搜索、任务状态、失败阶段、错误码、
  可重试性、处理分类和四种排序。
- 参数使用枚举和稳定 ASCII 标识校验；无效分页、方向、分类或带空格的失败阶段
  返回 `metadata_task_filter_invalid`。
- 页面保存纯 UI 偏好，5 秒刷新不清除当前筛选；卡片展示分类、阶段、错误码、
  可重试性和脱敏原因。
- 响应、DOM 和筛选值不包含 Torrent URL、passkey、Cookie、凭据或绝对路径。

## 自动化

- `MetadataTaskFilterApiTests` 创建隔离 SQLite 任务与可重试网络失败，验证组合筛选、
  分类、分页投影、防秘密泄漏和无效参数。
- `MetadataTaskDetailApiTests`、原列表 API 测试和静态 WebUI 资源测试纳入回归。
- `npm run web:check` → `npm run web:build` → `npm run web:check`：通过。
- `dotnet test AnimeGoNet.slnx --no-restore`：720/720 通过（App 366、Core 228、
  Data 115、插件契约 11），0 失败、0 跳过。
- win-x64 NativeAOT Release 发布通过，无 trim/AOT 警告；`eng/smoke-native.ps1`
  通过 schema v24、原生 JSON、SQLite、静态 WebUI 和安全导入检查。
