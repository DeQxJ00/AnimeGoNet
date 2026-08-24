# TV Other 物理目录改为 Extras

## 已确认行为

- TV 文件在 TMDB Series 和普通 Season 已确认、但 Episode 无法可靠确认时，业务分类仍为 `Other`。
- 新整理目标由 `<TMDB 动画名>/Sxx/Other/原文件名` 改为 `<TMDB 动画名>/Sxx/Extras/原文件名`。
- 正片路径、Movie 路径、任务筛选、失败原因和“Other 重新适配”业务名称均不改变。
- 已经存在于历史 `Sxx/Other/` 的文件不自动移动或删除；重新适配继续按数据库保存的实际媒体路径读取，因此兼容旧目录。
- 外部媒体补录只扫描 `Sxx` 顶层规范 `E###` 视频，`Extras` 与历史 `Other` 子目录均不会计入正片进度。

## 验收

- `MediaPathPlannerTests`：13 项通过，包含跨平台目录片段和非法字符规范化。
- `ExternalMediaImportStoreTests`：3 项通过，确认 `Extras` 与历史 `Other` 均不参与外部正片补录。
- `MediaOrganizationProcessorTests` 与 `OtherFileReadaptationApiTests`：16 项通过，确认正常整理写入 `Extras`，历史 `Other` 路径仍可重新适配。
