# 匹配与整理同步删除结果验证

## 行为

- 新增 `POST /api/v1/delete/tasks/{taskId}/execute`，仍先校验不可变预览 fingerprint
  和四类选择，再定向执行持久化删除计划并同步等待结果。
- 同一任务已有 `pending/executing` execution 时接管该 execution，不再把
  `This task already has an active delete execution.` 暴露给 WebUI，也不创建重复计划。
- 同步响应包含 execution ID、是否接管已有 execution、整体状态、尝试次数、失败原因以及每个冻结目标的状态。
- qB 删除仍固定 `deleteFiles=false`；源文件、媒体文件和业务/任务记录仍按原有安全顺序执行。
- 删除完成才刷新并显示成功；失败或等待状态保留对话框，显示完成、跳过、失败、待处理数量，
  可用同一按钮重试现有 execution。
- HTTP 请求中断不取消已经持久化的执行；执行阶段使用应用停止令牌，避免浏览器关闭导致
  execution 在五分钟租约内无主悬挂。

## 验收

- `dotnet test tests/AnimeGoNet.Data.Tests/AnimeGoNet.Data.Tests.csproj -c Release --filter "FullyQualifiedName~Deletion" --no-restore`：6/6 通过。
- `dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj -c Release --filter "FullyQualifiedName~Delete" --no-restore`：33/33 通过。Processor 覆盖按 execution ID 定向执行；API 预先创建 active execution，再调用同步入口，断言复用同一 ID、最终 completed、尝试次数和逐项目标状态。
- `npm run web:test`：29/29 通过，覆盖同步按钮文案、`/execute` 入口、等待、完成、失败统计和重试状态。
- win-x64 NativeAOT 发布成功；本机 6180 状态为 `native_aot=true`、schema v51，未知任务同步删除返回稳定 `delete_task_not_found`。
- 浏览器只读打开真实任务的删除预览，确认提示为“确认后页面会同步等待本次删除结果；已有执行会直接接管”，按钮为“确认删除并等待结果”。验收没有勾选动作或确认删除，不修改真实 qB 任务、文件或业务记录。
