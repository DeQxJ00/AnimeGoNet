# 作品库 Cover 安全代理与缓存（2026-07-29）

## 接口与回退

`GET /api/v1/library/covers/{tmdbSeriesId}/{seasonNumber}`

1. 优先使用该 TMDB Season 的经校验 `poster_path`；
2. Season 无 poster 时使用所属 TMDB Series poster；
3. 都不存在或上游失败时返回内置本地 SVG 占位图；
4. 正式作品库中不存在的季度返回稳定 `library_season_not_found`。

季度列表与详情响应新增同源 `poster_url`，前端不拼接或直连 TMDB 图片地址。

## 安全和缓存边界

- 上游固定为 HTTPS `image.tmdb.org/t/p/w500`，只接受数据库中的 TMDB 相对路径；
- 请求不携带 TMDB API key/Bearer，网络连接复用配置的 TMDB 代理；
- 响应以流式方式限制为 5 MiB，并按 JPEG/PNG/WebP 魔数验证，不信任远端 Content-Type；
- 同一 poster 的并发请求只触发一次上游读取；
- 成功图片按 poster path/size 的 SHA-256 键缓存到 `data_path/cache/covers`，写入使用同目录临时文件后原子替换；
- 非图片、HTTP 失败和超时不写缓存，返回带安全 warning header 的短缓存占位图；
- API 不返回缓存绝对路径、API key、Torrent URL、passkey 或下载器凭据。

## 验证

- 数据层测试覆盖 Season → Series → placeholder 回退与不存在季度。
- 服务测试覆盖首取/缓存命中、并发合并、失败/非法内容降级和密钥不进入 URI。
- HTTP transport 测试覆盖无认证请求、声明长度与无长度流式容量上限。
- API 测试覆盖同源 `poster_url`、响应类型、来源/缓存 headers、占位图和稳定 404。
- TypeScript `web:check` / `web:build`：通过。
- Release 全量测试：646/646（Plugin 11、Core 215、Data 109、App 311）。
- `win-x64` NativeAOT 发布：通过，完成 `Generating native code`。
- NativeAOT 可执行文件启动、SQLite schema v23、受限导入和静态 WebUI smoke：通过。
