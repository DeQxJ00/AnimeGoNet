# TV Extras 与 Movie 提示边界

- 已确认 TMDB Series/Season、但无法确认普通 Episode 的视频使用 `extras`。
- NCOP、SP、小数集号及无集号视频不计入 Other，也不会制造伪造 EP 进度。
- TV 任务中文件名包含 `劇場版`、`剧场版` 或大小写任意形式的 `movie` 时继续使用
  `other`，仅作为人工 TV+Movie 后处理入口；关键词不是 Movie TMDB 验证结果。
- 非视频附件仍沿用“存在已确认正片时转 Extras，纯附件任务保持可见”的安全边界。

验收：`EpisodeMetadataResolutionProcessorTests` 覆盖 SP/小数集号/无集号 Extras、
三种 Movie 提示和不请求伪造 TMDB Episode。
