# AI 调用日志触发原因验收

## 范围

- schema v55 为 `metadata_resolution_attempts` 增加可空的 `ai_trigger_reason`。
- 季度 AI 保存进入 AI 前最后一次确定性失败；Episode AI 保存本批未决视频原因。
- API 和 WebUI 摘要/展开详情均返回并显示该字段；旧记录显示“历史调用未记录”。

## 自动化验收

- Data schema/migration 与 AI 调用分页查询测试覆盖字段落库、搜索和返回。
- API 测试覆盖 `ai_trigger_reason` JSON 字段。
- WebUI 测试覆盖摘要与详情标签。
- 季度处理器覆盖 `season_unresolved:tmdb_season_air_date_not_matched`。
- Episode 处理器覆盖日期差超过 7 天，以及 Kokoore 文件名中 `- 07` 与哈希式方括号数字并存时的
  `episode_unresolved:ambiguous_episode_markers`，并确认 AI 结果仍须通过 TMDB S01E07 验证。

## 人工验收

1. 打开“日志 / AI 调用日志”。
2. 新产生的 AI 调用在折叠摘要和展开详情中均显示“AI 触发原因”。
3. schema v55 之前的调用显示“历史调用未记录”，不会使用最终错误码伪造触发原因。
