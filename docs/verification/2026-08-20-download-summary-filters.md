# 下载摘要快捷筛选验收（2026-08-20）

## 行为

- 下载状态的“活动、暂停、失败、等待整理、已完成、过期快照”摘要卡片可直接筛选下方任务列表。
- 后端 `summary_bucket` 与全局摘要使用同一组 SQLite 判定，避免卡片数字与列表口径漂移；失败同时覆盖 qB error、业务下载错误、下载准备失败与整理失败。
- 当前卡片显示选中态和 `aria-pressed=true`；再次点击取消。手工提交状态筛选或重置时清除快捷筛选。
- 连接速度和离线实例不是任务数量口径，不提供误导性的任务筛选。

## 验证

- `DownloadJobStoreTests`：9/9，通过全部摘要 bucket、非法 bucket、离线 stale 与既有下载状态投影测试。
- `DownloadManagementApiTests`：7/7，通过 `summary_bucket=active` 回显/筛选与非法 bucket 400。
- `npm run web:check`：通过。
- `npm run web:test`：36/36，通过可访问按钮、全部 bucket、API 查询参数、选中样式与既有 WebUI 契约。
- 本机真实 WebUI 使用现有 79 条下载任务验证：点击“失败 1”后仅显示 1 条 `target_conflict` 任务，列表状态显示“快捷筛选：失败”；再次点击恢复 79 条。验证过程未修改任务、下载器或媒体文件。
