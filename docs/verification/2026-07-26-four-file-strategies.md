# 四种文件策略与做种阶段验收

日期：2026-07-26

## 上游基线

只读核对独立 `AnimeGo/develop` 的 `configs/default.go`、`internal/animego/renamer/renamer.go` 与 `clientnotifier/notifier.go`：

- `link`：下载完成时建立硬链接，保留源文件；
- `link_delete`：下载完成时建立硬链接，做种完成后删除源文件；
- `move`：下载完成时移动，无法继续依赖源文件做种；
- `wait_move`：做种完成后移动。

## .NET 实现

- schema v18 将旧版本中 `organization_state=not_required` 的非 move 下载任务回填为 `pending`，不会修改 route snapshot。
- 新任务的四种策略都进入持久化整理队列；candidate SQL 同时检查任务状态、下载 job 状态和不可变 `file_strategy`。
- `link`/`link_delete` 在 `Seeding` 即发布媒体、NFO 和规范 Episode 完成记录，但不暂停 qB；qB 进入 `Complete` 前不会 claim 清理阶段。
- `link_delete` 在清理阶段逐文件验证目标存在、大小与 SHA-256 一致后才删除对应源文件；重试时源文件已不存在视为幂等完成。
- `move` 立即暂停并安全移动；`wait_move` 在 qB `Complete` 以前返回无工作。
- qB 任务清理始终是 `deleteFiles=false`。路径计划、文件策略和操作路径持久化后不可变。
- Windows、Linux、macOS 硬链接调用继续使用 source-generated `LibraryImport`，不引入反射或动态 P/Invoke。

## 自动验收

- `SafeFileLinkerTests`：真实临时文件硬链接、源文件保留、目标冲突保全、link_delete 校验删除和幂等恢复。
- `MediaOrganizationProcessorTests`：link/link_delete 在 Seeding 发布、Complete 前不清理、Complete 后分别保留/删除源文件；wait_move 在 Complete 前零文件变更；既有 move/字幕/NFO/完成记录流程不回归。
- `SchemaMigrationTests.FileStrategyMigrationBackfillsExistingNonMoveDownloadJobs`：v18 回填旧非 move job。
- 解决方案全量：428 passed（Core 169、Data 69、App 190）。
- Data 全量：69 passed。
- App 全量：190 passed。
- `win-x64` NativeAOT 发布成功；隔离临时目录启动后 `/api/v1/status` 返回 200、`native_aot=true`、schema v18。

## 仍需外部验收

- Docker fixture qB 的真实 `uploading/stalledUP` 到 `stoppedUP/pausedUP` 转换。
- Linux amd64/arm64 与 macOS arm64 的同 inode/文件标识断言。
- 错误配置为跨文件系统时，`hard_link_unavailable` 必须保留源文件且不写伪完成。
