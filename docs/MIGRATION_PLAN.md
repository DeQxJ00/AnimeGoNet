# AnimeGo → .NET 10 / NativeAOT 移植计划

## 1. 目标与完成标准

目标是在 `net10.0` 上重建 AnimeGo `develop@c7475df` 的完整可观察行为，并留下便于继续加功能的模块边界。

项目只有同时满足以下条件才算完成首个可用移植版：

- 默认配置可以从零启动，旧 YAML 配置可以读取和升级。
- 内置订阅、解析、过滤、重命名和定时任务无需外部 Python 即可运行。
- 项目下载器只支持 qBittorrent，但支持多个命名实例并通过真实容器集成测试；Transmission 明确排除，不在后续路线图中。
- RSS → 解析 → 去重 → 下载 → 做种 → 重命名/移动 → NFO 的端到端链路通过。
- 上游 OpenAPI 的 11 个 REST operation 和 1 个 WebSocket operation 全部通过契约测试；旧文档中的“10 个 HTTP API”仅为早期误计数。
- `win-x64` 与 `linux-x64` 的 NativeAOT 产物发布成功、无未批准的 trim/AOT 警告，并用发布后的原生二进制重跑 smoke/E2E。
- Docker 镜像用 NativeAOT 二进制运行，数据卷路径与原项目兼容。
- 新增 Web 管理页面，能完成日常查看、配置和操作，不只是 Swagger/占位页。
- 每个功能模块的实现、测试、文档在测试通过后独立提交。

## 2. 必须先确认的产品决策

计划可以按推荐默认值开工，但在相关模块进入实现前需要确认：

| 决策 | 推荐默认值 | 影响 |
|---|---|---|
| 首发平台 | 已确认：`win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`、`osx-arm64` | 五个 RID 均须在对应原生 GitHub runner 发布并执行 smoke。 |
| 内置插件 | 已确认：上游五类 builtin 与 MikanTool 全部 C# 化；新增 source adapter 第六类 | Mikan/U2/TTG 输入源显式注册；MikanTool 默认启用；空规则等价于全部放行，不依赖 Python。 |
| 扩展插件 | 已确认：完全移除 Python；官方 C# 插件编译期注册，第三方动态插件使用独立 C# 可执行程序 | NativeAOT 主程序不加载程序集；外部插件可独立发布、安装、升级和隔离。 |
| 旧 `.bolt` 数据 | 已确认：新程序直接使用 SQLite，不实现 .NET Bolt 二进制解析 | 配置和媒体 JSON 直接兼容；需要时用可选旧 Go 导出器转 JSON。 |
| Web UI 技术 | 已确认：静态 TypeScript/HTML/CSS，无 Vue/React 等重型运行时框架 | TypeScript 构建后作为静态资源随 AOT 主程序发布；生产环境不包含 Node.js。 |
| Web UI 首版范围 | 仪表盘、手动 RSS/下载、下载状态、配置、插件、缓存、实时日志 | 这是相对上游的新增功能，兼容 API 与新增 UI API 必须分开版本化。 |
| 配置/数据边界 | 已确认：YAML 管启动、部署、连接和数据更新策略；SQLite 管动画、订阅、过滤、下载、任务和数据版本 | 环境变量优先于 YAML；被覆盖字段在 Web 中只读显示。 |
| 本地访问 | 已确认：桌面默认 `127.0.0.1:7991`；Docker 默认 `0.0.0.0:7991` 且必须配置 Access Key | 不做多用户系统；保留 AnimeGoHelper 所需认证和最小 CORS。 |
| Docker 路径 | 已确认：Docker 默认 YAML 与 Compose 使用同一套绝对路径 | `data_path=/data`、`download_path=/download/incomplete`、`save_path=/download/anime`，保证配置可见且硬链接不跨卷。 |
| AnimeGoHelper | 已确认：原脚本不修改 | 路由、请求字段、Base64、响应 envelope、认证和过滤行为都做契约测试。 |
| 数据更新 | 已确认：完全由 YAML 配置驱动 | 开关、Cron、manifest、自动下载/导入、保留版本数可配置并由 Web 安全编辑。 |
| TMDB 规范命名 | 已确认：TMDB 匹配成功时名称、季度和集号全部以经官方 API 验证的 TMDB 数据为准 | TMDB 语言固定 `zh-CN`，中文名缺失时仍使用 TMDB `original_name`；Bangumi/文件名值仅保留为来源字段。 |
| Mikan 人工规则 | 已确认：人工覆盖最高优先级；Mikan URL 中的作品 ID 统一称 `mikanid` | 相同 `mikanid` 视为同一作品，共享 `bgmid`、TMDB Series/Season 和 Episode Offset；自动解析不得覆盖。 |
| 多源路由 | 已确认：多个命名下载器实例；首版正式来源仅 Mikan，U2/TTG 暂缓 | Mikan（bgmid必填）可绑定 `bt`；U2/TTG 的 adapter/API/`pt` 路由仅保留未来扩展骨架，不生成默认 SourceProfile 或文件策略。 |
| AI 匹配 | 已确认：确定性季度链为 `TMDBFailSkip=4`、`TMDBFailBacktrace=3`、`TMDBFailUseTitleSeason=2`、`TMDBFailUseFirstSeason=1`；AI 是一个独立、默认关闭的任务级开关 | 每个任务最多一次调用和一个 Prompt，发送总标题、候选视频相对文件名/字节容量及可空 `bgmid`/`anidbid`/`imdbid`，同时返回 Series/Season/全部 Episode；非空 ID 与任务作品级绑定但跨站标题/季度/EP 仅供参考；最终结果必须由 TMDB 验证。P2/P1 是明确的本地 Season 回退例外，不验证 TMDB Season。 |
| 在线数据源测试 | CI 默认回放 fixture；手动/定时任务运行受控 live smoke | 避免 Mikan/Bangumi/TMDB 波动导致 CI 不稳定。 |

### C# 插件模型

具体接口、manifest、进程协议、SDK 和提交拆分见 [`PLUGIN_ARCHITECTURE.md`](PLUGIN_ARCHITECTURE.md)。

多输入源、下载器实例、通用 API、路由快照和 Web UI 契约见 [`SOURCE_ROUTING.md`](SOURCE_ROUTING.md)。

NativeAOT 不支持 `Assembly.LoadFile` 等动态程序集加载，也不支持运行时代码生成。因此不采用“把 DLL 扔进 plugins 目录再反射加载”的传统插件模型，而采用两层设计：

1. **编译期插件**：官方 source/feed/parser/filter/rename/schedule 和 MikanTool 实现强类型接口，通过显式 `PluginCatalog`/DI 注册并随主程序一起 AOT 发布。新增或替换这类插件需要重新构建 AnimeGoNet。
2. **外部进程插件**：需要独立安装或升级的第三方 C# 插件发布为自包含可执行程序，主程序读取 `plugin.json` 后通过 stdin/stdout JSON Lines 协议调用。插件本身推荐 NativeAOT，并为每个目标 RID 单独发布；主程序始终不加载插件程序集。

共同要求：

- `AnimeGo.Plugin.Abstractions` 定义六类强类型接口、稳定 DTO、错误和取消语义。
- `AnimeGo.Plugin.Sdk` 提供外部插件协议循环、source-generated JSON、日志和健康检查帮助类。
- manifest 固定 `id`、`version`、`apiVersion`、`type`、入口文件、支持 RID、配置 JSON Schema 和声明能力。
- 协议至少包含 `initialize`、`execute`、`health`、`shutdown`；每条消息带 request ID 和协议版本。
- stdout 只允许协议 JSON，日志写 stderr；限制启动/执行超时、消息大小、并发数和工作目录。
- 外部程序不是安全沙箱；仅运行用户信任的插件。Web UI 安装时显示可执行文件和能力声明，默认不自动下载或执行未知插件。
- 旧 `type: py/python` 配置只做迁移诊断：已知 builtin/MikanTool 映射为 C#；未知脚本明确报“不再支持”，不静默忽略。

## 3. 建议技术栈

### 生产代码

- .NET 10 / C#，`net10.0`。
- Generic Host + `BackgroundService` 管理客户端、下载器、重命名器和调度器生命周期。
- ASP.NET Core Minimal APIs，`WebApplication.CreateSlimBuilder`，原生 WebSocket，静态文件。
- Web 前端固定使用静态 TypeScript/HTML/CSS，不使用 Vue/React 等重型运行时框架；生成的浏览器 JavaScript 与静态资源嵌入发布物，Node.js 只存在于构建/测试阶段。
- `System.Text.Json` source generation；固定跨边界 DTO 注册到 `JsonSerializerContext`，配置等开放 JSON 使用 `JsonDocument`/`JsonNode`。
- `HttpClient`/`SocketsHttpHandler`；显式实现超时、代理、重试等待、Host 重定向和 Cookie。
- AI 首版使用 OpenAI-compatible HTTP API，不依赖厂商 SDK；固定请求/响应 DTO 及 `JsonSerializerContext`，确保 NativeAOT 可分析。
- `Microsoft.Data.Sqlite` + 显式 SQL，不使用 EF Core；表结构实现 bucket/key/value/ttl 和下载索引。
- YAML 先使用 `YamlDotNet.RepresentationModel` 做 AST 级读写，再显式映射到 DTO，避免反射式对象绑定。Phase 0 必须以 AOT 原生二进制验证。
- RSS/XML 使用 `XmlReader`/LINQ to XML；torrent 使用自有最小 Bencode 读取器以保证 info-hash 行为可控。
- Cron 使用支持六字段/秒的解析器；候选库必须先通过 AOT spike，否则实现受测试约束的最小解析器。
- 日志基于 `Microsoft.Extensions.Logging`，补充轻量滚动文件 provider 和 WebSocket fan-out，不引入动态模板编译。
- HTML 解析器、YAML、Cron、SQLite 是 Phase 0 的四个依赖验证点；没有 AOT 证据前不锁死包版本。

### 测试与工程

- 单元/组件测试使用 xUnit 和 `Microsoft.Testing.Platform`。
- 契约/Golden Master 测试复用上游 `test/testdata` 和 `assets/plugin/*/testdata`。
- HTTP 使用本地 fixture server；时间、文件系统、进程、HTTP、随机数通过小接口隔离。
- 首版下载器真实集成使用 Docker Compose 中隔离的多个 qBittorrent；另一个 qBittorrent 只作为本地 fixture seeder。
- 覆盖率分别统计 Core/Application/Infrastructure；排除生成代码，不用单一总覆盖率掩盖核心状态机缺口。
- CI 同时跑普通 JIT 测试和 NativeAOT publish + published-binary smoke。

## 4. 目标解决方案结构

```text
AnimeGoNet.slnx
src/
  AnimeGo.Domain/                 # 模型、值对象、状态机、纯算法
  AnimeGo.Application/            # 用例、接口、流程编排
  AnimeGo.Plugin.Abstractions/     # 六类 C# 插件契约和稳定 DTO
  AnimeGo.Plugin.Sdk/              # 外部进程插件协议与开发 SDK
  AnimeGo.Infrastructure/         # HTTP、SQLite、文件、客户端、插件进程
  AnimeGo.Web/                    # Minimal API、WebSocket、静态资源
  AnimeGo.Host/                   # CLI、DI、生命周期、NativeAOT 入口
  AnimeGo.PluginTool/             # 对应上游 AnimeGo-plugin
tests/
  AnimeGo.Domain.Tests/
  AnimeGo.Application.Tests/
  AnimeGo.Infrastructure.Tests/
  AnimeGo.Web.ContractTests/
  AnimeGo.ParityTests/
  AnimeGo.E2E.Tests/
web/
  animego-web/                    # 静态 TypeScript/HTML/CSS 管理页面
testenv/
  compose.yml
  fixtures/
  scripts/
eng/
  verify.ps1
  verify.sh
  publish-aot.ps1
docs/
```

依赖只允许从外向内：Host/Web/Infrastructure → Application → Domain。插件、下载客户端、缓存和数据源都通过显式接口注册；禁止扫描程序集自动发现实现，因为这既不利于扩展边界，也不利于 AOT。

## 5. 分阶段实施与提交边界

每一阶段都必须先完成本阶段验证，再产生 Git 提交。阶段中若内容过大，可按下面列出的子模块继续拆分，但不能跨阶段混合提交。

### Phase 0：基线与 AOT 风险消除

产物：

- 在 Linux 容器运行上游 Go 测试，保存基线结果。
- 建立上游行为清单、fixture 副本校验值和 API OpenAPI 快照。
- 创建最小 `webapiaot`/Worker 原型，验证 WebSocket、静态文件、SQLite、YAML AST、Cron、HTML 解析器。
- `Directory.Build.props` 从第一天启用 nullable、warnings-as-errors、trim/AOT analyzer、deterministic build。
- CI 建立 `dotnet test`、`dotnet publish -r win-x64/linux-x64` 和原生 smoke。

提交建议：

- `build: scaffold net10 solution and native-aot gates`
- `test(parity): capture AnimeGo develop baseline fixtures`
- `build: prove native-aot infrastructure dependencies`

门禁：两个 Tier-1 RID 发布；无未登记 IL2xxx/IL3xxx；原生进程 `/ping` 和 WebSocket smoke 通过。

### Phase 1：领域模型与序列化契约

- 移植 Anime、Episode、FeedItem、TorrentItem、ClientEvent、Plugin、RenameResult 等模型。
- 将来源元数据与媒体库规范元数据拆分；至少保留 `SourceName`、`SourceEpisodeNumber`、`TmdbSeriesName`、`TmdbSeasonNumber`、`TmdbEpisodeNumber` 和 `TmdbEpisodeId`。
- 增加 `MikanWorkMetadataRule`，以正整数 `MikanId`（API/SQLite 字段 `mikanid`）为唯一作品键，保存关联 `BangumiSubjectId`、统一 `TmdbSeriesId`、`TmdbSeasonNumber`、有符号 `EpisodeOffset`、启用状态、版本和审计时间；上游 `bangumiId` 只作为兼容输入别名。
- 为最终媒体字段分别保存 `TmdbSeriesResolutionSource`、`TmdbSeasonResolutionSource` 和 `TmdbEpisodeResolutionSource`，避免一个含糊来源字段无法解释 Series、Season、Episode 来自不同阶段。
- 固化枚举/字符串值、hash/key/full-name、NFO XML、JSON 字段名和零值规则。
- 建立 `JsonSerializerContext` 和 golden tests。

提交：`feat(domain): port models and serialization contracts`

门禁：上游 JSON fixture 反序列化/再序列化语义一致；hash、命名、NFO 快照一致；AOT publish 仍为零新增警告。

### Phase 2：配置、路径与内置资源

- 兼容 `-config/-debug/-web/-backup` 及四个同名环境变量。
- 兼容全部 `ANIMEGO_*` 配置覆盖、相对路径规则和默认目录。
- 首次生成带注释 YAML；实现 `1.1.0` → `1.7.1` 的升级与备份。
- 新增 `TMDBFailBacktrace`、独立 `TMDBFailUseAIMatchSeason` 和后置 `TMDBFailEpUseAIMatchSeason`；全部默认 `false`，确定性季度契约固定 Skip=4、Backtrace=3、TitleSeason=2、FirstSeason=1。
- 增加 `ai` 配置段：OpenAI-compatible `base_url`、`api_key`、`model`、超时、重试和置信度阈值；环境变量覆盖的密钥在 Web 中只读且不回显。
- 释放内置插件资源，不覆盖用户修改。

提交：`feat(config): port yaml defaults env overrides and upgrades`

门禁：12 份历史配置 golden test；默认 YAML 快照；Windows/Linux 路径测试；原生二进制首次启动目录快照。

### Phase 3：SQLite 缓存、目录数据库与可选旧数据导出

- 实现 SQLite bucket/key/value/TTL/批量/删除接口，API 层仍可保留 `/api/bolt` 兼容名称。
- 实现媒体目录 JSON 扫描、anime/season/episode 索引和写入。
- 实现全局 TMDB Episode 去重索引、来源 alias、完成记录删除和 `tvshow.nfo` 更新。规范键固定为 TMDB Series/Season/Episode；Mikan/U2/TTG 来源键只作 alias 和审计。删除规范完成记录时同时失效全部 alias。
- 不在 .NET 主程序解析 Bolt；若确有历史数据保留需求，提供独立旧 Go 导出器，将已知 bucket 导出为带 schema 的 JSON，再由 .NET 幂等导入。
- 已实现的 schema-v1 工具链固定主库五个 bucket 与归档库 `bangumi_sub`，导出只读且拒绝覆盖；导入采用 schema v39 内容指纹审计和单事务 upsert，相同包复跑不覆盖后续缓存。用户步骤见 `LEGACY_DATA_MIGRATION.md`。

提交：

- `feat(storage): add sqlite cache with ttl semantics`
- `feat(database): port media directory database and nfo output`
- `feat(migrate): import optional legacy json export`

门禁：崩溃后重开、TTL 边界、并发写、可选 JSON 重复导入、已有媒体扫描、NFO 快照全部通过。

### Phase 4：网络层、RSS 与 torrent

- 复刻 User-Agent、代理、超时、重试、重试等待、Host redirect、Mikan Cookie、TMDB key query。
- 复刻 RSS 文件/URL/字节输入和错误分类。
- 解析 `.torrent`、magnet、info hash、文件列表、单/多文件 torrent。

提交：

- `feat(http): add compatible request policies and source routing`
- `feat(feed): port rss parsing`
- `feat(torrent): port bencode torrent and magnet parsing`

门禁：本地 fixture server 的请求录制一致；所有上游 RSS/torrent fixtures 一致；超时/重试可用虚拟时间稳定测试。

### Phase 5：Mikan、Bangumi、TMDB 数据源

- Mikan 页面解析和 `mikanid`/Bangumi Subject ID/字幕组信息；从 `/Home/Bangumi/{mikanid}` 及 RSS 链路稳定取得 `mikanid`，同 ID 的不同标题、字幕组和 Torrent 归入同一作品作用域。
- Bangumi API + Archive 缓存读取/刷新/锁。
- TMDB 搜索、相似度、季度/首播日期匹配及 fallback。
- 明确区分 Mikan `bangumiId`、Bangumi Subject ID、TMDB Series ID、Season Number 和 AnimeGoNet 内部 ID；详细状态机见 [`METADATA_RESOLUTION.md`](METADATA_RESOLUTION.md)。
- “TMDB 完全失败”与“已经得到 TMDB ID 但季度未匹配”分开；确定性链按适用条件执行 `TMDBFailSkip=4`、`TMDBFailBacktrace=3`、`TMDBFailUseTitleSeason=2`、`TMDBFailUseFirstSeason=1`，AI 是独立可选阶段，Backtrace 在没有 `bgmid` 时不适用。P2 仅本地解析任务 `title`，P1 固定本地 `S01`，均不验证 TMDB Season。
- AnimeGoNet 新增 `TMDBFailBacktrace` / `advanced.default.tmdb_fail_backtrace`（默认 `false`），不属于 AnimeGo `develop` 的原始行为：当前作品无法联合确认 `tmdbid + Season` 时，沿 Bangumi“前传”关系逐项回溯；每个前作分别使用 Bangumi 日文原名、中文名和该前作首播日期重新搜索并验证完整 `tmdbid + Season`，允许命中与当前搜索候选不同的 TMDB Series。
- 回溯实现必须可取消并使用 visited Subject ID 防止关系环；缺日期时继续查找其前传，多前传按最近关系优先且稳定排序；回溯耗尽后继续较低优先级策略。
- 新增规范键 `ai_use_metadata_match`（默认 `false`）：每个下载任务最多一次大模型业务调用，使用总标题、候选视频的相对文件名/字节容量及可空 `bgmid`/`anidbid`/`imdbid`，通过唯一 Prompt 同时返回整个任务的 TMDB Series/Season/Episode 候选。Mikan日期优先开关开启且单文件、`bgmid/pubDate` 等运行条件满足时，主程序先按Bangumi播出日期计算 `bgm_episode_candidate`；候选成功后才开启固定Prompt区块，结合文件名EP定向查询TMDB。非空 ID 已由来源链路绑定当前任务，但只提供作品级上下文；跨站标题、季度拆分和 Episode 编号可不同，不能直接复制。旧 `ai_use_season_match`/`ai_use_episode_match` 只作升级读取。完整协议见 [`AI_METADATA_MATCHING.md`](AI_METADATA_MATCHING.md)。
- 本地桥接 TMDB/Bangumi Streamable HTTP MCP为模型 function tools；TMDB始终启用，Bangumi仅有 `bgmid` 时启用。`anidbid` 存在时增加固定 AniDB→`tmdbtv` 候选工具；适用 MCP不足后才启用 Web Search。
- `imdbid` 存在时通过 TMDB MCP external ID/find endpoint 查询固定候选，拒绝 Movie 并逐级验证 TV Series/Season/Episode。
- 季度由确定性策略确认后，若一个或多个来源 EP 无法对应且任务从未尝试过 AI，则由同一 `ai_use_metadata_match` 开关首次触发共享任务级匹配；模型返回的 `tmdb_id` 和已确认季度必须与上下文一致，否则拒绝。季度阶段无论成功或失败尝试过 AI，Episode 阶段都不得再次调用。
- 实现 Mikan 单文件 `pubDate` Prompt门禁和显式开关：无偏移时间按SourceProfile时区（默认 `Asia/Shanghai`）规范化；严格按Torrent实际文件条目数判断。条件满足时主程序先计算最近普通EP并写入 `bgm_episode_candidate`，候选成功后Prompt结合文件名EP查询TMDB；失败后继续通用流程。独立测试程序允许手工输入候选验证同一门禁和Prompt。
- 将上游 `assets/plugin/filter/Auto_Bangumi/raw_parser.py` 移植为 C# 内置解析器。Mikan RSS SourceProfile 在本地为每个文件重算 `file_episode_candidate`，但不把它或偏移发送给 AI。主程序在逐文件 TMDB 验证后计算 `episode_offset=TMDB EP-file candidate`，只有同一普通季度内结果统一才写缓存学习证据，详见 [`MIKAN_EPISODE_OFFSET_CACHE.md`](MIKAN_EPISODE_OFFSET_CACHE.md)。
- 增加默认关闭且只在主程序中实现的 `(mikanid,groupid)` 可信偏移缓存：来源 EP 是 Torrent 视频文件名解析出的 EP，不同文件名 EP 的已验证 `tmdb_id+season+offset` 完全一致并达到可配置门槛（默认 3）才升级为可信；重复 EP 不计数，任一冲突重置学习/撤销可信。主程序在 AI 调用前命中包含有效 `tmdb_id`、普通 `season` 和偏移的可信记录后，直接本地计算目标 EP 并跳过 AI，不为该次命中再逐集请求 TMDB。
- AI 返回值不直接进入领域模型。主程序查询 TMDB `tv/{id}`、`tv/{id}/season/{season}` 和目标 Episode 验证存在性、TV 类型、日期/标题对应关系后，才生成规范字段。
- 元数据入口先应用启用的 Mikan 作品级人工规则；命中后统一使用规则中的 Bgm/TMDB Series/Season，并按 `TmdbEpisodeNumber = SourceEpisodeNumber + EpisodeOffset` 映射普通正片。人工规则高于所有自动策略，无效时显式报 `ManualOverrideInvalid`，不能静默回退覆盖。
- 每个最终 Series/Season/Episode 写入值同时保存取得阶段和不可变的解析 Run/Attempt 引用。Series/Season 证据保存在完成的 `metadata_resolution_runs`；Episode 证据按 `task_files` 逐文件保存，字幕关联使用自己的 `subtitle_association` Attempt，不能借用视频或同策略最后一次 Attempt。SQLite schema v32 触发器验证 Attempt 属于同一任务/Run、正确 stage、相同 strategy 且结果为 `matched`；失败分类和具体原因继续在 Web UI 中可见。
- SQLite schema v33 将路由快照中的做种分钟固化到 download job，保存 qB 累计做种秒数、`not_required/waiting/seeding/completed` 与首次完成时间；累计值和完成状态只能前进。`wait_move` 和 `link_delete` 后续动作只依赖该持久化门禁，因此 qB 暂时离线、进程重启或 SourceProfile 修改不会改变既有任务语义。
- SQLite schema v34 将来源动态 tag 模板固化进任务 route snapshot，并在 download job 保存实际 tag、`pending/applied/skipped/not_configured` 和稳定失败码。只在规范 TMDB 元数据确认后、qB 仍暂停时展开上游兼容日期/季度/EP变量；缺元数据可审计跳过，qB 写入失败沿下载准备租约重试。
- SQLite schema v35 将正常整理产生的 `completion_aliases` 纳入通用查询，并在 Mikan RSS 批次条目保存早期 completion 命中证据。完成记录、来源 alias 与 Episode claim 同事务提交；RSS 在 Bangumi/Torrent 网络访问前以 IMMEDIATE 事务按 `source+mikanid+来源EP` 早停，并在 staging 前再次事务复查后 claim。删除业务完成记录时 alias 和命中证据由外键级联清除，后续同批次可重新导入。
- SQLite schema v36 为每个 Mikan SourceProfile 增加只写 RSS URL、调度开关/Cron 和最近运行审计。编译期 `mikan-rss-ingest-schedule` 只从调度参数接收来源 ID/revision，执行时读取当前 URL 并进入同一 Mikan RSS 规则/去重/统一导入链；旧 revision、重叠运行、后台禁用和重启中断均有显式门禁。
- SQLite schema v37 为每个 download job 增加持久化媒体整理阶段与单位进度。重命名规划、媒体/字幕传输、NFO、目录索引、下载器清理逐阶段上报；失败重试保留可审计阶段，已完成文件 operation 可续算，清理租约恢复不得重新执行文件工作。
- SQLite schema v38 为每个 SourceProfile 增加默认开启的重复命中通知开关。RSS 使用批次 profile 快照，统一导入写入不可变任务路由快照；关闭只抑制脱敏日志/WebSocket 事件，不得改变全局完成记录、逐文件去重或下载门禁。
- SQLite schema v39 增加旧 Go cache JSON 的内容指纹迁移审计；报告不保存或展示原始 key/value，重复包不覆盖新缓存。
- 正常取得 TMDB ID 时，在动画根目录 `tvshow.nfo` 默认只写真实 `<tmdbid>`；仅在 `write_bangumi_id_when_tmdb_matched` 显式开启时附加对应 `<bangumiid>`。
- TMDB 完全失败兜底开启、权威TMDB访问成功并确定无匹配、Bangumi Subject ID有效且季度已确定时，继续下载/刮削，并在 `tvshow.nfo` 固定写 `<tmdbid>0</tmdbid>` 和对应 `<bangumiid>`。
- 兜底关闭或兜底前置条件不满足时不继续下载/刮削，也不生成失败 NFO；不得只写 `tmdbid=0`。
- TMDB 后续补全成功时原子更新 `tvshow.nfo`，用真实 TMDB ID 替换 0；默认同时移除兜底 `bangumiid`，显式开关开启时才保留。

提交：

- `feat(download): port manager state machine and notifier`
- `feat(rename): port file strategies and naming pipeline`
- `feat(scrape): write media database and jellyfin nfo`

门禁：状态迁移表覆盖；四种策略在临时卷真实文件系统通过；不允许删除测试根目录外文件；端到端合法小文件下载通过。

### Phase 10：调度器与生命周期

- 六字段 Cron、NextTime、StartRun、插件变量、并发和异常隔离。
- Bangumi Archive、数据库刷新、feed、schedule 插件任务。
- Ctrl+C/SIGTERM、5 秒退出上限、HTTP/WS/客户端/数据库有序关闭。

提交：`feat(schedule): port cron tasks and graceful shutdown`

门禁：虚拟时间测试；重复启动/停止；长任务取消；原生进程 SIGTERM 后数据无损。

### Phase 11：Web API、WebSocket 与 Web 管理页面

- 精确复刻 `/ping`、`/sha256`、8 个 `/api/*` 路由和 `/websocket/log`。
- 兼容 access-key SHA-256 校验和错误响应 envelope。
- 兼容 config、plugin config、cache/bolt、RSS、download manager 行为。
- 兼容 `DeQxJ00/AnimeGoHelper` 油猴脚本的现有调用：
  - `POST /api/rss` 用于全集或指定集订阅，进入 RSS → MikanTool filter → parser → download 流水线。
  - `POST /api/download/manager` 用于已取得 torrent URL 的单集快速下载，按上游语义跳过 filter，但仍解析并进入下载管理器。
  - `GET/POST /api/plugin/config` 使用规范插件名 `inner_plugin_mikan`，同时接受旧插件名 `filter/mikan_tool.py`、Base64 JSON 和原响应 envelope；内部映射到 SQLite 规则，不写 Python 插件文件。
  - 保留 `/ping`、SHA-256 `Access-Key`、Tampermonkey 跨域请求和 Mikan 来源的最小 CORS 兼容。
- 生成 OpenAPI，并新增独立版本的 UI 查询/命令 API，避免改变旧接口。
- 新增 `POST /api/v1/ingest`，沿用并强类型化 Mikan 的 `source + data[].torrent + data[].info` 批量格式，统一接收标题、Torrent、source item/work ID 和可空 bgmid/anidbid/imdbid；旧 Mikan API 转换到同一 command。新增下载器实例/SourceProfile CRUD、连接测试、路由预览和引用保护 API。
- passkey Torrent URL 只在受认证入口和受限抓取器内使用；按 SourceProfile host白名单逐跳校验DNS/redirect，限制响应并验证bencode/info-hash，日志/API/Web只留不可逆指纹，临时torrent用受限权限和TTL清理，不进入AI或普通备份。
- Web 页面提供：
  - 仪表盘：服务/下载器连接状态、任务数量、最近错误、下次调度时间。
  - 订阅与下载：提交 RSS/种子 URL、查看处理结果、允许的手动操作。
  - 下载状态：统一展示多个qBittorrent实例的规范状态、百分比、容量、速度、ETA、Seeds/Peers和文件级priority，同时独立展示AnimeGoNet解析/移动/重命名/字幕/NFO/数据库阶段；qB 100%不等于业务完成。提供暂停、恢复、业务重试及删除中心跳转，详细边界见[`DOWNLOAD_PROGRESS_UI.md`](DOWNLOAD_PROGRESS_UI.md)。
  - 下载器实例：多实例 CRUD、连接/版本/延迟/任务数、路径与硬链接探测、来源引用；活动引用存在时禁止直接删除。
  - 输入源路由：下载器绑定、ID字段约束、过滤/匹配 profile、category/tag、文件/做种策略、重复命中通知和模拟路由预览。
  - 配置：表单编辑、服务端校验、明文变更 diff、保存前 revision 备份、即时/重启生效提示；原始部署 YAML 保持运维只读，不在 Web 展示或改写。季度失败区显示四个确定性策略及一个任务级 AI 元数据开关，AI/TMDB 密钥直接回填。
  - Mikan 过滤：内置 C# 复现 `Filiter0`～`Filiter4` 的旧规则和 AnimeGoHelper Base64 配置接口；默认 Mikan SourceProfile 提供默认开启的 RSS 过滤总开关，Web 提供开关、五档规则编辑、真实顺序预览、legacy JSON 导入导出、revision 冲突及快照回滚，详见 [`MIKAN_FILTER_COMPAT.md`](MIKAN_FILTER_COMPAT.md)。
  - Mikan 同集优选：一次 RSS 批次内按可靠的 `mikanid+来源EP` 聚合重复选项，使用完全可配置的有序优先级组和组内 `{name, values[]}` 具名数组逐级淘汰，剩一个立即短路；Web 可独立启停、任意增删/排序组与数组并维护具名黑白名单，预设字幕语言/封装/编码/分辨率四组并默认拒绝720p，详见 [`MIKAN_RSS_PRIORITY.md`](MIKAN_RSS_PRIORITY.md)。
  - 动画作品：按 `mikanid` 查看和编辑作品级人工规则，包括关联 Bgm Subject、TMDB Series/Season 与 Episode Offset；保存前预览受影响的未完成任务和样例 EP，支持禁用、清除和显式重新匹配。
  - 作品库：以已验证的 `TMDB TV Series + 普通 Season` 为列表单位，展示 TMDB 动画名称、季度/剧集 Cover、Season 和 EP 网格；EP 的全集与下载完成标记只认 TMDB Episode 及规范完成记录，不使用来源集号补齐。支持按最后更新、最后 EP 变动、TMDB 名称、TMDB Season 开播日期和本地加入日期稳定排序，详细语义见 [`WEB_UI.md`](WEB_UI.md)。
  - TMDB 获取方式：作品详情分别显示 Series、Season、Episode 的取得阶段、人工规则 `mikanid`/偏移、验证状态、最后解析时间和策略时间线。
  - 删除中心：提供删除业务记录、删除下载器任务、删除下载源文件、删除媒体库文件四种独立能力，也允许在影响预览后组合执行；业务记录中可精确删除某集的已下载完成记录，解除 RSS 去重门禁；默认不隐式级联。
  - 元数据失败中心：按阶段、错误码、可重试性和处理状态筛选，显示最终失败原因及策略尝试时间线，支持对单项安全地重新匹配。
  - 插件：按六类查看启用状态、args/vars、校验结果；危险的脚本上传默认不开放。
  - 缓存/数据库：bucket/key 浏览、单项删除二次确认，不暴露敏感字段。
  - 实时日志：WebSocket 流、级别过滤、暂停/恢复、断线重连。
- 首版动画条目实现已确认的增删改查、作品/季度列表、TMDB EP 完成状态和排序；服务端用例和版本化 UI API 保持可扩展，不预先加入未确认的复杂媒体管理功能。
- 四类删除使用不同命令和权限检查。组合删除先冻结任务并生成删除计划，再处理媒体库文件、下载器任务/下载源，最后删除业务记录；任一步失败都保留可重试状态和审计，不把部分失败伪装成完成。所有文件路径必须解析并验证位于配置的 `download_path` 或 `save_path` 内，禁止跟随目录逃逸。
- 前端处理窄屏/桌面布局、中文界面、空状态、加载态和错误态。
- access-key 默认仅保存在会话内存或 `sessionStorage`，日志/API 响应不得回显密码、Cookie、TMDB key。
- 部署 YAML 是启动/路径/连接等部署基线，由运维维护；Web 不显示或改写其原文。可编辑应用字段以 `data_path/config/application.private.json` 保存 revision 覆盖，保存前由同一服务端候选逻辑校验并显示脱敏 diff，覆盖/恢复前把旧 revision 原子备份到 `data_path/backups`；环境变量覆盖字段显示最终有效值但不可写。SQLite 不复制这些配置作为第二真相源。

提交：

- `feat(web): port compatible minimal api endpoints`
- `feat(web): support animego helper legacy api contract`
- `feat(web): add websocket log stream and static assets`
- `feat(web-ui): add dashboard downloads and logs`
- `feat(web-ui): add configuration plugins and cache views`

门禁：OpenAPI 路径快照；每条路由成功/参数错误/认证错误；WebSocket pause/resume；Go/.NET 兼容接口契约差分；AnimeGoHelper 的单集、全集、上传规则、下载规则四个场景通过；前端 unit/component/Playwright E2E 和基本可访问性检查；AOT 发布目录直接刷新任意前端路由不 404。

### Phase 12：组合、发布与最终验收

- DI 显式注册所有实现，完成主 CLI 和 `AnimeGo-plugin` 对应工具。
- Docker NativeAOT 多阶段构建、卷、环境变量、端口 7991、healthcheck、非 root 和多架构 manifest。
- 提供 `compose.example.yml`，首版示例连接已有 qBittorrent 或 Compose 内 qBittorrent 两种部署方式。
- Tier-1 RID 压缩包、checksums、SBOM、许可证清单。
- 完整 JIT + AOT E2E、升级演练、性能基线和安全检查。

提交：

- `feat(host): compose complete AnimeGo service`
- `build(container): publish native-aot runtime image`
- `build(compose): add production docker compose examples`
- `build(release): add rid artifacts checksums and sbom`
- `test(e2e): verify complete parity workflow`
- `docs: publish migration and operations guide`

门禁：新用户安装、旧配置升级、可选旧 JSON 导入、qBittorrent多实例、五个 NativeAOT RID、Docker `linux/amd64`/`linux/arm64` 全部通过；Web UI 可完成日常操作；未批准 AOT/trim warning 数为 0。旧 Transmission 配置的永久不支持诊断必须通过。

## 6. 隔离下载测试环境

`testenv/compose.yml` 建立独立网络，包含：

- AnimeGoNet AOT 被测容器。
- qBittorrent `bt`、`pt` 测试实例及独立的 qBittorrent fixture seeder，共享受控测试卷但配置和任务隔离。
- fixture HTTP 服务，返回固定 RSS/Mikan/Bangumi/TMDB/torrent 响应。
- 本地 torrent seeder；只分发仓库生成的几 KB 合法测试文件。

安全规则：

- 所有宿主端口只绑定 `127.0.0.1`；CI 内部网络默认不暴露到公网。
- 所有下载、保存、数据目录位于 `.testdata/`，启动前解析绝对路径并检查仍在测试根下。
- 不使用受版权保护内容，不接入用户真实 RSS，不碰用户真实 qBittorrent。
- 测试销毁只删除已验证位于 `.testdata/runtime` 下的命名卷/绑定目录。
- 真实数据源 live smoke 与真实下载 E2E 分离；live smoke 只解析，不提交下载。

下载验证由 fixture qBittorrent seeder 分发仓库生成的小文件，`bt`、`pt` 两个 qBittorrent 作为被测客户端分别完成真实下载、文件选择、状态变化、移动/硬链接和删除；不建立其他下载器测试路线。

### 生产 Docker 适配要求

- 多阶段构建：Node 阶段构建 Web、.NET SDK + clang 阶段发布 AOT、最终 `runtime-deps`/distroless 类最小镜像运行。
- 镜像内不携带 .NET SDK、Node、npm cache 或源码；外部 C# 插件通过只读插件目录单独挂载，不打入默认镜像。
- Docker 已确认同时发布 `linux/amd64` 和 `linux/arm64`；平台产物在对应原生 runner 验证，Buildx 合并 OCI manifest。
- 官方 Docker 默认配置文件直接写入以下值，Compose 卷逐项匹配；入口程序不使用隐藏默认值把 YAML 替换成另一套路径：

  | 配置键 | Docker YAML 值 | Compose 挂载 |
  |---|---|---|
  | `setting.data_path` | `/data` | `./data:/data` |
  | `setting.download_path` | `/download/incomplete` | `./download:/download` |
  | `setting.save_path` | `/download/anime` | `./download:/download` |
  | `setting.client.download_path` | `/download/incomplete` | 下载器同样挂载 `./download:/download` |

- Docker 默认 YAML 片段固定为：

  ```yaml
  setting:
    data_path: /data
    download_path: /download/incomplete
    save_path: /download/anime
    client:
      download_path: /download/incomplete
  ```

- 宿主映射使用 `./data:/data` 和单一 `./download:/download`；AnimeGoNet 与官方 Compose 下载器都使用相同容器路径。不得把 incomplete 和 anime 配成两个独立 Docker volume，否则 `link/link_delete` 可能跨文件系统失败。
- `ANIMEGO_DATA_PATH`、`ANIMEGO_DOWNLOAD_PATH`、`ANIMEGO_SAVE_PATH`、`ANIMEGO_CLIENT_DOWNLOAD_PATH` 继续为兼容覆盖项，但官方 Compose 默认不设置它们。用户一旦设置，Web 必须显示“环境变量覆盖 YAML”，启动日志同时输出最终有效路径。
- 外部下载器若看到不同路径，只允许覆盖 `setting.client.download_path`；`setting.download_path` 始终是 AnimeGoNet 容器内可访问的同一物理目录。启动诊断必须检测路径不存在、不可写和已知跨卷情况。
- 默认非 root，支持 `PUID`/`PGID` 初始化或文档化的宿主目录权限方案；容器进程能正确接收 SIGTERM。
- `HEALTHCHECK` 调 `/ping`，启动顺序只依赖健康状态，不用固定 sleep。
- 提供只读根文件系统示例，仅 `/data`、单一 `/download` 媒体卷和临时目录可写。
- Compose secrets/file secrets 用于下载器密码、access-key、Cookie/API key；环境变量仍保留兼容模式。
- 发布镜像带 OCI labels、固定基础镜像 digest、SBOM、provenance 和漏洞扫描。

## 7. NativeAOT 持续门禁

每个功能提交必须满足：

1. `dotnet build -c Release` 无警告。
2. 所有被引用项目设置 `IsAotCompatible=true`；生产入口设置 `PublishAot=true`。
3. 启用 trim、single-file、AOT analyzer；新增警告直接失败。
4. `VerifyReferenceAotCompatibility=true` 产生的第三方元数据噪音必须逐项审查，禁止全局 suppress。
5. CI 在对应原生 runner 发布并测试全部五个 RID：Windows x64/ARM64、Linux x64/ARM64、macOS ARM64。
6. 测试发布目录中的原生二进制，不用 `dotnet run` 冒充 AOT 验证。
7. 通过 `/ping`、配置加载、SQLite、插件 builtin、WebSocket 和 graceful shutdown smoke。
8. 若新增反射，必须是有界且由 source generation/显式 metadata 覆盖；禁止 `Assembly.Load*`、`Reflection.Emit`、运行时代理。

## 8. Git 管理与提交规则

- 当前仓库从空目录执行 `git init -b main`，上游只读远端名为 `upstream`。
- 用户自己的 Git 托管地址确定后再添加 `origin`；不把 `upstream` 当作推送目标。
- 每个模块使用 `port/<phase>-<module>` 短分支，测试完成后以 fast-forward/rebase 保持线性历史。
- 测试和实现同属一个功能提交；不提交明知失败的中间状态到 `main`。
- Commit message 使用 Conventional Commits，并带明确 scope，例如 `feat(parser): ...`。
- 一个提交只解决一个可独立回滚的功能；格式化、依赖升级、生成文件和业务逻辑不得无关混杂。
- 提交前运行 `eng/verify.ps1 -Module <name>`；提交说明记录验证命令和上游文件映射。
- 修复移植回归用 `fix(<module>): ...` 独立提交，不改写已经共享的历史。
- 上游同步使用 `chore(upstream): sync develop to <sha>`，先更新 baseline/fixture，再逐项处理行为变化。
- 发布 tag 从 .NET 移植版自己的版本开始，保留 `Upstream-Version`/`Upstream-Commit` 构建元数据。

## 9. 风险与缓解

| 风险 | 等级 | 缓解 |
|---|---:|---|
| 动态 C# DLL 插件与 NativeAOT 冲突 | 高 | 官方插件编译期注册；动态插件使用独立可执行进程，主程序禁止 `Assembly.Load*`。 |
| YAML 注释/旧版本升级不完全一致 | 高 | AST 读写、12 份历史 fixture、字节/语义双层快照。 |
| 上游外部网站 HTML/API 已变化 | 高 | 固定 fixture 定义兼容行为；live smoke 只发现现实变化，不直接改 golden。 |
| 下载状态机存在竞态和破坏性文件操作 | 高 | 纯状态机测试、测试根路径守卫、容器真实 E2E、故障注入。 |
| 旧 Bolt 无直接 .NET 兼容读取 | 低 | 已决定不在主程序解析；缓存重建，必要时旧 Go 工具导出已知 bucket 为 JSON。 |
| 第三方库表面可编译但 AOT 运行失败 | 高 | Phase 0 原生 spike；每次依赖升级重跑 published-binary tests。 |
| 原 Go 测试在当前 Windows 无法执行 | 中 | Linux 容器固定 Go 基线，保存失败白名单及原因。 |
| Web UI 扩大后端 API/安全面 | 中 | 兼容 API 保持不变；UI API 版本化；本机配置值按便利要求直显，日志/运行轨迹继续脱敏；危险操作二次确认和契约测试。 |
| Docker 路径/权限导致真实部署失败 | 高 | 非 root、PUID/PGID、卷路径映射矩阵和 Linux x64/arm64 Compose E2E。 |
| “1:1”范围无限扩大 | 中 | 以本文件的可观察行为优先级和 `VERIFICATION_MATRIX` 为验收合同。 |

## 10. 粗略工作量与里程碑

这是中大型重写，不建议用单一总工期承诺。按单人全职、上游 fixture 可复用估算：

- M0（Phase 0-2）：1–2 周，AOT 骨架、契约、配置可用。
- M1（Phase 3-7）：3–5 周，存储、数据源、插件、解析流水线可用。
- M2（Phase 8-10）：2–4 周，两种下载器、文件状态机、调度可用。
- M3（Phase 11-12）：2–4 周，完整 Web UI、Web/API、Docker/发布、全链路验收。

总计约 8–15 周，Web UI 交互深度、外部 C# 插件 SDK/协议深度和五个平台的原生验证是最大变量。每个里程碑都能产生可运行、可验证、可回滚的版本。

## 11. AnimeGoNetData 自动数据仓库

参考 [wetor/AnimeGoData](https://github.com/wetor/AnimeGoData) 的独立更新方式，使用已经确认命名的独立 `AnimeGoNetData` 仓库，但不继续生成 Bolt。

### 数据产物

- `manifest.json`：schema 版本、生成时间、上游仓库/Release/asset、记录数、文件大小、SHA-256、最低客户端版本。
- `bangumi-subjects-v1-*.jsonl.gz`：动画 subject 分片。
- `bangumi-episodes-v1-*.jsonl.gz`：普通 episode 分片；可按 subject ID 范围切分。
- 可选轻量索引：subject ID → 分片，方便按需下载；首版可以先全量导入。
- 所有 JSON 字段使用稳定 schema；新增字段向后兼容，破坏性变化提升 schema major。

### 数据 GitHub Actions

- 每日定时检查 Bangumi Archive 最新 Release/asset，同时支持 `workflow_dispatch`。
- 使用 ETag/asset ID/SHA-256 判断上游是否变化；未变化则成功退出，不制造空 Release。
- 下载后验证 zip 路径安全、JSONL 可解析、ID 唯一、subject/episode 引用、记录数下限和日期范围。
- 生成过程必须确定性；相同输入重复运行得到相同数据文件哈希。
- 先上传带版本的不可变 Release assets，最后更新 latest manifest；失败不覆盖上一版。
- Action 使用最小权限、concurrency 防重入、依赖固定 major 并由 Dependabot/Renovate 跟踪。

### AnimeGoNet 客户端更新

- 后台任务和 Web UI 都可执行“检查/下载/导入/回滚”。
- 更新行为只读取 YAML/环境变量的有效配置，不在 SQLite 中维护第二份策略。建议 schema：

  ```yaml
  setting:
    data_update:
      enabled: true
      cron: "0 0 4 * * ?"
      manifest_url: "https://github.com/<owner>/AnimeGoNetData/releases/latest/download/manifest.json"
      auto_download: true
      auto_import: true
      keep_versions: 2
      timeout_second: 300
  ```

- `enabled=false` 只关闭后台调度，手动检查/导入仍可使用；`auto_download=false` 只提示新版本；`auto_import=false` 下载并校验后等待用户确认。
- Web 修改这些字段时写入 `data_path/config/application.private.json` 的原子 revision 覆盖并热重排 Cron；不改写运维维护的部署 YAML。环境变量覆盖字段只能查看，不能伪装成已写入。
- 先下载到临时目录，校验 size、SHA-256、schema 和最低客户端版本。
- 使用流式 gzip + `JsonDocument`/源生成 DTO 读取，不把完整数据集加载到内存。
- 在单独 SQLite staging 表批量导入，完成完整性检查后事务切换 active version。
- 保留上一个可用数据版本；下载中断、磁盘不足、JSON 损坏或 schema 不支持时继续使用旧版。
- 配置自动更新 Cron、镜像地址和是否允许联网；离线部署可手工上传同一数据包。
