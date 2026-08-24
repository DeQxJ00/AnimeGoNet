# TMDB 成功响应缓存验证

日期：2026-08-01

## 范围

- 上游基线：`develop@c7475dfc55a374cd0dd08821bf17125dab1e3145` 的
  `internal/animego/anisource/themoviedb` 与默认
  `advanced.cache.themoviedb_cache_hour=336`。
- .NET 实现：`TmdbCachingClient`、SQLite `cache_buckets/cache_entries`、部署 YAML、
  私有应用覆盖、环境/命令行锁、配置 API 与静态 TypeScript WebUI。

## 业务边界

- Search、Series details、Season、Episode 仅在权威调用成功并完成结构/身份校验后写
  `bolt/themoviedb`；空 Search 是成功响应，可缓存。
- 404/null、网络、超时、429/5xx、认证、配置、协议与调用取消均不写 negative
  cache。缓存存储失败降级为直接使用已成功的 TMDB 响应。
- 当前默认 TTL 为 144 小时，可配置范围 `(0, 8760]`。上游旧配置中的显式 336
  小时继续按迁移值保留；到期项由 SQLite store 惰性删除。
- key 按规范 Base URL、语言、operation 与请求身份 SHA-256 分区；不含 API key 或
  Bearer token，原始搜索词只参与摘要而不单独落库。读取缓存后再次校验
  Series/Season/Episode 父子身份；
  JSON 损坏或身份不一致时删除并回源。
- 序列化只使用 `TmdbJsonContext` source generation，保持 NativeAOT 边界。
- 缓存命中但本次解析所需对象缺失时，只刷新缺失层一次：空 Search、缺失的
  Series/Season、Season 快照缺目标 EP、空 Episode 分别走显式权威刷新。刷新成功覆盖
  同一 key 并重置 TTL；刷新为空、身份无效、Episode 无日期或网络失败均保留旧成功值。

## 验收

- `TmdbCachingClientTests`：跨 client/重启复用、TTL 边界替换、空搜索缓存、404 不缓存、
  无 `air_date` Episode/Season 不缓存且旧条目自动刷新、
  失败不缓存、Base URL/语言隔离、secret/query 不落库、伪造身份回源、取消传播与默认
  DI 装配。
- `TmdbSeriesResolverTests`、`TmdbSeriesSeasonResolverTests` 与
  `EpisodeMetadataResolutionProcessorTests`：三层缺失均只刷新一次；固定
  TMDB `296101/S01` 的旧快照只有 E1–E7，刷新得到 E8 后按 Bangumi 日期确定性完成，
  不进入 AI。
- 配置测试：144 小时默认值、范围校验、规范 YAML、12 份固定上游历史 YAML 的旧
  `themoviedb_cache_hour` 迁移、环境/命令行只读锁、API/WebUI 投影与私有覆盖。
- WebUI：TypeScript strict check、确定性编译和 5 项共享 HTTP client 测试。
- 全量 .NET 测试：Core 330、Data 177、App 780、插件相关 51，合计
  `1338/1338` 通过。
- win-x64 Release `PublishAot=true` 完成 `Generating native code`，无 warning/error；
  `eng/smoke-native.ps1` 的 first-start 与 legacy-yaml-upgrade 两种模式均通过 schema
  v36、静态 WebUI、SQLite、配置迁移、NativeAOT capability 和安全退出门禁。
