# Mikan RSS 多文件候选二次优选验证

## 已实现边界

- RSS `[02-03]` 不直接产生来源 Episode，初始决策仍为 `UngroupedBypass`。
- 同一批次、同一 mikanid 至少两个旁路 winner 时，预检只读取 `.torrent` 元数据；不创建下载任务、不访问 qBittorrent、不调用 AI。
- 只有实际视频文件数大于 1、每个文件均可解析唯一普通 EP，并完成 Bangumi/TMDB 日期对应及 TMDB Episode 官方验证的候选，才形成规范覆盖集合。
- 覆盖集合与普通单集候选使用原 RSS title 重新运行现有黑白名单/有序规则。合集必须赢得全部覆盖 Episode；部分胜出记录 `PartialCoverageConflict` 并整体移除后重算。
- 完整胜出的合集记录 `VerifiedMultiEpisodePriorityWinner`；重叠普通单集记录 `SuppressedByMultiEpisodeWinner`。统一导入接管预检 staging，不再次获取 `.torrent`。
- 网络、Torrent 元数据或确定性元数据证据失败时保持原批次规划，后续正式任务仍可按原逻辑进入 AI 或 Other。

## 自动验收

- `MikanRssVerifiedCoverageSelectorTests`：覆盖完整合集胜出、重叠普通单集压制，以及部分覆盖冲突移除后单集重新胜出。
- `MikanRssIngestProcessorTests.VerifiedMultiFileWinnerSuppressesOverlappingSinglesAndReusesStagedTorrent`：两个 `[02-03]` 合集与 EP2/EP3 单集同批输入，仅发生两次 Torrent staging，最终只创建一个合集任务，证明 winner 复用预检 staging。
- Mikan RSS App 回归 15/15、Core RSS 回归 36/36 通过。

真实 Mikan RSS 和 qBittorrent 下载没有在本验证中触发；本模块的生产行为边界由确定性 fake Torrent/Bangumi/TMDB 测试锁定，避免测试误创建私人下载任务。
