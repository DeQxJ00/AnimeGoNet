# AI 调用日志 TMDB 最终 Episode 验证

日期：2026-08-20

## 行为

- schema v52 按 AI attempt 持久化主程序最终通过 TMDB 验证的 Series、Season、Episode、Episode ID 与名称。
- 多文件及跨季度结果按唯一 `Series/Season/Episode` 排序返回。
- AI 调用日志摘要行和展开详情同时显示最终验证 EP；没有审计记录时明确显示未通过验证。
- 记录来自 `CompleteEpisodesAsync` 的同一事务，不采用模型自报值；后续人工重新适配不会改写旧记录。
- 升级时从仍带 `episode_resolution_attempt_id` 的现有文件证据回填历史记录。

## 验收

- Data 测试覆盖一个 AI attempt 同时验证 S01E001 与 S02E001，并校验 Episode 名称。
- API 测试校验 `validated_episodes[]` 的完整结构。
- WebUI 静态契约校验摘要和详情均包含“TMDB 最终验证 EP”。
- Release 解决方案构建通过：0 warning、0 error。
- `AnimeGoNet.Data.Tests` 完整 238/238 通过；相关 App API/WebUI 测试 3/3 通过；WebUI 测试 35/35 通过。
- 本机测试库已迁移至 schema v52，历史回填 6 条审计；运行中 API 的 7 条 AI 调用里，6 条返回最终验证 EP，1 条失败调用返回空列表。
