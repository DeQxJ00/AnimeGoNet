# AnimeGoNet TODO

状态约定：`[ ]` 未开始，`[>]` 进行中，`[x]` 已完成，`[!]` 阻塞。只有验证矩阵对应项通过后才能勾选完成并提交。

## P0 — 决策与基线

- [x] 固定上游 `develop@c7475dfc55a374cd0dd08821bf17125dab1e3145`。
- [x] 从 `upstream/develop` 创建 `codex/animegonet-main` 功能分支；保留上游 Go 源码为只读行为参照，不覆盖用户已有文件。
- [x] 盘点上游模块、API、配置、插件、fixture 和发布方式。
- [x] 记录 Windows 上游测试基线：局部覆盖被污染的 Android/ARM64 Go 目标后，仅 `internal/pkg/request` 因本机禁止绑定固定 `127.0.0.1:8080` 失败；其余包通过或按上游门禁跳过。
- [x] 确认 builtin 插件和 MikanTool 过滤器全部 C# 内置化；旧 Python 名称仅作为兼容别名。
- [x] 确认首发 RID：`win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`、`osx-arm64`。
- [x] 确认完全移除 Python 插件；官方 C# 插件编译进主程序，动态第三方 C# 插件使用独立可执行进程协议。
- [x] 确认不在 .NET 主程序解析旧 `.bolt`，直接使用 SQLite；只保留可选 JSON 导出导入路径。
- [x] 确认首版包含 Windows/Linux ARM64 与 macOS ARM64。
- [x] 确认 Web 前端采用静态 TypeScript/HTML/CSS，不引入 Vue/React 等重型运行时框架；构建产物嵌入主程序静态资源。
- [x] 确认 YAML 管部署/启动/更新策略，SQLite 管业务和运行数据；环境变量覆盖项在 Web 中只读显示。
- [x] 确认原生运行默认仅监听回环，Docker 监听所有接口但强制 Access Key。
- [x] 确认 AnimeGoHelper 以原脚本不修改为验收标准，保持旧 API 和响应格式。
- [x] 确认数据更新的开关、Cron、manifest、自动下载/导入和保留版本数均由 YAML 配置。
- [x] 确认确定性季度失败策略：Skip=4、Backtrace=3、TitleSeason=2、FirstSeason=1；AI 匹配是独立开关且默认 `false`，不占确定性优先级编号。
- [x] 确认增加后置 EP-AI：非 AI 季度成功但 EP 无法对应时，按下载任务使用同一 Prompt 整体匹配一次；配置名为 `tmdb_failep_use_ai_match_season`，默认 `false`。
- [x] 确认 AI 不依赖具体输入站点；请求使用下载任务总标题、候选视频的相对文件名/字节容量，以及可空 `bgmid`/`anidbid`/`imdbid`，单文件和多文件使用同一基础契约。
- [x] 确认 Mikan `pubDate` 优先查找仅在Torrent实际文件条目数恰好为1且有有效bgmid/pubDate时由固定Prompt启用；单文件模式和根目录下仅一个文件均满足，Bangumi最近EP不能直接决定TMDB集号。
- [x] 确认非空元数据 ID 与当前任务标题和 Torrent 文件组存在作品级绑定，但跨站标题、季度拆分和 Episode 编号不要求一致，仅作辅助证据；具体 EP 仍须独立匹配并经 TMDB 验证。
- [x] 确认 AI 工具顺序：本地 TMDB MCP始终启用，Bangumi MCP仅有 `bgmid` 时启用，AniDB映射仅有 `anidbid` 时启用，`imdbid` 通过 TMDB external ID 查询；MCP不足后才允许 Web Search。
- [x] 确认 TMDB 匹配成功后，动画目录名、季度号和集号全部采用经 TMDB API 验证的值；来源名称/集号只保留用于审计。
- [x] 确认 TMDB 完全失败沿既定下载/兜底流程处理，并持久化最终失败原因及每个匹配策略的尝试结果；Web UI 必须可查看、筛选和重试。
- [x] 确认 `tmdbid=0` 只允许成功访问 TMDB 后的确定性语义无匹配；网络/超时/429/5xx、认证、配置、协议、输入或歧义失败均禁止 Bangumi 完全兜底。
- [x] 确认人工规则为最高优先级；Mikan URL 中的作品 ID 统一称 `mikanid`，相同 `mikanid` 共享 `bgmid`、TMDB Series/Season 和 EP 偏移，自动策略不得覆盖。
- [x] 确认 `Auto_Bangumi/raw_parser.py` C# 移植得到的逐文件 `file_episode_candidate` 完全属于 Mikan RSS 本地状态，不进入 AI Prompt/请求/响应。主程序在逐文件 TMDB Episode 验证完成后按 `TMDB EP - 文件名 EP` 本地计算统一偏移。
- [x] 确认增加默认关闭的 Mikan 字幕组可信偏移缓存：严格按 `mikanid+groupid` 隔离，3 个不同来源 EP 得到相同 `tmdb_id+season+offset` 才可信，重复 EP 不计数，冲突立即重置/撤销；主程序在 AI 调用前命中可信记录时直接本地计算目标 EP 并跳过 AI。
- [x] 确认作品详情必须展示 TMDB Series/Season/Episode 分别在哪个阶段取得。
- [x] 确认 Web UI 同时支持四类独立删除：业务记录、下载器任务、下载源文件、媒体库文件，并可预览后组合执行。
- [x] 确认重复集默认只认首个成功完成记录；后续同集在 RSS 解析阶段命中完成记录即停止，删除该业务记录后允许重新进入流程。
- [x] 确认附属文件首版只整理字幕；唯一匹配 EP 时随视频重命名并保留多语言/轨道后缀，无法对应时季度已知则进入 `Other`。
- [x] 确认支持多个命名下载器实例，并按输入源配置下载器、元数据字段、过滤/匹配规则、文件/做种策略；Mikan/U2/TTG 使用同一通用导入流水线和 API 契约。
- [x] 确认示例路由：Mikan（bgmid必填）→ `bt` qBittorrent；U2（anidbid可空）和 TTG（imdbid可空）→ `pt` qBittorrent，名称和绑定均可配置。
- [x] 确认项目只支持 qBittorrent 下载器及其多命名实例；取消 Transmission 适配计划，旧类型仅作不支持诊断。
- [x] 确认 U2/TTG 首版采用外部油猴/扩展/API提交模式；调用方传回带个人 passkey 的 Torrent URL，主程序不保存站点账号/Cookie、不登录或抓取网页。
- [x] 确认沿用并强类型化 Mikan `source + data[].torrent + data[].info` 批量格式，所有来源统一调用 `/api/v1/ingest`；旧 API 转同一 command。
- [x] 确认跨输入源按 `(TMDB Series, Season, Episode)` 全局去重；只跳过已完成 EP，同剧集和多文件 Torrent 中的其他 EP 不受影响。
- [x] 确认 Mikan RSS 同集优选的黑白名单是前置资格过滤，单候选也执行；只有资格过滤后同一 `mikanid+来源EP` 仍有多个候选时才运行可配置优先级组。
- [x] 确认默认 Mikan SourceProfile 使用 `move`：下载完成后移动到媒体库、不继续做种；Web可改其他策略且只影响新任务。
- [ ] 确认 U2/TTG 默认文件策略是否使用候选 `link` 以长期做种；四种模式和上游完成回调的隐式删除偏差已记录。
- [ ] 在 Linux Go 容器跑上游测试并保存基线报告。
- [x] 生成上游 fixture SHA-256 清单和 OpenAPI 快照。

## P1 — .NET 10 / NativeAOT 工程骨架

- [x] 安装并固定 .NET 10 SDK（当前主机为 SDK `10.0.302`，通过 `global.json` 固定 feature band）。
- [x] 固化解决方案分层、核心数据聚合、SQLite 明确 SQL 规则与 NativeAOT 允许/禁止边界（见 `docs/ARCHITECTURE.md`）。
- [x] 创建 solution、Core/Data/App 分层项目及对应测试项目。
- [x] 添加 `global.json`、`Directory.Build.props`、`Directory.Packages.props`。
- [x] 启用 nullable、warnings-as-errors、deterministic、AOT/trim analyzer。
- [x] 建立 Windows/Linux/macOS build/test CI，并对工作流 YAML 做本地语法校验。
- [>] 验证 Minimal API、WebSocket、静态文件 NativeAOT（Minimal API/静态文件已通过 win-x64 原生 smoke；WebSocket 待实现）。
- [x] 验证 Microsoft.Data.Sqlite NativeAOT（win-x64 原生进程完成 migration、integrity 与状态读取）。
- [ ] 验证 YAML AST、Cron、HTML 解析候选依赖 NativeAOT。
- [x] 建立 published-binary smoke 脚本（`eng/smoke-native.ps1`）。

## P2 — 领域与配置

- [ ] 移植所有领域模型、枚举和错误类型。
- [x] 建立首阶段强类型配置/目录模型与校验：Docker 三路径、命名 qBittorrent、Mikan `move` 默认、AI 600 秒和高风险 fallback 默认关闭。
- [>] 固化 JSON source-generation context（状态、统一导入和 legacy manager DTO 已覆盖；后续 API DTO 持续加入）。
- [ ] 移植 hash、name、path、时间等纯函数。
- [ ] 移植默认 YAML 与注释。
- [ ] 移植环境变量覆盖。
- [ ] 移植配置检查、路径初始化和资源释放。
- [ ] 移植配置 `1.1.0` → `1.7.1` 升级链与备份。
- [ ] 新配置加入 Skip/Backtrace/TitleSeason/FirstSeason 四档确定性季度策略和独立季度 AI/后置 EP-AI 开关，全部默认 `false`；旧配置升级显式写入新默认值并生成阶段注释。
- [ ] 增加 OpenAI-compatible AI 配置 DTO、环境变量、敏感值脱敏和 source-generated JSON 上下文。
- [>] 领域模型拆分来源字段与 TMDB 规范字段：权威 `TmdbSeries`/`TmdbSeason`/`TmdbEpisode` 与三级验证结果已建立；来源字段和持久化编排仍待串联。
- [x] 增加 `MikanWorkMetadataRule`：`mikanid` 唯一键、`BangumiSubjectId`、`TmdbSeriesId`、`TmdbSeasonNumber`、有符号 `EpisodeOffset`、启用/版本/审计字段；数据层已实现 revision 冲突保护、禁用和清除，API/编排接入在对应阶段继续。
- [ ] 将上游 `assets/plugin/filter/Auto_Bangumi/raw_parser.py` 1:1 移植为 NativeAOT 友好的 C# 内置解析器，不在兼容层擅加年份保护、歧义拒绝或E04/EP04扩展；另建 `FileEpisodeCandidateResolver` 安全层，只在 Mikan SourceProfile 决定是否形成逐文件 `file_episode_candidate`；增加 AI/TMDB 验证后的本地统一偏移计算器，结果不一致时只禁止缓存学习，不否定已验证的逐文件映射。
- [>] 已建立 NativeAOT-safe Torrent 文件和 Mikan RSS title EP 安全分类层：兼容上游 Go `ParseEp` 的 `[04]`/`[04v2]`/` - 11`/`EP12`/`第12话`，RSS title 另支持不受扩展名截断的最后可靠标记；小数集与 SP/OVA/OAD/PV/NCOP/NCED/Menu/S00E 均不形成普通整数。入库仅为普通正整数写 `file_episode_candidate`，已在确认 Season 内逐文件经 TMDB Episode API 验证；RSS 批次串联与 raw_parser.py 严格 differential 仍待实现。
- [>] 增加 `MikanOffsetEvidence`/`MikanTrustedOffsetCache` SQLite 模型、事务状态机和默认关闭配置；数据层已按 `(mikanid,groupid,来源EP)` 唯一约束累计三个不同正整数 EP，并在冲突/歧义时撤销可信状态；命中可安全计算目标 EP，主程序 AI 前置接入仍待串联。
- [ ] 增加 Series/Season/Episode 三层 `TmdbResolutionSource` 和解析运行/策略尝试引用。
- [ ] 通过全部配置/模型 parity tests。

## P3 — 存储

- [x] 建立 SQLite schema v1、幂等事务迁移器与显式 SQL 完成记录 store；启用 foreign key/WAL/busy timeout。
- [ ] 实现 SQLite KV/TTL store。
- [ ] 实现 bucket/list/get/delete 兼容接口。
- [ ] 移植目录 JSON 数据库扫描/索引/写入。
- [>] 移植全局 TMDB Episode 去重索引、来源 alias 和完成记录删除（全局完成唯一键、并发 TryAdd、逐文件 EpisodeClaim、已完成/进行中精确跳过及失败释放已完成；删除执行器已按精确记录 ID 事务删除 completion/alias 并释放对应 completed claim，通用 alias repository 待实现）。
- [>] 实现 `tmdbid=0` 的 `FallbackEpisodeClaim`、`FallbackCompletionRecord` 和分层唯一键（schema/约束已完成；事务 store 与早停编排待实现）。
- [ ] TMDB 恢复后事务合并 fallback 完成记录和 alias；多个记录收敛到同一 TMDB Episode 时标记 `DuplicateAfterResolution`，不重复下载、不自动删除文件。
- [ ] 移植 `tvshow.nfo` 生成和更新。
- [ ] 按需实现旧 Go 已知 bucket → JSON 导出及 .NET 幂等导入，不阻塞首版。
- [ ] 通过存储故障恢复、并发和迁移测试。

## P4 — HTTP、Feed、Torrent

- [ ] 移植代理、超时、重试、Host redirect、Cookie/API key。
- [x] 移植 RSS 文件/URL/raw parse：已实现 5 MiB 上限、禁用 DTD/外部实体、首个 enclosure、无 enclosure 跳过、非法 length 归零、Mikan `pubDate` 日期兼容和稳定错误码；URL/文件读取边界可注入测试，尚未暴露为公网抓取 API。
- [>] 实现 Bencode/torrent/magnet/info-hash（严格 v1 Bencode、原始 info 字节 SHA-1、单/多文件清单、padding/路径/数量/总量校验已完成；magnet 与上游 fixture parity 待实现）。
- [>] 通过本地 fixture HTTP、RSS、torrent parity tests：RSS raw/file/注入式 HTTP、缺字段、损坏 XML、DTD、错误脱敏和 mikanid fixture 已通过；上游全部 fixture、真实本地 fixture server 与 torrent magnet parity 待完成。

## P5 — 数据源

- [ ] 移植 Mikan。
- [>] 从 Mikan RSS/页面 `/Home/Bangumi/{mikanid}` 提取并持久化正整数 `mikanid`：RSS source URL 优先、channel link 回退及 path/query 解析已验证；批次任务持久化与页面解析仍待串联。
- [>] 移植 Bangumi API：已按上游 `/v0/subjects/{bgmid}` 与官方 `/v0/subjects/{bgmid}/subjects` 实现 AOT-safe Subject/关系客户端、固定 User-Agent、日期/身份校验和稳定网络/协议失败分类；Episode 与缓存仍待实现。
- [ ] 移植 Bangumi Archive 下载/缓存刷新。
- [>] 移植 TMDB 搜索、相似度和季度匹配（上游 discover/tv 查询参数、四步去后缀、UTF-8 byte 相似度、0.75 阈值、普通季度过滤、90 天日期阈值、zh-CN DTO 与 Series/Season/Episode 三级身份验证已实现；Bangumi Subject → TMDB Series/Season 与逐文件 Episode worker/运行审计已接入，缓存和 Bangumi Episode 确定性匹配待实现）。
- [>] 按 issue #15 实现 `TMDBFailBacktrace` / `tmdb_fail_backtrace`（默认 `false`）：已接入 Bangumi“前传”多层回溯、同层稳定排序、缺日期继续、visited 防环、成功早停和错误后继续低优先级策略；网络重试策略与受控 live fixture 仍待实现。
- [ ] 实现 `TMDBFailUseAIMatchSeason` / `tmdb_fail_use_ai_match_season`（默认 `false`），每个下载任务只向大模型发送总标题、候选视频的相对文件名/字节容量及可空作品级 `bgmid`/`anidbid`/`imdbid`，一次返回整个文件列表的 TMDB 映射；不得以跨站标题不一致否定任务绑定，也不得直接复制来源 EP。
- [ ] 实现 `TMDBFailEpUseAIMatchSeason` / `tmdb_failep_use_ai_match_season`（按指定拼写，默认 `false`）：非 AI 季度匹配成功后先验证来源 EP；存在不对应时按下载任务执行一次 AI EP 匹配。
- [ ] 实现 Mikan 单文件发布日期Prompt门禁和显式开关：保留完整 `pubDate`，无偏移时按SourceProfile时区解析；即使开关开启也仅在Torrent实际文件条目数1、bgmid/日期有效且主程序成功计算 `bgm_episode_candidate` 时为真，Prompt直接结合文件名EP定向查TMDB，失败回通用流程。
- [x] 同步 AI 测试程序：增加可编辑开关和手工 `bgm_episode_candidate`、只读有效门禁；覆盖两种单文件Torrent、实际多文件禁用、无bgmid/日期/候选禁用和优先分支失败回退。
- [ ] 使用固定 JSON 请求/响应 DTO 调用 OpenAI-compatible API；模型返回必须由 TMDB Series/Season/Episode API 二次验证。
- [ ] 实现 AOT-safe 本地 Streamable HTTP MCP客户端和 function-calling工具循环；为 BGM/TMDB同名工具添加命名空间，并覆盖 JSON/SSE、会话、超时、取消和失败隔离。
- [ ] 实现可空 `anidbid` → `tmdbtv` 候选查询；固定URL、限制响应并阻止SSRF，候选未经 TMDB MCP验证不得采用。
- [ ] 实现可空 `imdbid` 规范化和 TMDB MCP external ID/find 候选查询；拒绝 Movie，最终 TV Series/Season/Episode 逐级验证。
- [>] AI 和确定性匹配均拒绝 Season 0；确定性流程已拒绝 Season 0，Series/Season 已确认但 Episode 未匹配的文件已持久化 `Other` 及稳定原因，实际整理到 `<TmdbName>/Sxx/Other/` 与 AI 门禁仍待实现。
- [>] 将确定性季度失败策略固定为 Skip=4、Backtrace=3、TitleSeason=2、FirstSeason=1；四级确定性策略已按优先级接入并验证早停/错误降级，独立 AI 阶段仍待实现且结果必须经 TMDB 验证。
- [ ] 为前传缺失、日期缺失、多前传、关系循环、回溯到首部仍不匹配、请求失败和取消建立 fixture。
- [ ] 为 AI 禁用/未配置/超时/限流/畸形 JSON/伪造 ID/多候选/文件列表冲突/缓存建立 fake-server 测试。
- [>] 移植 Mikan → Bangumi → TMDB 编排与 fallback：携带 `bgmid` 的已下载任务已由内置 worker 执行 Bangumi Subject → TMDB Series → 日期季度，并持久化每次策略；Mikan 页面自动发现 bgmid、Backtrace、AI、Bangumi 完全兜底仍待实现。
- [>] 自动编排之前应用 Mikan 作品级人工规则；完整 TMDB Series/Season 覆盖由专用 worker 优先领取并权威验证，EP Offset 已在逐文件 TMDB Episode 验证前应用且无效时阻断静默回退；可信自动 offset 与字幕绑定仍待串联。
- [x] 人工规则无效时记录人工覆盖策略失败并阻止静默自动覆盖；清除/禁用后可通过 `POST /api/v1/metadata/tasks/{taskId}/retry` 显式重新匹配，事务性恢复自动策略队列且保留历史运行记录，并拒绝活动租约/非失败状态。
- [>] 区分 TMDB 无结果、季度无匹配、瞬时网络错误和认证/配置错误（客户端已稳定分类 SemanticNoMatch/Network/RemoteService/Authentication/Configuration/Protocol/InvalidInput 且异常脱敏；重试编排待实现）。
- [>] 为完整失败保存 `failure_kind`、`tmdb_access_confirmed`、`bangumi_fallback_eligible/denial_reason`（权威 404 仅产生 `SemanticNoMatch + access_confirmed`，其他客户端失败均禁止资格；SQLite 持久化与最终门禁待串联）。
- [ ] 持久化元数据解析运行与策略尝试记录：阶段、策略、优先级、结果、错误码、脱敏原因、可重试性、次数、耗时和时间戳；重启后可查询。
- [ ] 通过 fixture parity 和受控 live smoke。

## P6 — 插件与业务流水线

- [ ] 实现 `AnimeGo.Plugin.Abstractions` 和 source/feed/parser/filter/rename/schedule 六类强类型 C# 插件契约。
- [ ] C# 移植 builtin feed/parser/filter/rename/schedule；默认运行不加载 Python。
- [ ] 实现内置 C# MikanTool 五级黑白名单规则，默认启用；精确复现 `Filiter0`～`Filiter4` 作用域、`1>2>3`、最终 AND、大小写敏感子串和多个 `Filiter0` 的 legacy 顺序行为。
- [ ] 为默认 Mikan SourceProfile 增加 `mikan_rss_filter_enabled` 总开关（默认 `true`）；关闭时 AnimeGoHelper `/api/rss` 记录 `SkippedByConfiguration` 后继续流水线，规则保留，进行中任务使用原快照。
- [>] 增加独立 `mikan_rss_priority_enabled` 批次优选开关（默认 profile 已启用，schema v13 规则版本、默认初始化与预览 API 已接入；禁用时预览逐项记录 `SkippedByConfiguration` 且不清空规则，真实批次编排待接入）。
- [>] 实现完全可配置的 `priority_groups[]`：纯 C# 引擎支持任意有序组/具名数组、统一 lowercase 和逐级淘汰；schema v13 store 与 GET/PUT expected-revision 全快照 API 已支持增删/排序，细粒度 CRUD/WebUI 待实现。
- [x] 优选组资格过滤后只有一个候选记录 `SingleCandidateBypass` 且不执行优先级组；多候选每轮剩一个立即短路，最终并列按原 RSS 顺序稳定选择。
- [x] 预置字幕语言、字幕封装、编码、分辨率四组，但引擎不写死组数或内容；name 仅展示，values 才参与匹配。
- [>] 实现优选阶段具名白名单/黑名单数组、黑名单优先和默认 720p 黑名单；SQLite CRUD/审计待实现。
- [>] RSS loser 产生 `SuppressedByHigherPriority` 决策且 winner 不隐式晋级；Mikan RSS title 的可靠普通/小数/特别篇/未知来源 EP 分类已实现，与规则引擎、Torrent 获取/AI/任务创建的编排门禁待接入。
- [ ] 实现显式 `PluginCatalog` 注册，禁止反射扫描和动态 DLL 加载。
- [ ] 实现外部 C# 插件进程的 manifest、JSON Lines 协议、超时、取消、健康检查和退出隔离。
- [ ] 提供 `AnimeGo.Plugin.Sdk`、NativeAOT 插件模板和五 RID GitHub Actions 模板。
- [ ] 实现 `AnimeGo.PluginTool`。
- [ ] 移植 parser manager。
- [ ] 移植 ordered filter manager。
- [ ] 移植 feed → filter → parse → download pipeline。
- [ ] 通过上游所有插件/parser/filter fixture，以及外部 C# 插件协议故障注入测试。

## P7 — 首版 qBittorrent 下载客户端

- [>] 定义稳定 `IDownloadClient` 契约，并将单下载器配置升级为命名实例字典；`bt`/`pt` 客户端、Cookie 会话、实例隔离、按实例串行操作和失败隔离同步已实现；指数退避/熔断待实现。
- [>] 实现 `SourceProfile` 和不可变路由快照：Mikan 默认 seed、可配置 U2→`pt` 路由、revision/文件策略/规则开关快照已落库；U2/TTG 默认文件策略仍待确认，CRUD、category/tag/做种策略待实现。
- [ ] 初始化默认 Mikan SourceProfile 的 `file_strategy=move`；Web改动只进入新任务快照，保存时明确提示该模式不做种。
- [>] 新增强类型输入适配层：Mikan/U2/TTG 统一校验、别名、mikanid/IMDb 规范化和冲突拒绝已实现；统一/旧入口已在请求期执行安全 Torrent staging 并原子保存文件清单，qB worker 待接入。
- [x] 实现 qBittorrent 5 WebUI API adapter 和 fake-handler contract tests：登录、torrent/file list、multipart add、file priority、stop/start/delete、状态映射、严格 hash/index/priority 校验与失败响应。
- [x] 实现 staged Torrent 后台 dispatch：SQLite并发租约、崩溃租约恢复、不可变实例路由、paused add、同hash幂等检查、已有/新增任务显式再暂停、qB确认、download job事务与确认后staging清理。
- [x] 接入本机 `TestSpace` portable qBittorrent 隔离沙箱：ignore、独立测试项目、端口所有者/profile/版本、用户名密码 Cookie 登录、list 和三路径 smoke 已通过；默认 CI 不启动该实例，也未创建 Torrent。
- [ ] 建立隔离 Docker Compose 下载环境。
- [ ] qBittorrent 通过 add/list/state/file-priority/pause/resume/delete/reconnect 真实容器测试。
- [ ] 同时启动 `bt`/`pt` 两个 qBittorrent 实例，验证 Mikan→bt、U2/TTG→pt，修改绑定不影响进行中任务。
- [ ] 旧YAML/环境变量出现Transmission时可读取并生成`UnsupportedDownloaderType`迁移诊断；不得启动任务、不得静默改成qB，Web保持可进入修复。

## P8 — 下载、重命名、刮削

- [>] 移植下载管理状态机和 notifier（staged→dispatching→download_preparing→metadata_resolved→download_queued/skip→downloading/downloaded、持久化准备租约及安全重试已实现；做种/整理 notifier 待实现）。
- [>] 移植重启恢复、去重、失败重试和删除 callback（dispatch lease恢复、qB同hash幂等、按实例+hash运行快照恢复、离线 stale 保留与退避重试，以及每个 job 的不可变 download/save root 快照已实现；删除回调待实现）。
- [>] 完成记录仅在下载、文件策略、重命名和必要 NFO/目录库写入全部成功后原子写入：move worker 已在所有文件及原子 `tvshow.nfo` 成功后才执行完成记录/episode claim 事务，qB cleanup 独立在后；完整目录库和 RSS 早期事务复查待实现。
- [ ] 移植 link/link_delete/move/wait_move。
- [>] `move` 安全编排：下载完成后暂停、持久化逐文件执行、TMDB规范路径、同卷原子移动/跨卷copy+SHA-256、冲突保全、崩溃恢复、原子 `tvshow.nfo`、完成记录事务及独立 `deleteFiles=false` qB cleanup 已串联并通过 fake-qB+真实临时文件测试；真实 qB/Docker共享路径 E2E 与完整目录库待验收。
- [>] 将媒体整理、做种目标完成、删除下载器任务、删除下载源拆成独立持久化状态：move 文件操作与 qB cleanup 已分阶段；四类删除已按逐项目标独立持久化、租约执行和失败重试，qB 删除固定 `deleteFiles=false`，源/媒体文件分别受捕获根目录约束；做种目标待实现。
- [ ] 处理多文件、跨盘、目标冲突和部分失败。
- [>] 多文件 Torrent 逐文件去重：qBittorrent 暂停添加、metadata/claim 完成后逐项核对 index/path/size、重复与 ignored 文件 priority=0、wanted 文件 priority=1 后才恢复；全重复任务保持停止并以 `deleteFiles=false` 移除。fake/SQLite并发、恢复和失败测试已通过；绑定字幕与真实 qB/container E2E 待实现。
- [x] 实现字幕识别与唯一绑定：同目录同 stem 优先、语言/default/forced/SDH 后缀原样保留、不同 stem 按来源 EP 唯一匹配、`.idx/.sub` 分别绑定并保留扩展；匹配后只复用视频的已验证 TMDB EP/claim/priority，未匹配或歧义进入已确认季度 `Other`，整理不产生重复完成记录。
- [ ] 串联媒体目录 DB 与 NFO。
- [x] 任一季度匹配策略成功后，固定使用 TMDB `zh-CN` 名称（缺失时用 TMDB 原名）、Season Number 和 Episode Number 生成 `<TmdbName>/Sxx/Eyyy.ext`；字幕生成 `Eyyy.<保留后缀>.<字幕扩展>`，Other 保留安全清洗后的原文件名，均已串联持久化 move worker。
- [ ] 非 AI 季度结果依次执行同号 EP 快速校验、Bgm/TMDB 标题日期校验；失败且 `tmdb_failep_use_ai_match_season=true` 时进行一次 AI EP 映射，返回的 TMDB ID/Season 必须与已确认值相同。
- [ ] 保留来源名称和来源集号用于审计、去重诊断及 UI 展示；未经 TMDB API 验证的 AI 值不得参与路径、数据库键或 NFO。
- [>] 多文件任务逐集验证 TMDB Episode：已实现独立租约 worker、官方 Episode 身份验证、规范 Episode 持久化、人工 offset、网络失败保持 pending、季度已知时 `Other` 原因，以及跨任务完成/活动 claim 的逐 EP 重复门禁；已串联 paused qB 的逐文件 priority 与恢复门禁，实际下载/落盘及字幕绑定待实现。
- [ ] 增加 `advanced.default.tmdb_fail_use_bangumi` 业务兜底开关，默认 `false`；关闭时 TMDB 完全失败即沿用原失败流程，不继续下载/刮削且不生成 NFO。
- [ ] 开关开启后，仅在权威TMDB访问成功且最终为确定性无匹配、已有有效 Bangumi Subject ID 且季度 fallback 成功时继续；动画根目录 `tvshow.nfo` 固定写 `<tmdbid>0</tmdbid>` 和对应 `<bangumiid>`。
- [ ] 验证已取得 TMDB ID、仅季度匹配失败时仍走原季度 fallback，不误入 Bangumi 完全失败兜底。
- [ ] 通过状态机、文件策略和合法小文件 E2E。

## P9 — 调度、Web API 与 Web 页面

- [ ] 实现六字段 Cron 调度、StartRun 和 NextTime。
- [ ] 实现 Bangumi/数据库/feed/plugin tasks。
- [ ] 实现优雅退出和取消传播。
- [ ] 移植 10 个 HTTP API。
- [>] 新增 `/api/v1/ingest` 通用批量 Torrent/URL 导入 API，沿用 `source + data[].torrent + data[].info`；旧 `/api/download/manager` 已转换到同一 command，二者均完成安全 staging 与后台 qB dispatch；`/api/rss` 待接入。
- [>] 将 passkey Torrent URL 和 `.torrent` announce 视为 secret：profile host白名单及不可变路由快照、逐跳redirect/DNS校验、校验IP固定连接、限时限量、严格Bencode/info-hash、请求期受限 staging、崩溃过期清理、qB确认接收后删除均已实现；AI负向门禁待串联。
- [ ] 新增下载器实例和 SourceProfile 的版本化 CRUD、连接测试、路由预览及引用保护 API。
- [>] 移植 access-key、响应 envelope、参数错误（直接/旧 hash access-key、ping/sha256、legacy manager envelope 和逐项导入错误已验证；其余旧 API 待移植）。
- [ ] 移植 WebSocket 日志 pause/resume。
- [ ] 兼容 `DeQxJ00/AnimeGoHelper`：`/ping`、`/api/rss`、`/api/download/manager`、`/api/plugin/config` 和 `Access-Key`。
- [ ] 将旧插件名 `filter/mikan_tool.py` 映射到 SQLite 过滤规则，不要求实际 Python 文件存在。
- [ ] 实现 Mikan 过滤 Web UI：RSS 过滤总开关、五档规则 CRUD/启停、关键词编辑、服务端样例预览、旧 JSON 导入导出、revision 冲突、快照回滚和过滤决策详情。
- [>] 实现 Mikan RSS 优选 Web UI：原生 TypeScript 页面已支持白/黑名单及有序组/数组的增删、启停、上下移动、values 编辑、expected-revision 保存和真实服务端批次 preview（名单结果、winner、实际执行组）；SourceProfile 独立开关写入、拖拽与历史回滚待实现。
- [ ] 移植静态页并生成 OpenAPI。
- [ ] 通过 API/WS 契约差分测试。
- [ ] 创建 Web 前端工程、类型化 API client 和前端测试基线。
- [>] 实现仪表盘和下载器/任务状态（下载状态卡片、进度、实例离线提示，以及 `download_preparing`/重复跳过、元数据 Series/Season/Episode 阶段、失败原因、文件归类计数和显式重新匹配入口已实现；准备失败详情、汇总指标与完整管理视图待实现）。
- [>] 实现两层下载进度投影：qB规范状态/百分比/容量/速度/ETA/Seeds/Peers与AnimeGoNet业务状态已分离，qB 100%映射为 `downloaded` 而非最终业务完成；解析/移动/重命名/字幕/NFO阶段待串联。
- [>] 实现按实例隔离的qB同步器和`DownloaderTaskSnapshot`：活动约2秒、空闲约10秒、单实例单在途、实例失败隔离、离线保留stale快照、重启按实例+hash恢复已完成；指数退避/熔断待实现。
- [>] 实现下载列表/详情/文件级priority与wanted进度、筛选搜索分页和状态时间线（只读列表 API/WebUI 与规范快照已实现；详情、文件级、筛选分页和时间线待实现）。
- [>] 实现暂停、恢复和AnimeGoNet业务重试；下载任务卡片已只跳转四类删除中心并执行预览/确认，暂停、恢复和业务重试待实现。首版不复刻Tracker/Peer明细、piece图、限速、强制校验/汇报和qB全局设置。
- [ ] 实现多下载器页面：实例 CRUD、连接状态、路径/硬链接探测、来源引用和任务数量。
- [ ] 实现输入源页面：下载器绑定、ID字段规则、过滤/匹配 profile、category/tag、文件/做种策略、重复命中通知和路由预览。
- [ ] 实现手动 RSS/下载提交与操作结果。
- [ ] 实现配置表单、YAML 预览、校验、diff 和保存备份。
- [ ] 配置页显式展示五个季度失败开关及独立 EP-AI 开关，说明优先级/触发阶段和 Backtrace/AI 前置条件，AI 密钥只写不回显。
- [ ] 动画条目页同时展示来源名称/集号和最终 TMDB 名称/Season/Episode，以及 AI 匹配状态、置信度、最终失败原因和策略尝试时间线。
- [ ] 实现作品库季度列表：以 `TMDB Series + 普通 Season` 为单位展示 TMDB 名称、Season/Series Cover、Season 和 EP 网格；逐 EP 的已下载/未下载状态只由 TMDB Episode 列表与规范完成记录计算。
- [ ] 实现作品库服务端分页排序和前端升/降序：最后业务更新时间（默认降序）、TMDB 名称、TMDB Season 开播日期、本地加入日期；缺失日期置后并使用 TMDB ID/Season 稳定排序。
- [ ] 实现 Cover 后端代理、本地缓存和占位图，不向浏览器暴露 TMDB API key；列表查询使用批量投影，避免按作品/EP产生 N+1 查询。
- [ ] 将 TMDB 未解析及 `tmdbid=0` 兜底条目放入“待补全 TMDB”，不生成 TMDB EP 网格或完成比例；恢复真实 TMDB 映射后再并入标准作品库。
- [ ] 待补全 TMDB 详情展示兜底完成记录、实际去重身份/作用域和跨来源重复风险，但不把它表示为 TMDB EP 下载状态。
- [ ] 增加 Mikan 作品规则 CRUD：按 `mikanid` 编辑 Bgm/TMDB Series/Season/EP Offset，预览影响范围，支持禁用、清除和显式重新匹配；已完成文件不自动移动。
- [>] 作品详情展示 Series/Season/Episode 的 TMDB 获取阶段、验证状态、人工偏移和最后解析时间（任务状态投影已展示最近成功策略和更新时间；季度详情、人工偏移与逐次验证时间线待实现）。
- [x] 实现四类删除命令及组合删除计划：schema v12 已完成指纹预览、逐项冻结、租约恢复、稳定失败码和部分失败重试；执行顺序为 qB 任务（永不带文件）→源文件→媒体文件→业务记录/claim，文件只允许捕获根目录内精确普通文件且不递归删目录；Minimal API 和 WebUI 四类独立勾选、目标预览、明确确认及 execution 状态查询已接入。
- [>] Web UI 支持按失败阶段、错误码、可重试性和处理状态筛选；提供安全的“重新匹配”，并区分待自动重试、需配置修复、需人工处理、已跳过和已兜底（失败任务显式重新匹配与脱敏失败原因已实现；筛选、错误码/可重试性投影及完整分类待实现）。
- [ ] 对环境变量覆盖字段显示有效值和只读锁定状态，禁止 Web 保存造成“已修改但不生效”的假象。
- [ ] 实现插件分类、启停、args/vars 和校验视图。
- [ ] 实现缓存/数据库浏览和安全删除。
- [ ] 实现实时日志过滤、暂停、恢复和断线重连。
- [ ] 完成响应式布局、空/错/加载状态和基本可访问性。
- [>] TypeScript 7 strict 类型检查和确定性编译已接入独立 CI job，提交产物必须与源码一致；DOM 单元测试和 Playwright UI E2E 待实现。
- [ ] 用 Tampermonkey + Mikan fixture 页验证“单集”“全集”“上传/获取过滤配置”。

## P10 — 组合与发布

- [ ] 完成 Host DI 和 CLI 行为。
- [>] 完成 Docker NativeAOT 镜像（双架构 Dockerfile、Buildx CI 和容器 smoke 已建立；本机无 Docker CLI，待 GitHub runner 实跑）。
- [ ] 添加非 root、PUID/PGID、healthcheck、SIGTERM、只读根文件系统验证。
- [ ] 添加连接外部下载器和内置 Compose 下载器的部署示例。
- [x] 固定官方 Docker：`data_path=/data`、`download_path=/download/incomplete`、`save_path=/download/anime`。
- [x] 提供写有上述绝对路径的 Docker 容器配置；Compose 卷与配置逐项一致，不依赖隐藏路径修正。
- [>] 官方 Compose 将 AnimeGoNet 与下载器的同一宿主目录统一挂载到 `/download`；配置和 smoke 断言已建立，真实容器验证待 CI。
- [ ] 验证外部下载器 `client.download_path` 路径转换与错误诊断。
- [ ] 构建并实机验证 `linux/amd64`，按确认结果验证 `linux/arm64`。
- [ ] 验证外部 C# 插件目录挂载、平台/RID 校验、非 root 启动和禁用回退。
- [ ] 发布并实机验证 `win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`、`osx-arm64` AOT artifacts。
- [ ] 生成 checksums、SBOM、第三方许可证。
- [ ] 完成新安装、旧配置升级、旧数据迁移演练。
- [ ] 完成全链路 JIT/AOT/Docker E2E。
- [ ] 用发布镜像完成 Web UI Playwright E2E。
- [ ] 编写用户迁移、部署、插件和运维文档。
- [ ] 标记第一个可用预发布版本。

## P11 — AnimeGoNetData

- [x] 确认独立数据仓库名称为 `AnimeGoNetData`；托管地址由该独立任务配置。
- [ ] 定义 `manifest.json`、subjects/episodes JSONL schema 和版本策略。
- [ ] 定义并验证 `setting.data_update` YAML schema、默认值、环境变量覆盖和热重载行为。
- [ ] 实现 Bangumi Archive 下载、校验、清洗、分片和 gzip。
- [ ] 建立每日检查 + 手动触发 GitHub Action。
- [ ] 建立数据唯一性、引用完整性、数量下限和确定性测试。
- [ ] 发布不可变 Release assets、SHA-256 和 latest manifest。
- [ ] AnimeGoNet 实现检查更新、流式下载、校验和 staging SQLite 导入。
- [ ] 实现事务切换、上版保留、失败回滚和离线手工导入。
- [ ] Web UI 增加数据版本、更新时间、检查/更新/回滚状态。
- [ ] 分别验证关闭调度、仅检查、自动下载待确认、自动导入和失败保留旧版。
