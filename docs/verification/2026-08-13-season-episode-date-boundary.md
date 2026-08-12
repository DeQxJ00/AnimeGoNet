# 季度与 Episode 日期边界修正

日期容差的业务语义于 2026-08-13 明确为：

- Bangumi 作品/季度首播日期与 TMDB Season `air_date` 允许相差 `±1` 个日历日；
- Bangumi Episode `airdate` 与 TMDB Episode `air_date` 不使用上述容差，确定性日期映射只接受同一日且唯一的候选；
- Torrent `published_at` 不参与季度或 Episode 的确定性日期校验，只保留为 Mikan AI 的可选辅助输入。

旧 Go `develop` 在 `internal/constant/anisource.go` 定义
`ThemoviedbMatchSeasonDays = 90`，并在 `parseAnimeSeason` 中用 Bangumi Subject
日期选择 TMDB 普通 Season。AnimeGoNet 早期为保持上游行为保留了该窗口；本次按已确认
业务语义有意收窄为 `±1` 日。此前新增的 Episode `±1` 日窗口属于错误套用，现已移除。

验收覆盖季度差值 `-2/-1/0/+1/+2`，期望只有 `-1/0/+1` 成功；Episode
同日映射继续成功，相差一天明确返回 `tmdb_episode_bangumi_date_not_found`。内置统一
AI Prompt 同步升级为 `tmdb-ai-match-v13`，明确容差只属于季度首播日期。

旧 Mikan 实测报告是在错误的 Episode 容差下生成的历史证据；其中只有日期同日的映射
仍可直接作为当前规则证据，依赖相邻日期的结果必须按新规则重新执行后才能验收。

## 验收结果

- Core 边界定向测试：17/17 通过；
- Prompt/API/配置定向测试：45/45 通过；
- 完整 .NET Debug 测试：1625/1625 通过（Plugin Abstractions 13、Plugin SDK 16、
  Core 389、Plugin Tool 23、Data 217、App 967）；
- WebUI TypeScript 类型检查、构建和静态 DOM 测试通过，Web 测试 19/19。
