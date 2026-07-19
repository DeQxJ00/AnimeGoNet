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
- [ ] 领域模型拆分来源字段与 TMDB 规范字段：`SourceEpisodeNumber`、`TmdbSeriesName`、`TmdbSeasonNumber`、`TmdbEpisodeNumber`、`TmdbEpisodeId`。
- [ ] 增加 `MikanWorkMetadataRule`：`mikanid` 唯一键、`BangumiSubjectId`、`TmdbSeriesId`、`TmdbSeasonNumber`、有符号 `EpisodeOffset`、启用/版本/审计字段。
- [ ] 将上游 `assets/plugin/filter/Auto_Bangumi/raw_parser.py` 1:1 移植为 NativeAOT 友好的 C# 内置解析器，不在兼容层擅加年份保护、歧义拒绝或E04/EP04扩展；另建 `FileEpisodeCandidateResolver` 安全层，只在 Mikan SourceProfile 决定是否形成逐文件 `file_episode_candidate`；增加 AI/TMDB 验证后的本地统一偏移计算器，结果不一致时只禁止缓存学习，不否定已验证的逐文件映射。
- [ ] 增加 `MikanOffsetEvidence`/`MikanTrustedOffsetCache` SQLite 模型、事务状态机和默认关闭配置；按 `(mikanid,groupid,来源EP)` 唯一约束累计三个不同 EP。可信记录强制包含有效 `tmdb_id`、普通 `season` 和偏移；主程序在 AI 调用前命中后本地计算目标 EP，无候选、结果非正数或记录无效时回退正常流程。
- [ ] 增加 Series/Season/Episode 三层 `TmdbResolutionSource` 和解析运行/策略尝试引用。
- [ ] 通过全部配置/模型 parity tests。

## P3 — 存储

- [x] 建立 SQLite schema v1、幂等事务迁移器与显式 SQL 完成记录 store；启用 foreign key/WAL/busy timeout。
- [ ] 实现 SQLite KV/TTL store。
- [ ] 实现 bucket/list/get/delete 兼容接口。
- [ ] 移植目录 JSON 数据库扫描/索引/写入。
- [>] 移植全局 TMDB Episode 去重索引、来源 alias 和完成记录删除（全局完成唯一键和并发 TryAdd 已完成；alias/delete repository 待实现）。
- [>] 实现 `tmdbid=0` 的 `FallbackEpisodeClaim`、`FallbackCompletionRecord` 和分层唯一键（schema/约束已完成；事务 store 与早停编排待实现）。
- [ ] TMDB 恢复后事务合并 fallback 完成记录和 alias；多个记录收敛到同一 TMDB Episode 时标记 `DuplicateAfterResolution`，不重复下载、不自动删除文件。
- [ ] 移植 `tvshow.nfo` 生成和更新。
- [ ] 按需实现旧 Go 已知 bucket → JSON 导出及 .NET 幂等导入，不阻塞首版。
- [ ] 通过存储故障恢复、并发和迁移测试。

## P4 — HTTP、Feed、Torrent

- [ ] 移植代理、超时、重试、Host redirect、Cookie/API key。
- [ ] 移植 RSS 文件/URL/raw parse。
- [ ] 实现 Bencode/torrent/magnet/info-hash。
- [ ] 通过本地 fixture HTTP、RSS、torrent parity tests。

## P5 — 数据源

- [ ] 移植 Mikan。
- [ ] 从 Mikan RSS/页面 `/Home/Bangumi/{mikanid}` 提取并持久化正整数 `mikanid`；同 ID 的不同字幕组、标题和 Torrent 归入同一作品作用域。
- [ ] 移植 Bangumi API。
- [ ] 移植 Bangumi Archive 下载/缓存刷新。
- [ ] 移植 TMDB 搜索、相似度和季度匹配。
- [ ] 按 issue #15 实现 `TMDBFailBacktrace` / `tmdb_fail_backtrace`（默认 `false`）：季度匹配失败时沿 Bangumi“前传”关系逐项回溯首播日期，重新匹配同一 TMDB 剧集的季度。
- [ ] 实现 `TMDBFailUseAIMatchSeason` / `tmdb_fail_use_ai_match_season`（默认 `false`），每个下载任务只向大模型发送总标题、候选视频的相对文件名/字节容量及可空作品级 `bgmid`/`anidbid`/`imdbid`，一次返回整个文件列表的 TMDB 映射；不得以跨站标题不一致否定任务绑定，也不得直接复制来源 EP。
- [ ] 实现 `TMDBFailEpUseAIMatchSeason` / `tmdb_failep_use_ai_match_season`（按指定拼写，默认 `false`）：非 AI 季度匹配成功后先验证来源 EP；存在不对应时按下载任务执行一次 AI EP 匹配。
- [ ] 实现 Mikan 单文件发布日期Prompt门禁和显式开关：保留完整 `pubDate`，无偏移时按SourceProfile时区解析；即使开关开启也仅在Torrent实际文件条目数1、bgmid/日期有效且主程序成功计算 `bgm_episode_candidate` 时为真，Prompt直接结合文件名EP定向查TMDB，失败回通用流程。
- [x] 同步 AI 测试程序：增加可编辑开关和手工 `bgm_episode_candidate`、只读有效门禁；覆盖两种单文件Torrent、实际多文件禁用、无bgmid/日期/候选禁用和优先分支失败回退。
- [ ] 使用固定 JSON 请求/响应 DTO 调用 OpenAI-compatible API；模型返回必须由 TMDB Series/Season/Episode API 二次验证。
- [ ] 实现 AOT-safe 本地 Streamable HTTP MCP客户端和 function-calling工具循环；为 BGM/TMDB同名工具添加命名空间，并覆盖 JSON/SSE、会话、超时、取消和失败隔离。
- [ ] 实现可空 `anidbid` → `tmdbtv` 候选查询；固定URL、限制响应并阻止SSRF，候选未经 TMDB MCP验证不得采用。
- [ ] 实现可空 `imdbid` 规范化和 TMDB MCP external ID/find 候选查询；拒绝 Movie，最终 TV Series/Season/Episode 逐级验证。
- [ ] AI 和确定性匹配均拒绝 Season 0；Series/Season 已确认但 Episode 未匹配的文件保留原名放入 `<TmdbName>/Sxx/Other/`，并保存原因。
- [ ] 将确定性季度失败策略固定为 Skip=4、Backtrace=3、TitleSeason=2、FirstSeason=1；AI 匹配独立且默认关闭，启用时结果仍须 TMDB 验证。
- [ ] 为前传缺失、日期缺失、多前传、关系循环、回溯到首部仍不匹配、请求失败和取消建立 fixture。
- [ ] 为 AI 禁用/未配置/超时/限流/畸形 JSON/伪造 ID/多候选/文件列表冲突/缓存建立 fake-server 测试。
- [ ] 移植 Mikan → Bangumi → TMDB 编排与 fallback。
- [ ] 自动编排之前应用 Mikan 作品级人工规则；按 `TmdbEpisodeNumber = SourceEpisodeNumber + EpisodeOffset` 映射普通正片并验证目标 TMDB Episode。
- [ ] 人工规则无效时记录 `ManualOverrideInvalid` 并阻止静默自动覆盖；清除/禁用后才恢复自动策略链。
- [ ] 区分 TMDB 无结果、季度无匹配、瞬时网络错误和认证/配置错误，并验证重试耗尽后的兜底边界。
- [ ] 为完整失败保存 `failure_kind`、`tmdb_access_confirmed`、`bangumi_fallback_eligible/denial_reason`；仅 `SemanticNoMatch && tmdb_access_confirmed` 可进入 `tmdbid=0`，旧网络错误恢复后必须重新请求TMDB。
- [ ] 持久化元数据解析运行与策略尝试记录：阶段、策略、优先级、结果、错误码、脱敏原因、可重试性、次数、耗时和时间戳；重启后可查询。
- [ ] 通过 fixture parity 和受控 live smoke。

## P6 — 插件与业务流水线

- [ ] 实现 `AnimeGo.Plugin.Abstractions` 和 source/feed/parser/filter/rename/schedule 六类强类型 C# 插件契约。
- [ ] C# 移植 builtin feed/parser/filter/rename/schedule；默认运行不加载 Python。
- [ ] 实现内置 C# MikanTool 五级黑白名单规则，默认启用；精确复现 `Filiter0`～`Filiter4` 作用域、`1>2>3`、最终 AND、大小写敏感子串和多个 `Filiter0` 的 legacy 顺序行为。
- [ ] 为默认 Mikan SourceProfile 增加 `mikan_rss_filter_enabled` 总开关（默认 `true`）；关闭时 AnimeGoHelper `/api/rss` 记录 `SkippedByConfiguration` 后继续流水线，规则保留，进行中任务使用原快照。
- [>] 增加独立 `mikan_rss_priority_enabled` 批次优选开关（默认 profile 已启用，分组引擎已实现；SQLite 规则版本/批次编排待接入）。
- [>] 实现完全可配置的 `priority_groups[]`：纯 C# 引擎支持任意有序组/具名数组、统一 lowercase 和逐级淘汰；持久化 CRUD 待实现。
- [x] 优选组资格过滤后只有一个候选记录 `SingleCandidateBypass` 且不执行优先级组；多候选每轮剩一个立即短路，最终并列按原 RSS 顺序稳定选择。
- [x] 预置字幕语言、字幕封装、编码、分辨率四组，但引擎不写死组数或内容；name 仅展示，values 才参与匹配。
- [>] 实现优选阶段具名白名单/黑名单数组、黑名单优先和默认 720p 黑名单；SQLite CRUD/审计待实现。
- [>] RSS loser 产生 `SuppressedByHigherPriority` 决策且 winner 不隐式晋级；与 Torrent 获取/AI/任务创建的编排门禁待接入。
- [ ] 实现显式 `PluginCatalog` 注册，禁止反射扫描和动态 DLL 加载。
- [ ] 实现外部 C# 插件进程的 manifest、JSON Lines 协议、超时、取消、健康检查和退出隔离。
- [ ] 提供 `AnimeGo.Plugin.Sdk`、NativeAOT 插件模板和五 RID GitHub Actions 模板。
- [ ] 实现 `AnimeGo.PluginTool`。
- [ ] 移植 parser manager。
- [ ] 移植 ordered filter manager。
- [ ] 移植 feed → filter → parse → download pipeline。
- [ ] 通过上游所有插件/parser/filter fixture，以及外部 C# 插件协议故障注入测试。

## P7 — 首版 qBittorrent 下载客户端

- [ ] 定义稳定 `IDownloadClient` 契约，并将单下载器配置升级为命名实例字典；首版同机可配置多个 qBittorrent，连接、会话、熔断和状态隔离。
- [>] 实现 `SourceProfile` 和不可变路由快照：Mikan 默认 seed、可配置 U2→`pt` 路由、revision/文件策略/规则开关快照已落库；U2/TTG 默认文件策略仍待确认，CRUD、category/tag/做种策略待实现。
- [ ] 初始化默认 Mikan SourceProfile 的 `file_strategy=move`；Web改动只进入新任务快照，保存时明确提示该模式不做种。
- [>] 新增强类型输入适配层：Mikan/U2/TTG 统一校验、别名、mikanid/IMDb 规范化和冲突拒绝已实现；正式接口与 Torrent 抓取待实现。
- [ ] 实现 qBittorrent adapter 和 fake-server contract tests。
- [ ] 建立隔离 Docker Compose 下载环境。
- [ ] qBittorrent 通过 add/list/state/file-priority/pause/resume/delete/reconnect 真实容器测试。
- [ ] 同时启动 `bt`/`pt` 两个 qBittorrent 实例，验证 Mikan→bt、U2/TTG→pt，修改绑定不影响进行中任务。
- [ ] 旧YAML/环境变量出现Transmission时可读取并生成`UnsupportedDownloaderType`迁移诊断；不得启动任务、不得静默改成qB，Web保持可进入修复。

## P8 — 下载、重命名、刮削

- [ ] 移植下载管理状态机和 notifier。
- [ ] 移植重启恢复、去重、失败重试和删除 callback。
- [ ] 完成记录仅在下载、文件策略、重命名和必要 NFO/目录库写入全部成功后原子写入；RSS 早期检查并在提交下载器前事务复查。
- [ ] 移植 link/link_delete/move/wait_move。
- [ ] `move` 安全编排：下载完成后暂停任务，移动/跨卷复制校验、重命名/NFO/目录库成功后才移除下载器任务（不再删除文件）并写完成记录；失败保留可重试源文件。
- [ ] 将媒体整理、做种目标完成、删除下载器任务、删除下载源拆成独立持久化状态，避免上游 `DeleteFile=true` 完成回调导致 `link` 提前停止做种。
- [ ] 处理多文件、跨盘、目标冲突和部分失败。
- [ ] 多文件 Torrent 逐文件去重：qBittorrent 暂停添加后设置文件 priority；重复 EP 视频及绑定字幕不下载，其余 EP 正常继续。
- [ ] 实现字幕识别与唯一绑定：同 stem、多语言/默认/强制/SDH 后缀、按来源 EP 唯一匹配、`.idx/.sub` 成对处理；匹配后继承 TMDB EP，未匹配进入季度 `Other`。
- [ ] 串联媒体目录 DB 与 NFO。
- [ ] 任一季度匹配策略成功后，固定使用 TMDB `zh-CN` 名称（缺失时用 TMDB 原名）、Season Number 和 Episode Number 生成 `<TmdbName>/Sxx/Eyyy.ext`。
- [ ] 非 AI 季度结果依次执行同号 EP 快速校验、Bgm/TMDB 标题日期校验；失败且 `tmdb_failep_use_ai_match_season=true` 时进行一次 AI EP 映射，返回的 TMDB ID/Season 必须与已确认值相同。
- [ ] 保留来源名称和来源集号用于审计、去重诊断及 UI 展示；未经 TMDB API 验证的 AI 值不得参与路径、数据库键或 NFO。
- [ ] 多文件任务逐集验证 TMDB Episode；已确认 Episode 的正片正常落盘，Series/Season 已确认但 Episode 未匹配的文件进入季度 `Other`；Series/Season 未确认、重复映射或目标冲突时对应文件不落盘并可重试。
- [ ] 增加 `advanced.default.tmdb_fail_use_bangumi` 业务兜底开关，默认 `false`；关闭时 TMDB 完全失败即沿用原失败流程，不继续下载/刮削且不生成 NFO。
- [ ] 开关开启后，仅在权威TMDB访问成功且最终为确定性无匹配、已有有效 Bangumi Subject ID 且季度 fallback 成功时继续；动画根目录 `tvshow.nfo` 固定写 `<tmdbid>0</tmdbid>` 和对应 `<bangumiid>`。
- [ ] 验证已取得 TMDB ID、仅季度匹配失败时仍走原季度 fallback，不误入 Bangumi 完全失败兜底。
- [ ] 通过状态机、文件策略和合法小文件 E2E。

## P9 — 调度、Web API 与 Web 页面

- [ ] 实现六字段 Cron 调度、StartRun 和 NextTime。
- [ ] 实现 Bangumi/数据库/feed/plugin tasks。
- [ ] 实现优雅退出和取消传播。
- [ ] 移植 10 个 HTTP API。
- [>] 新增 `/api/v1/ingest` 通用批量 Torrent/URL 导入 API，沿用 `source + data[].torrent + data[].info`；旧 `/api/download/manager` 已转换到同一 command，`/api/rss` 与安全 Torrent staging 待接入。
- [ ] 将 passkey Torrent URL 和 `.torrent` announce 视为 secret：来源host白名单、逐跳redirect/DNS校验、限时限量、脱敏日志、受限 staging、确认接收后清理，禁止发送给AI。
- [ ] 新增下载器实例和 SourceProfile 的版本化 CRUD、连接测试、路由预览及引用保护 API。
- [>] 移植 access-key、响应 envelope、参数错误（直接/旧 hash access-key、ping/sha256、legacy manager envelope 和逐项导入错误已验证；其余旧 API 待移植）。
- [ ] 移植 WebSocket 日志 pause/resume。
- [ ] 兼容 `DeQxJ00/AnimeGoHelper`：`/ping`、`/api/rss`、`/api/download/manager`、`/api/plugin/config` 和 `Access-Key`。
- [ ] 将旧插件名 `filter/mikan_tool.py` 映射到 SQLite 过滤规则，不要求实际 Python 文件存在。
- [ ] 实现 Mikan 过滤 Web UI：RSS 过滤总开关、五档规则 CRUD/启停、关键词编辑、服务端样例预览、旧 JSON 导入导出、revision 冲突、快照回滚和过滤决策详情。
- [ ] 实现 Mikan RSS 优选 Web UI：独立开关、优先级组与具名数组的增删/拖动、values数组、具名黑白名单、默认720p黑名单、批次预览、决胜原因、revision与回滚。
- [ ] 移植静态页并生成 OpenAPI。
- [ ] 通过 API/WS 契约差分测试。
- [ ] 创建 Web 前端工程、类型化 API client 和前端测试基线。
- [ ] 实现仪表盘和下载器/任务状态。
- [ ] 实现两层下载进度投影：qB规范状态/百分比/容量/速度/ETA/Seeds/Peers与AnimeGoNet解析/移动/重命名/字幕/NFO/数据库阶段分离，qB 100%不得提前标业务完成。
- [ ] 实现按实例隔离的qB同步器和`DownloaderTaskSnapshot`：活动约2秒、空闲约10秒、单实例单在途、熔断隔离、离线保留stale快照、重启按实例+hash恢复。
- [ ] 实现下载列表/详情/文件级priority与wanted进度、筛选搜索分页和状态时间线；多文件总进度仅统计wanted文件，metadata未知时使用不确定态。
- [ ] 实现暂停、恢复和AnimeGoNet业务重试；删除只跳转四类删除中心。首版不复刻Tracker/Peer明细、piece图、限速、强制校验/汇报和qB全局设置。
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
- [ ] 作品详情展示 Series/Season/Episode 的 TMDB 获取阶段、验证状态、人工偏移和最后解析时间。
- [ ] 实现四类删除命令及组合删除计划：业务记录、下载器任务、下载源文件、媒体库文件；逐项确认、路径约束、部分失败重试和审计。
- [ ] Web UI 支持按失败阶段、错误码、可重试性和处理状态筛选；提供安全的“重新匹配”，并区分待自动重试、需配置修复、需人工处理、已跳过和已兜底。
- [ ] 对环境变量覆盖字段显示有效值和只读锁定状态，禁止 Web 保存造成“已修改但不生效”的假象。
- [ ] 实现插件分类、启停、args/vars 和校验视图。
- [ ] 实现缓存/数据库浏览和安全删除。
- [ ] 实现实时日志过滤、暂停、恢复和断线重连。
- [ ] 完成响应式布局、空/错/加载状态和基本可访问性。
- [ ] 通过 TypeScript 类型检查、DOM 单元测试和 Playwright UI E2E。
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
