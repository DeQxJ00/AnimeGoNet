# 活动占用消失后的重复任务重试验收（2026-08-22）

## 业务边界

- `episode_already_completed` 是永久重复，本次功能不会重新下载。
- `episode_claimed_by_another_task` 只表示判定当时另一任务持有活动 Episode claim；下载准备把文件 priority 设为 0 并保持 qB Torrent paused，不删除载荷。
- 下载任务的“重新检查占用”会在单个 SQLite 事务内重新查询规范 completion 和当前 claim。只有两者均不存在（或原 claim 已 released）时才为该文件重新取得 active claim、恢复 `episode` 归类、清空旧文件分配并重新排入下载准备。
- Episode 已完成或仍被其他任务占用时返回 `download_duplicate_still_occupied`，任务与 qB 优先级保持不变。

## 自动化验收

- `DownloadPreparationProcessorTests`：普通永久重复仍以 `deleteFiles=false` 从 qB 清理；活动 claim 冲突则保持 paused 且不删除，保留显式重试所需的文件清单。
- `DownloadManagementApiTests`：详情暴露可重试；claim 已消失时重试恢复 task/file/preparation 并产生新的 active claim，全程不直接调用 qB resume；真实 completion 存在时返回独立 409 稳定错误码。
- TypeScript 构建产物与源码同步，任务卡在 `download_skipped_duplicate` 状态显示“重新检查占用”。

现有历史任务若其 qB Torrent 已被旧版本清理，数据库状态仍可安全重置，但下载准备会明确进入 qB 文件清单不可用的可重试失败；不会伪造已开始下载。重新提交来源或保留的 qB Torrent 可恢复载荷。新版本产生的活动占用重复不再遇到该历史限制。
