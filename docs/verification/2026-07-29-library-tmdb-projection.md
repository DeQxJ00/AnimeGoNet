# 作品库 TMDB 投影基础（2026-07-29）

## 范围

SQLite schema v23 为后续作品库列表、排序、Cover 与 EP 网格补齐权威字段：

- `anime_series.first_air_date`；
- `anime_series.poster_path` 由现有列开始实际写入；
- `anime_seasons.air_date`；
- `anime_seasons.episode_count`，非负且旧记录迁移后为 `0`；
- `anime_seasons.poster_path` 由现有列开始实际写入。

TMDB DTO 和 NativeAOT source-generated JSON 同步读取 Series/Season `poster_path`。路径必须是最多 256 个字符、以 `/` 开头且不含反斜杠或控制字符的 TMDB 相对路径；主程序不会保存任意图片 URL。

自动/人工季度解析共用的 `MetadataResolutionStore` 与“待补全 TMDB”人工恢复使用同一写入语义，保存首播日期、TMDB 总集数和 poster。后续不完整结果不会用空 poster/日期或本地 `0` 集数覆盖已有权威投影。

## 验证

- schema 22 → 23 迁移保留 Series/Season 行，并为旧 Season 设置安全的 `episode_count=0`。
- TMDB 客户端映射 Series/Season poster，拒绝绝对 URL、反斜杠和控制字符。
- 正常季度完成和待补全恢复均断言日期、集数、Series poster 与 Season poster 精确落库。
- 完整 Release 回归：620/620 通过（插件 11、Core 215、Data 102、App 292）。
- win-x64 `PublishAot=true` 完成 `Generating native code`，0 warning / 0 error；原生进程 smoke 通过 schema v23、SQLite 初始化、`native_aot=true`、安全 ingest 拒绝、qB capability 与静态 WebUI 检查。
