# 2026-08-09 Mikan 真实链路审计

状态：29 条 metadata/qB 暂停投递审计完成；真实下载/整理已完成首批 2 条。Docker 按项目所有者要求不执行。

## 已验证

- 新增显式 opt-in 的 `MikanLiveChainAuditTests` 与 `eng/mikan-live-audit.ps1`；默认 CI 不连接本机 qB。
- 审计从私有 29 条 CSV 读取真实 Mikan 输入，使用隔离 qB 与 TestSpace 路径，并增量记录筛选、元数据、下载、整理和 AI 用量；报告不保存凭据或原始 Torrent URL。
- 第 2–3 行 metadata-only 与真实下载/整理实测均为 2/2：分别得到预期 `TMDB 65942 / S01 / E056` 与 `TMDB 65942 / S01 / E067`，AI 调用 0、token 0。真实批次耗时 8 分 33 秒，两条 qB payload 均完成，生成规范 `E056.mp4`/`E067.mp4`、Episode sidecar、Season sidecar、Series sidecar 与 `tvshow.nfo`，最终状态为 `CleanupCompleted`。
- 批次结束后核对 qB：audit task 0、audit category 0；遗留的两个精确 audit tag 已按名称删除，未使用文件删除。媒体文件保留在获准的 TestSpace `jellyfin_data` 中。
- 实测发现来源同号不能直接等于 TMDB 同号：TMDB 将连续作品放在同一季度时，Mikan/Bangumi 会从 1 重新编号。新增 `tmdb_episode_bangumi_date` 确定性策略，以唯一 Bangumi 普通 Episode 日期在已确认 TMDB Season 内做 `±1` 日唯一最近匹配，并再次调用 TMDB Episode endpoint 验证。来源集号同时识别 Bangumi `ep` 与全局 `sort`；当前 Subject 无该编号时读取直接 `续集`，第 9 行据此把 `sort=45 / 2021-08-31` 映射为 TMDB `82684/S02E21 / 2021-08-31`。
- 2026-08-09 完整 metadata/qB 暂停投递报告 `run-20260809-120518-f1b7dd77`：29/29 通过，失败 0，AI 任务 0，Prompt/Completion/Total token 均为 0。第 28 行按修正后的私有 CSV 验证为 `79166/S03E01`；第 10 行视频 `S02E22` 与两条外挂字幕一起匹配；第 30 行为 `223564/S01E28`。
- Torrent `published_at` 不参与 Bangumi/TMDB 单集 `±1` 日校验，也不作为确定性 EP 证据；它只在 Mikan AI 请求中保留为辅助参数。
- resolver、Bangumi `sort`/续集、字幕关联与 Prompt 边界的定向测试通过；完整 solution Release 测试共 1509 项全部通过。
- win-x64 已重新执行 NativeAOT 发布并出现 `Generating native code`；最终发布二进制的首次启动/API/SQLite/WebUI smoke 与 AI 元数据后台 worker smoke 均通过，schema 为 v41。

## 待完成

- 真实下载/整理仍需在用户明确允许的测试 Torrent 范围内继续分批执行；metadata/qB 暂停投递 29 条已完成。
