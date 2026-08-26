# TV+Movie 人工后处理

- Torrent 仍按用户选择的 TV 类型进入原链路，不在下载前拆包或猜测多个媒体类型。
- “匹配与整理”中所有已整理任务都提供“TV+Movie 后处理”；后端只接受已整理 TV 任务。
- 后处理文件列表来自已完成的实际媒体操作，包含正片、Extras、Other 和已忽略视频；
  `劇場版`、`剧场版`、大小写任意的 `movie` 仅负责预选，漏判文件可手工选择。
- 一次处理一个 Movie 视频。用户通过 TMDB Movie 可视化搜索选择候选；提交时后端再次
  调用 `/movie/{id}` 验证身份，不信任浏览器返回值，也不调用或修改 AI Prompt。
- 后端保留原文件/TV 身份快照，清除被误占用的 TV Episode 完成记录，把文件从 TV
  媒体库安全迁移到 `movie_save_path`，写 `movie.nfo`，建立 Movie 完成记录并回到
  `organized`。重复 Movie 或并发占用会被拒绝。
- 处理 Movie 后，原 Torrent 和 qBittorrent 任务不被删除；对于 PT 硬链接来源，仅移动
  TV 媒体库中的链接，长期做种目录保持不变。

验收：应用测试覆盖预选、TMDB 搜索、二次验证、TV→Movie 实体迁移、NFO、数据库身份
与 Movie 完成记录；WebUI 静态契约覆盖可访问弹窗、搜索和提交入口。
