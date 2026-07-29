# 作品库季度详情与 EP 网格 API（2026-07-29）

## 接口

`GET /api/v1/library/seasons/{tmdbSeriesId}/{seasonNumber}`

- 两个路径参数都必须为正整数；
- 只返回 `tmdbid > 0`、已进入正式作品库的普通季度；
- 不存在时返回稳定错误 `library_season_not_found`。

## EP 与下载状态语义

EP 数组只来自该季度持久化的官方 `tmdb_episodes` snapshot，并按 TMDB Episode Number 排序。`anime_seasons.episode_count` 只用于完整性检查，不用于猜造 snapshot 中不存在的 EP。

只有相同 `(tmdbSeriesId, seasonNumber, episodeNumber)` 的规范 `completion_records` 才产生 `downloaded`；等待下载、下载中、整理失败、snapshot 外旧记录以及 `Other` 文件都不产生已下载格。完成记录被精确删除后，下一次查询立即恢复 `not_downloaded`。

响应返回 TMDB Episode ID、名称、开播日期、时长、snapshot 获取时间、完成来源/时间和 `media_path_known`，但不返回本地媒体路径、内部 SQLite 行 ID、Torrent URL、passkey 或下载器凭据。

## 查询与验证

- 季度头部使用一条批量聚合查询，EP 网格使用一条有序查询；不存在按 EP 或任务逐条读取的 N+1。
- 数据层专项测试：6/6。
- API 专项测试：10/10。
- TypeScript `web:check` / `web:build`：通过。
- Release 全量测试：637/637（Plugin 11、Core 215、Data 108、App 303）。
- `win-x64` NativeAOT 发布：通过，完成 `Generating native code`。
- NativeAOT 可执行文件启动、SQLite schema v23、受限导入和静态 WebUI smoke：通过。
