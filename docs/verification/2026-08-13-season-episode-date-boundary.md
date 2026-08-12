# 季度与 Episode 日期边界修正

日期容差的业务语义于 2026-08-13 明确为：

- Bangumi 作品/季度首播日期与 TMDB Season `air_date` 允许相差 `±1` 个日历日；
- Bangumi Episode `airdate` 与 TMDB Episode `air_date` 的主匹配同样允许 `±1` 日；
- 主匹配失败后，仅实际 Torrent 文件总数为 1 时，从文件名读取普通整数 EP，以该 EP 定位 Bangumi Episode，再找 TMDB Season 中日期最近项；差值不超过 7 日且 TMDB EP 与文件名 EP 一致才接受；
- 超过 7 日、编号不一致、证据缺失、无法消歧或多文件主匹配失败时进入统一 AI；AI 关闭时进入已确认季度 Other；
- Torrent `published_at` 不参与季度或 Episode 的确定性日期校验，只保留为 Mikan AI 的可选辅助输入。

旧 Go `develop` 在 `internal/constant/anisource.go` 定义
`ThemoviedbMatchSeasonDays = 90`，并在 `parseAnimeSeason` 中用 Bangumi Subject
日期选择 TMDB 普通 Season。AnimeGoNet 早期为保持上游行为保留了该窗口；本次按已确认
业务语义有意收窄为 `±1` 日。Episode 使用独立的上述两级规则。

验收覆盖季度差值 `-2/-1/0/+1/+2`，期望只有 `-1/0/+1` 成功；Episode 主匹配覆盖
`±1` 日，单文件补判覆盖 2/3/7 日成功、8 日失败、编号不一致失败和多文件转 AI。
本地确定性 Episode 规则不写入 AI Prompt；经项目所有者明确确认，内置统一 AI Prompt `tmdb-ai-match-v15` 的第 9 条只保留季度首播日期规则。

旧 Mikan 实测报告早于单文件 7 日补判和独立证据来源，必须按最终规则重新执行后才能
作为完整验收证据。

## 验收结果

- Core 边界定向测试已纳入完整回归，覆盖 `±1` 日主匹配、2/3/7 日单文件补判、
  8 日拒绝、编号不一致拒绝及多文件转 AI；
- Prompt/API/配置契约使用已确认的内置版本 `tmdb-ai-match-v15` 并纳入完整回归；
- 完整 .NET Debug 测试：1638/1638 通过（Plugin Abstractions 13、Plugin SDK 16、
  Core 399、Plugin Tool 23、Data 217、App 970）；
- WebUI TypeScript 类型检查、构建和静态 DOM 测试通过，Web 测试 19/19。
