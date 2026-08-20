# 重复跳过独立统计验收（2026-08-20）

## 业务口径

- `ingest_tasks.status=download_skipped_duplicate` 只计入 `skipped_duplicate_jobs`。
- “等待整理”明确排除上述任务；即使历史任务仍残留 `organization_state=pending` 也不误计。
- 下载 API 新增 `summary_bucket=skipped_duplicate`，任务中心与总览均提供“重复跳过”原生按钮。

## 验证

- `DownloadJobStoreTests.SummaryBucketsUseTheSamePredicatesAsDashboardCounts`：1/1 通过，覆盖残留 `organization_state=pending` 的排除行为。
- `DownloadManagementApiTests`：8/8 通过，新增 API 断言证明重复跳过总数为 1、等待整理为 0，并验证两个 bucket 的返回列表。
- `npm run web:check`：通过。
- `npm run web:test`：36/36 通过。
- 静态资源版本定向测试：2/2 通过。
- Release 构建：0 警告、0 错误。
- 本机真实数据验收：修正前“等待整理”为 21；修正后显示“等待整理 5”和“重复跳过 16”。点击“重复跳过”进入下载任务并显示 16 条及“快捷筛选：重复跳过”；点击“等待整理”显示 5 条，页面内不含“重复集已跳过”。未修改任何业务任务或媒体文件。
