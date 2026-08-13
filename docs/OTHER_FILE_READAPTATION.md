# Other 文件重新适配

该功能用于已经完成整理、但一个或多个文件因 Episode 无法确认而位于
`<TMDB 动画名>/Sxx/Other/` 的任务。它不是“重置整个任务”，也不会重新下载。

## 行为边界

- 入口：任务库中状态为 `organized` 且 `Other > 0` 的任务显示“重新适配 Other”。
- 预览：`GET /api/v1/metadata/tasks/{taskId}/other-readaptation/preview`。
- 执行：`POST /api/v1/metadata/tasks/{taskId}/other-readaptation`。
- 保留任务、来源证据、既有 Metadata Run、策略时间线和 AI 调用/Debug 日志。
- 保留已经确认的 TMDB Series 和普通 Season，只清除目标 Other 文件的 Episode 结果。
- 重新执行当前 Episode 确定性规则；满足现有门禁时可调用当前配置的统一 AI 流程，AI 结果仍须经 TMDB Episode 验证。
- 成功后以旧 `Other` 文件作为安全移动源，直接进入规范 Episode 路径；再次无法确认时文件原地保留在 `Other`。
- 不重建 Torrent、不重新下载、不改变下载准备状态，也不调用 qB pause/delete/cleanup。

## 安全门禁

首版只支持任务快照中的 `move` / `wait_move`。执行前必须同时满足：

1. 任务已经 `organized`，下载整理状态已经 `completed`；
2. 没有活动中的元数据解析租约；
3. 每个 Other 文件仍存在于已记录路径，且字节数与 Torrent 清单一致；
4. 每个目标路径只被一条完成的文件操作引用；共享路径不会自动移动；
5. 文件仍具有大于零的 TMDB Series/Season。

任一门禁失败，API 返回稳定冲突响应且不修改任务、数据库文件状态或媒体文件。

## 持久化与恢复

SQLite schema v47 的 `other_file_readaptation_jobs` 按文件保存旧媒体路径、原始
`other_reason`、请求时间和完成时间。重新适配期间，媒体整理只领取这些文件，并把旧
媒体路径作为源路径。全部文件操作和完成记录提交后，任务直接恢复为 `organized`，
不会再次进入下载器清理阶段。

## 验收

- Data tests：状态重入、TMDB Series/Season 保留、共享路径拒绝、源路径覆盖、完成后不产生 qB cleanup。
- API test：真实临时文件通过预览，执行后文件仍在原位等待 Episode worker，下载准备保持 `completed`。
- WebUI：任务卡仅在 `organized + Other` 时显示按钮，先显示文件与旧原因确认，再执行。
- NativeAOT：随正常 win-x64 发布 smoke 验证端点和静态 WebUI。
