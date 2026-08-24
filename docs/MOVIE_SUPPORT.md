# 动画电影适配设计与移植清单

状态：单正片首版已实现并通过回归。TV 现有行为保持不变；历史任务和未传类型的兼容 API 均按 `tv` 处理。

## 已确认边界

- 媒体类型只有 `tv` 与 `movie`，统一导入字段名为 `info.media_type`。
- TV 使用 `save_path`；电影使用独立 `movie_save_path`。Docker 默认分别为
  `/download/anime` 与 `/download/movies`，二者继续共享 `/download` 挂载。
- Mikan 普通页面、RSS 和旧 AnimeGoHelper 请求默认 `tv`；首页最下面的剧场版分区以及
  其“单/全”按钮显式提交 `movie`。
- 电影调用 TMDB Movie API，不把电影伪装成 TV S01E01，也不执行
  TMDBFailSkip/Backtrace/UseTitleSeason/UseFirstSeason 季度链。
- 现有正式 AI Prompt 不自动修改。电影 AI 输出契约需要单独确认。

## 1:1 可追踪实现清单

| 模块 | TV 基准 | Movie 目标 | 状态 | 验收 |
|---|---|---|---|---|
| 配置 | `paths.save_path` | `paths.movie_save_path` | 已实现 | Core/App 配置测试、Web 测试 |
| 任务身份 | 默认 TV Episode | `media_type=movie` | 已实现基础字段 | schema v56、导入存储测试 |
| 手动导入 | 单 Torrent/RSS | 类型下拉并透传 | 已实现 | API/WebUI 测试 |
| 保存型 RSS 来源 | 默认 TV | SourceProfile 持久化 tv/movie，立即运行与 Cron 共用 | 已实现 | schema v58、API/调度测试 |
| AnimeGoHelper | 单/全默认 TV | 剧场版按钮传 movie | 已实现于独立仓库 `1290d42` | Node DOM/请求契约测试 |
| TMDB 搜索 | `/3/discover/tv?with_genres=16` | `/3/discover/movie?with_genres=16` | 已实现 | loopback HTTP 测试 |
| TMDB 详情 | Series/Season/Episode | `/3/movie/{id}` 并再次验证身份 | 已实现 | DTO/AOT 与缓存测试 |
| 规范身份 | Series+Season+Episode | Movie ID | 已实现 | schema v57、唯一 claim/完成记录测试 |
| 整理 | `<show>/Sxx/E###` | `<movie> (<year>)/<movie> (<year>).ext` | 已实现（单正片） | 端到端文件策略测试 |
| NFO | `tvshow.nfo` + Episode | `movie.nfo` | 已实现 | XML 测试 |
| 字幕 | EP 关联后同名 | 主电影文件关联后同名 | 已实现 | 多语言后缀测试 |
| 作品库 | Season/EP 进度 | 电影状态、封面、TMDB 跳转 | 已实现 | API/WebUI 测试 |
| 删除/扫描 | TV 目录边界 | movie 根目录边界 | 删除已实现；外部扫描待实现 | 删除计划/执行测试 |

## 仍需明确或容易遗漏

1. 电影默认采用 Jellyfin 常见目录 `<片名> (<年份)>/<片名> (<年份>).ext`。同一 Torrent
   有正片、特典、预告或多版本时不能全部当作同一主文件；需要定义 Extras/Versions 规则。
2. 电影去重应按 TMDB Movie ID，而不是 Series/Season/Episode；同一电影的不同分辨率、版本、
   剪辑版是否允许并存，需要单独策略。
3. Bangumi 的剧场版 Subject 与 Mikan `mikanid/groupid` 映射仍可复用，但 P3 季度回溯和可信
   EP Offset 对电影不适用，必须在媒体类型边界前停止。
4. qBittorrent 路由目前按输入源绑定。若以后希望 TV 与 Movie 自动走不同 qB 实例/分类，需新增
   媒体类型级路由覆盖；本阶段保持来源路由不变。
5. 外部媒体扫描、删除计划、备份导入导出、Docker 路径探测和磁盘容量统计必须同时覆盖
   `movie_save_path`，不能只改下载后的移动目标。

## 当前确定性流程

1. Mikan/手动导入把 `info.media_type=movie` 写入任务；保存型 Mikan RSS 来源则从
   SourceProfile 读取同一类型，点击立即运行和 Cron 调度不会退回 TV；缺失时仍按 `tv`。
2. 电影任务只接受一个主视频；零视频或多个视频使用稳定失败码停止，不进入 TV 的 EP/季度流程。
3. 可用 bgmid 时读取 Bangumi `name` 与 `name_cn` 作为搜索词，再执行多轮标题清理搜索。
4. 每个候选先来自 `/3/discover/movie?with_genres=16`，随后必须由 `/3/movie/{id}` 验证。
5. 通过 Movie ID 独立去重，整理到 `movie_save_path`，字幕随主文件改名并保留语言后缀，写入 `movie.nfo`。
6. 下载任务和“动画电影”库显示 Movie ID、标题、上映日期和完成状态，不显示伪造 EP 进度。

当前没有修改正式 AI Prompt；多主视频/特典/多版本、电影 AI 契约和电影外部媒体扫描仍需单独确认或实现。
