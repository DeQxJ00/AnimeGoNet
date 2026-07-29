# 作品库季度列表 API（2026-07-29）

## 接口

`GET /api/v1/library/seasons`

- `page`：默认 `1`，正整数。
- `page_size`：默认 `24`，范围 `1..100`。
- `sort`：`last_updated`（默认）、`name`、`air_date`、`added_at`。
- `direction`：`desc`（默认）或 `asc`。

返回稳定 ID `tmdb:{series}:s{season}`、TMDB Series/Season 身份、显示/排序名称、Season 名称、经校验的相对 poster 与来源、三个日期、Episode 总数/snapshot 数/规范完成数、Series/Season 解析来源、验证状态、最近运行 ID 和一致性警告。

## SQL 与边界

- 列表单位固定为正式 `tmdb_series_id > 0 + season_number > 0`；`tmdbid=0` 待补全记录不进入查询。
- count 与页面投影都是批量 SQL，不按作品或 EP 发起后续查询。
- 完成数只计算同时存在于 `tmdb_episodes` snapshot 的规范完成记录；snapshot 外完成记录单独产生警告。
- 最后更新时间聚合 Series/Season、任务、解析运行和完成记录业务时间。
- 名称、日期、加入时间和最后更新时间都在 `LIMIT/OFFSET` 前排序，并追加稳定 TMDB ID/Season tie-breaker。
- `air_date` 的 `NULL` 在升序和降序都固定置后。
- 待补全恢复记录从事务保存的 `manual/automatic` 来源投影为 `pending_tmdb_manual/automatic`。
- 响应不包含媒体绝对路径、内部 SQLite row ID、Torrent URL、passkey 或下载器凭据。

## 自动验证

- Data 测试覆盖默认排序、正式完成进度、snapshot 缺口、snapshot 外完成、媒体路径未知警告、解析来源、兜底排除、空日期双向置后和逐页无重复。
- API 测试覆盖默认 envelope、poster fallback 来源、完成进度、排序分页、稳定参数错误和敏感路径不返回。
- TypeScript strict 检查与确定性构建通过。
- 完整 Release 回归：631/631 通过（插件 11、Core 215、Data 106、App 299）。
- win-x64 `PublishAot=true` 完成 `Generating native code`，0 warning / 0 error；原生进程通过 schema v23、SQLite、静态 WebUI、安全 ingest 与 NativeAOT capability smoke。
