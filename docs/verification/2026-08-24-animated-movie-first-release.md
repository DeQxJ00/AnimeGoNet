# 动画电影单正片首版验证 — 2026-08-24

## 已交付范围

- `tv` 与 `movie` 由统一导入的 `info.media_type` 显式区分，缺失时兼容为 `tv`。
- TV 使用 `save_path`，电影使用独立 `movie_save_path`；Docker 固定为
  `/download/anime` 与 `/download/movies`，共享同一 `/download` 挂载。
- Mikan 手动 Torrent/RSS 可选择类型；独立 AnimeGoHelper 仓库提交 `1290d42`
  让首页剧场版分区的“单/全”请求传 `movie`，其他入口传 `tv`。
- 保存型 Mikan RSS 来源在 schema v58 持久化媒体类型；WebUI、部署 YAML、配置归档、
  “立即读取”与 Cron 调度使用同一值。旧来源默认 TV，非 Mikan 来源不能保存 movie。
- 电影搜索调用 `/3/discover/movie?with_genres=16`，候选必须再次通过
  `/3/movie/{id}` 验证；不会进入 TV 的季度、EP 或 TMDBFail 链。
- 首版只接受一个主视频，按 TMDB Movie ID 独立 claim/去重，整理到
  `<电影名> (<年份>)/<电影名> (<年份>).ext`；字幕关联主文件并保留语言后缀，
  写入 `movie.nfo`，不生成虚假的 S01E01。
- 下载任务展示 Movie ID、标题和上映日期；动画电影库提供搜索、排序、分页、
  封面、完成状态和 TMDB Movie 跳转；四类删除支持电影完成记录、claim 和电影根目录。

## 自动验证

- `.NET Release` 全解决方案：1888 passed，0 failed，0 skipped。
- 静态 WebUI：43 passed，0 failed。
- 独立 AnimeGoHelper：2 passed，0 failed，且 `node --check AnimeGoHelper.js` 通过。
- `win-x64` Release NativeAOT 完成 `Generating native code`，无构建错误；发布后的
  `AnimeGoNet.App.exe` 通过 `eng/smoke-native.ps1` first-start，验证 schema v58、
  SQLite 初始化、Minimal API、静态 WebUI、WebSocket 与原生运行标识。
- 保存型 RSS 媒体类型的数据层、立即运行和 Cron 调度均有 movie 透传回归测试；
  测试会读取最终 `ingest_tasks.media_type`，不只检查表单或 API 回显。
- 五个目标 RID 与 Docker 构建继续由现有 GitHub Actions 矩阵覆盖；本次未把 Docker
  标记为本机实测，符合项目所有者此前“生成能力保留，Docker 自行测试”的要求。

## 明确未修改或留待扩展

- 正式 AI Prompt 没有修改。现有 AI 输出契约是 TV Series/Season/Episode；电影 AI
  输出字段与 Prompt 必须由项目所有者确认后再加入。
- 多主视频、特典、预告、多个剪辑版/画质版本与 Jellyfin Extras/Versions 尚无已确认
  业务语义，首版使用稳定失败码停止，不会误把多个文件当成同一电影。
- 电影外部媒体扫描、电影详情人工覆盖、电影专用 Other 审核与库级删除按钮属于后续
  增强；现有任务级四类删除已经覆盖电影。
