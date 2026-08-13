# Other 重新适配完整重跑（2026-08-14）

## 业务边界

- 入口只面向已整理任务中的 `Other` 文件；不会重新下载 Torrent，也不会操作 qBittorrent。
- Mikan 任务从已保存且不含 query/fragment/user-info 的 Episode 来源页重新解析 `mikanid+groupid`，随后重新解析 `mikanid→bgmid`。含 passkey 或查询参数的 URL 不会保存为重试来源。
- 本次强制运行跳过 Mikan 身份缓存、Bangumi Archive 缓存、TMDB SQLite 成功响应缓存和可信 offset 读取；新取得的 Mikan/TMDB 成功响应仍写回缓存。人工 TMDB 覆盖继续保持最高优先级。
- 清空所选文件旧 Series/Season/Episode 结论并重新执行完整匹配；需要 AI 时不会被历史 AI 尝试记录抑制，Prompt 本次未修改。旧解析 Run、Attempt、AI 日志继续保留供审计。
- 同一路径被多个任务引用时，整理使用校验复制并保留源文件；独占路径继续使用 move，避免共享任务互相抢占文件。

## 人工审核与删除

任务从重新适配开始进入 `readaptation_review_state=pending`。只有完成整理回到 `organized` 后，WebUI 的“确认人工审核”才可提交；审核前后端均拒绝删除 AnimeGoNet 任务记录。删除中心新增独立“任务记录”选项，始终最后执行；它不会替代业务记录、下载器任务、源文件和媒体文件四类选择。

## 自动验收

- `OtherFileReadaptationStoreTests`：清空旧元数据、重新计算文件 EP 候选、共享路径保留源文件标记及整理续跑。
- `OtherFileReadaptationApiTests`：隔离 Mikan Episode/作品页真实 HTTP 链路、完整重新入队、不重复下载、人工审核与删除许可。
- `SafeFileMoverTests`：共享源首次复制和目标已存在重试均保留源文件，并验证字节一致。
- `DeletePlanStoreTests` / `DeleteExecutionProcessorTests`：未审核拒绝、qB 前置约束、任务记录最后执行及级联清理。
- TypeScript 严格编译及 Web 单元测试覆盖新增任务中心按钮和删除请求字段。
