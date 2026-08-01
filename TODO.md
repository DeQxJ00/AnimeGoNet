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
- [x] 确认 AI 季度与 Episode 是同一任务级流程：一个开关、一个 Prompt、每个任务最多一次语义调用；确定性季度已成功但 EP 无法对应时，只有该任务从未尝试 AI 才能首次触发，默认 `false`。
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
- [x] 验证 Minimal API、WebSocket、静态文件 NativeAOT：win-x64 原生进程 smoke 已覆盖 `/ping`、静态 WebUI、WebSocket upgrade 与 pause 控制帧。
- [x] 验证 Microsoft.Data.Sqlite NativeAOT（win-x64 原生进程完成 migration、integrity 与状态读取）。
- [x] 验证 YAML AST、Cron、HTML 解析候选依赖 NativeAOT：HTML scanner 与六字段 Cron/调度器无反射；部署 YAML 使用 YamlDotNet `YamlStream` AST 显式遍历，限制 UTF-8/大小/深度/节点并拒绝重复键，三者均通过 win-x64 NativeAOT publish 与原生进程 smoke。
- [x] 建立 published-binary smoke 脚本（`eng/smoke-native.ps1`）。

## P2 — 领域与配置

- [ ] 移植所有领域模型、枚举和错误类型。
- [x] 建立首阶段强类型配置/目录模型与校验：Docker 三路径、命名 qBittorrent、Mikan `move` 默认、AI 600 秒和高风险 fallback 默认关闭。
- [>] 固化 JSON source-generation context（状态、统一导入和 legacy manager DTO 已覆盖；后续 API DTO 持续加入）。
- [ ] 移植 hash、name、path、时间等纯函数。
- [x] 移植默认 YAML 与注释：首次启动 `CreateNew` 原子生成 1.7.1、无 BOM UTF-8、Unix `0600`；涵盖路径、命名 qB、来源绑定、TMDB/Bangumi/AI、四档失败链、Torrent、Cron 和数据更新，secret 为空且高风险开关默认关闭。
- [>] 移植环境变量覆盖：规范嵌套键、现有扁平键、旧 qB 兼容键及命令行优先级已接入；应用字段和下载器实例字段的部署锁均在 API/WebUI 逐字段显示来源，拒绝改写且保存其他字段时不把环境凭据复制进私有覆盖。路径及完整旧配置 precedence parity 仍待补齐。
- [>] 移植配置检查、路径初始化和资源释放：严格 YAML 输入边界、强类型值校验、三路径和下载器子目录边界、首次目录/文件初始化、宿主释放均已有测试；全部旧 Go 配置异常 parity 仍待补齐。
- [>] 移植配置 `1.1.0` → `1.7.1` 升级链与备份：1.1.0～1.7.1 旧 qB `setting:`/`advanced:` 现默认保存同目录原字节 `CreateNew` 版本化备份，再经同目录临时文件原子重写规范 1.7.1；路径/qB/Mikan策略/category/做种/动态 tag 模板/TMDB/代理/失败链/Cron 与 `advanced.source|anidata.mikan.cookie` 已迁移，错误值在落盘前拒绝，Transmission 保持原文件并 fail closed；其余历史字段 parity 仍为进行中。
- [x] 新配置加入 Skip/Backtrace/TitleSeason/FirstSeason 四档确定性季度策略和一个任务级 AI 元数据开关，全部默认 `false`；规范 YAML/扁平键/API/WebUI 已使用 `ai_use_metadata_match`，旧双键兼容读取、新安装默认注释及旧 YAML 自动重写新默认值均已完成。
- [x] 增加 OpenAI-compatible AI 配置 DTO、扁平环境变量、敏感值脱敏和 source-generated JSON 上下文；API 只返回 provider/base/model/工具端点与 `api_key_configured`，不返回密钥。
- [>] 领域模型拆分来源字段与 TMDB 规范字段：权威 `TmdbSeries`/`TmdbSeason`/`TmdbEpisode` 与三级验证结果已建立；来源字段和持久化编排仍待串联。
- [x] 增加 `MikanWorkMetadataRule`：`mikanid` 唯一键、`BangumiSubjectId`、`TmdbSeriesId`、`TmdbSeasonNumber`、有符号 `EpisodeOffset`、启用/版本/审计字段；数据层已实现 revision 冲突保护、禁用和清除，API/编排接入在对应阶段继续。
- [x] 将上游 `assets/plugin/filter/Auto_Bangumi/raw_parser.py` 1:1 移植为 NativeAOT 友好的 C# 内置解析器：19 组由 develop 分支 Python 产出的 golden fixture 已逐字段覆盖标题、季度、集号、字幕、发布组、分辨率和来源，并明确保留不识别 E04/EP04 等原始语义。独立 `FileEpisodeCandidateResolver` 才拒绝年份/分辨率占位、歧义和非正片，只对 Mikan SourceProfile 落逐文件候选；AI/TMDB 验证后的本地统一偏移计算及“不一致只禁止学习”已在 Episode worker 完成。
- [>] 已建立 NativeAOT-safe Torrent 文件和 Mikan RSS title EP 安全分类层：兼容上游 Go `ParseEp` 的 `[04]`/`[04v2]`/` - 11`/`EP12`/`第12话`，RSS title 另支持不受扩展名截断的最后可靠标记；小数集与 SP/OVA/OAD/PV/NCOP/NCED/Menu/S00E 均不形成普通整数。入库的 Mikan `file_episode_candidate` 现改由 raw_parser.py 兼容层与独立安全层决定，其他 adapter 固定不写；确认 Season 后仍逐文件经 TMDB Episode API 验证，RSS 批次与逐文件候选的跨请求审计仍待补全。
- [x] 增加 `MikanOffsetEvidence`/`MikanTrustedOffsetCache` SQLite 模型、事务状态机和默认关闭配置；按 `(mikanid,groupid,来源EP)` 唯一约束累计三个不同正整数 EP，并在冲突/歧义时撤销可信状态；命中后在 AI/TMDB Episode 调用前本地计算并验证目标 EP，API/WebUI 显示 Learning/Trusted/ConflictReset。
- [x] 增加 Series/Season/Episode 三层 `TmdbResolutionSource` 和解析运行/策略尝试引用：schema v32 在解析完成事务中固化 Series/Season 的 Run+Attempt，并为每个 Episode/字幕文件保存精确 Attempt；API、任务面板和作品库均显示权威来源及证据引用，混合文件明确下钻到逐文件详情。
- [ ] 通过全部配置/模型 parity tests。

## P3 — 存储

- [x] 建立 SQLite schema v1、幂等事务迁移器与显式 SQL 完成记录 store；启用 foreign key/WAL/busy timeout。
- [x] 实现 SQLite KV/TTL store：schema v22 `cache_buckets/cache_entries`、原子批量 JSON、绝对 TTL、惰性/全局过期清理与并发写入已验证。
- [x] 实现 bucket/list/get/delete 兼容接口：`bolt`/`bolt_sub` 隔离、旧 envelope、只读 archive 删除保护和 Access-Key 已验证。
- [x] 移植目录 JSON 数据库扫描/索引/写入：按上游结构原子写 `anime.a_json`、`anime.s_json`、`*.e_json`，启动扫描并以 schema v27 建立 SQLite 索引/运行/拒绝审计；每日 6 点六字段 Cron 刷新，API/WebUI 可查看和手动刷新。
- [x] 移植全局 TMDB Episode 去重索引、来源 alias 和完成记录删除：全局完成唯一键、并发 TryAdd、逐文件 EpisodeClaim、已完成/进行中精确跳过及失败释放均已完成；通用 alias repository 支持规范化写入/来源 EP 查询，正常整理在 completion 同一事务写 alias，删除完成记录级联移除 alias 并释放对应 completed claim。
- [x] 实现 `tmdbid=0` 的 `FallbackEpisodeClaim`、`FallbackCompletionRecord` 和分层唯一键：schema/约束、事务 claim/release/complete、同作用域早停、同任务多文件共享 claim，以及失败释放后重试均已接入。
- [x] TMDB 恢复后事务合并 fallback 完成记录和 alias；多个记录收敛到同一 TMDB Episode 时标记 `DuplicateAfterResolution`，不重复下载、不自动删除文件；恢复前逐级在线验证 TMDB Series/Season/Episode。
- [x] 移植 `tvshow.nfo` 生成和更新：整理时原子生成，TMDB fallback 恢复后以持久化重写作业更新，均限制在捕获的 save root 内。
- [ ] 按需实现旧 Go 已知 bucket → JSON 导出及 .NET 幂等导入，不阻塞首版。
- [ ] 通过存储故障恢复、并发和迁移测试。

## P4 — HTTP、Feed、Torrent

- [x] 移植代理、超时、重试、Host redirect、Cookie/API key：TMDB/Bangumi 支持独立 API 地址、HTTP(S)/SOCKS5 代理、逐次超时、0～10 次可配置额外重试与 0～300 秒间隔；只重试连接/超时/429/5xx，每次重建请求，404/认证/协议失败不重试，调用方取消立即终止。TMDB API key/Bearer、Bangumi User-Agent 与 Mikan SourceProfile 级 `.AspNetCore.Identity.Application` Cookie 均已覆盖；Cookie 仅发往原始 Host，跨 Host redirect 必定剥离，API/WebUI 只显示配置状态、不回显值。
- [x] 移植 RSS 文件/URL/raw parse：已实现 5 MiB 上限、禁用 DTD/外部实体、首个 enclosure、无 enclosure 跳过、非法 length 归零、Mikan `pubDate` 日期兼容和稳定错误码；URL/文件读取边界可注入测试，尚未暴露为公网抓取 API。
- [x] 实现 Bencode/torrent/magnet/info-hash：严格 v1 Bencode、原始 info 字节 SHA-1、单/多文件清单、padding/路径/数量/总量校验已完成；magnet 现按上游支持首个 `urn:btih` 的 40 位 hex/32 位 Base32、首个 dn 和 tracker 计数，并保证返回/异常不保留 URI、tracker 或 passkey。
- [x] 通过本地 fixture HTTP、RSS、torrent parity tests：RSS raw/file/注入式 HTTP、缺字段、损坏 XML、DTD、错误脱敏、mikanid、两条 magnet 及上游固定提交四个真实 `.torrent` 的 info-hash/名称/总量/17 个文件 parity 已通过；真实 loopback socket server 另验证 chunked RSS、原始请求 path/query、Host/User-Agent、禁止自动 redirect、固定已校验 IP 连接与流式响应。生产 SSRF 策略仍拒绝 loopback/private 地址。

## P5 — 数据源

- [>] 移植 Mikan：RSS/页面身份、`mikanid`/`groupid`、作品页 `bgmid` 发现、五档 legacy filter、新黑白名单与有序优选、SourceProfile 级私有身份 Cookie、winner 统一导入均已接入；完整上游边界仍按本节其他未完成项继续验收。
- [x] 从 Mikan RSS/页面 `/Home/Bangumi/{mikanid}` 提取并持久化正整数 `mikanid`：RSS source URL 优先、channel link 回退及 path/query 解析已验证；RSS winner 会安全抓取对应作品页，仅接受 `p.bangumi-info` 内 `bgm.tv`/`bangumi.tv` 的正整数 Subject 链接，把 `bgmid` 与发现状态/失败码写入 schema v26 批次和统一导入任务。成功结果按批次复用；失败不下载且允许下一次显式 RSS 处理重试。
- [x] 移植 Bangumi API：已按上游 `/v0/subjects/{bgmid}`、官方 `/v0/subjects/{bgmid}/subjects` 与分页 `/v0/episodes` 实现 AOT-safe Subject/关系/Episode 客户端、固定 User-Agent、日期/身份/分页校验和稳定网络/协议失败分类；主程序默认先读活动 AnimeGoNet Data Archive 的 SQLite Subject/完整 Episode 快照，不存在、不完整或零集未知时回退在线 API，关系保持在线，版本激活/回滚无需重启即生效。
- [>] 移植 Bangumi Archive 下载/缓存刷新：主程序已支持 manifest/asset 下载、校验、原子版本导入、活动/前一版本切换与回滚，并已把活动版本接入 Bangumi Subject/Episode 读取；从上游原始 Archive 生成、清洗、分片、gzip 和发布数据包仍属于 P13 待实现边界。
- [>] 移植 TMDB 搜索、相似度和季度匹配（上游 discover/tv 查询参数、四步去后缀、UTF-8 byte 相似度、0.75 阈值、普通季度过滤、90 天日期阈值、zh-CN DTO 与 Series/Season/Episode 三级身份验证已实现；安全可取消的网络/429/5xx 重试已接入；Bangumi Subject → TMDB Series/Season、活动 Archive 缓存与逐文件 Episode worker/运行审计已接入，Bangumi Episode 确定性匹配的其余 parity 待实现）。
- [>] 实现 AnimeGoNet 新增的 `TMDBFailBacktrace` / `tmdb_fail_backtrace`（默认 `false`）：日文名、中文名、各自多轮清理及每轮全部合格 Series 均以完整 `tmdbid+Season` 为成功条件；P3 已按每个 Bangumi 前作的日文名、中文名和开播日期重新联合搜索，可恢复不同 TMDB Series，并覆盖多层、同层稳定排序、缺日期继续、visited 防环、成功早停和错误后继续低优先级策略；TMDB/Bangumi 网络重试策略已完成，受控 live fixture 仍待实现。
- [x] 实现统一 `ai_use_metadata_match`（默认 `false`）：一个共享解析器按下载任务发送总标题、候选视频相对文件名/字节容量及可空作品级 `bgmid`/`anidbid`/`imdbid`，一次返回整个文件列表的 TMDB Series/Season/Episode 映射；不得以跨站标题不一致否定任务绑定，也不得直接复制来源 EP。
- [x] 季度与 Episode 阶段共用同一 AI 尝试门禁和 `ai_metadata` 审计；确定性季度成功后普通 EP 匹配失败可首次触发，季度阶段成功或失败尝试过 AI 后均禁止 Episode 阶段再次调用；历史 `ai_season`/`ai_episode` 记录仍能阻止重复调用。
- [x] 实现 Mikan 单文件发布日期 Prompt 门禁和显式开关：保留完整 `pubDate`，无偏移时按 SourceProfile 时区解析；仅在实际 Torrent 文件条目数为 1、bgmid/日期有效且主程序成功计算 `bgm_episode_candidate` 时启用，日期候选失败安全回通用统一 AI 流程。
- [x] 同步 AI 测试程序：增加可编辑开关和手工 `bgm_episode_candidate`、只读有效门禁；覆盖两种单文件Torrent、实际多文件禁用、无bgmid/日期/候选禁用和优先分支失败回退。
- [x] 使用固定 JSON 请求/响应 DTO 调用 OpenAI-compatible API；输入/输出结构、文件身份和结果完整性先校验，模型候选再由 TMDB Series/Season/Episode API 二次验证。
- [x] 实现 AOT-safe 本地 Streamable HTTP MCP 客户端和 function-calling 工具循环；BGM/TMDB 工具使用命名空间，覆盖 JSON/SSE、session、工具 schema 缓存、超时、取消、响应上限和失败隔离。
- [x] 实现可空 `anidbid` → `tmdbtv` 候选查询：URL 固定、模型零参数、响应有界、禁止重定向/代理并将 DNS 连接钉在公网地址；候选仍需 TMDB MCP 与主程序 API 验证。
- [x] 实现可空 `imdbid` 规范化和固定零参数 `lookup_imdb_tmdb_tv`：主程序调用 TMDB MCP external ID/find，程序侧删除 Movie 结果，只返回正整数 TV Series 候选，最终 Series/Season/Episode 逐级验证。
- [x] AI 和确定性匹配均拒绝 Season 0；Series/Season 已确认但 Episode 未匹配的小数集、特别篇、普通 TMDB EP 不存在、AI 未匹配和孤立字幕均持久化 `Other` 及稳定原因，实际保留原名整理到 `<TmdbName>/Sxx/Other/`；不生成 Episode completion/alias/claim 或 `Eyyy.e_json` 伪进度。AI 仅在统一开关开启且任务此前未尝试时调用，季度阶段尝试后不会在 Episode 阶段重复调用。
- [x] 将确定性季度失败策略固定为 Skip=4、Backtrace=3、TitleSeason=2、FirstSeason=1；四级确定性策略已按优先级接入并验证早停/错误降级，独立统一 AI 阶段已接入且 Series/Season/Episode 结果必须经 TMDB 验证。
- [x] 为 P3 建立完整图/故障 fixture：无前传直接穷尽；缺日期仍继续遍历；同层多前传按开播日期降序/ID 升序；关系循环由 visited 终止；回溯到首部仍不匹配会穷尽日文/中文名及每名清理词；TMDB 网络失败保留稳定类型/码且不伪装成无匹配；Bangumi 请求失败向编排层传播；取消立即中断且不产生 fallback 结果。
- [x] 为 AI 禁用/未配置/超时/限流/畸形 JSON/伪造 ID/多候选/文件列表冲突/缓存建立 fake-server 测试：统一开关关闭时零请求/零审计；配置缺失在联网前失败；超时、429 重试与耗尽、认证和外层/模型 JSON 错误使用稳定安全分类；多个 provider `choices` 作为歧义拒绝；不存在的 TMDB Series 经权威二次验证拒绝；文件身份冲突在 TMDB 访问前拒绝；同 MCP endpoint 的工具 schema 只发现一次但每次会话仍重新初始化。
- [x] 移植 Mikan → Bangumi → TMDB 编排与 fallback：RSS winner 已按上游作品页关系自动发现并持久化 `bgmid`，携带 `bgmid` 的已下载任务由内置 worker 执行 Bangumi Subject → TMDB Series → 日期季度，并持久化每次策略；Backtrace、统一 AI 和固定 S01 的 Bangumi 完全兜底均已串联。页面缺链接、歧义、非可信域名、网络失败分别使用稳定失败码，失败批次不提前下载。
- [x] 自动编排之前应用 Mikan 作品级人工规则；完整 TMDB Series/Season 覆盖由专用 worker 优先领取并权威验证，EP Offset 已在逐文件 TMDB Episode 验证前应用且无效时阻断静默回退；可信自动 offset 已与字幕绑定、qB 文件 priority/恢复、实际文件整理、单一 completion 和安全 cleanup 串联。命中时主视频与字幕共享本地推导的 TMDB EP，零 AI、零 TMDB Episode 请求，字幕保留语言后缀且不创建第二 completion。
- [x] 人工规则无效时记录人工覆盖策略失败并阻止静默自动覆盖；清除/禁用后可通过 `POST /api/v1/metadata/tasks/{taskId}/retry` 显式重新匹配，事务性恢复自动策略队列且保留历史运行记录，并拒绝活动租约/非失败状态。
- [x] 区分 TMDB 无结果、季度无匹配、瞬时网络错误和认证/配置错误：客户端稳定分类 SemanticNoMatch/Network/RemoteService/Authentication/Configuration/Protocol/InvalidInput 且异常脱敏；连接/逐次超时/429/5xx 可配置重试，404/认证/协议失败不重试，取消立即传播。
- [x] 为完整失败保存 `failure_kind`、`tmdb_access_confirmed`、`bangumi_fallback_eligible/denial_reason`：SQLite Run 持久化、最终门禁、列表/详情 API 与 WebUI 决策说明均已串联；只有权威 `SemanticNoMatch + access_confirmed` 可进入兜底，Network/RemoteService/Authentication/Configuration/Protocol/InvalidInput/Ambiguous 全部经过处理器测试证明拒绝且不创建 `tmdbid=0`。
- [x] 持久化元数据解析运行与策略尝试记录：阶段、策略、优先级、结果、错误码、脱敏原因、可重试性、次数、耗时和时间戳；SQLite 重启后可按任务查询，版本化 API 与任务卡片策略时间线均已接入。
- [ ] 通过 fixture parity 和受控 live smoke。

## P6 — 插件与业务流水线

- [x] 实现 `AnimeGo.Plugin.Abstractions` 和 source/feed/parser/filter/rename/schedule 六类强类型 C# 插件契约；契约项目启用 trim/AOT analyzer，稳定 DTO 不引用 Web、SQLite 或下载客户端。
- [x] C# 移植 builtin feed/parser/filter/rename/schedule；`mikan-rss`、`mikan-title`、`mikan-tool`、`anime-library`、`staged-torrent-dispatch` 均由编译期目录注册并接入真实 feed→filter→parse→staging、整理与调度入口，默认运行无 Python 运行时或脚本加载路径。
- [x] 实现内置 C# MikanTool 五级黑白名单规则：纯 C# 引擎、schema v15 规则/快照、legacy `/api/plugin/config`、Episode identity parser、schema v16 逐候选审计，以及 `/api/rss` 的安全页面抓取/批内缓存/Filiter0..4 前置执行均已串联；被拒绝或身份失败的候选不进入新优选与 staging。现代管理 API 与 WebUI 已支持总开关、五档 CRUD/排序/启停、精确 JSON 数组关键词、服务端逐档预览、旧 JSON 导入导出、revision 冲突和快照回滚。
- [x] 默认 Mikan SourceProfile 的 `mikan_rss_filter_enabled` 已默认 `true` 并真实控制 `/api/rss`；关闭时零页面请求、逐项记录 `SkippedByConfiguration`、继续优选/staging且规则不变。来源 CRUD/UI 已完成；RSS 从请求起点显式贯穿同一 SourceProfile revision/双开关/下载器路由快照，并发修改只影响下一次请求。
- [x] 增加独立 `mikan_rss_priority_enabled` 批次优选开关：默认 profile 已启用，schema v13 规则版本、默认初始化、预览 API 和真实 `/api/rss`/现代 RSS 批次均已接入；禁用时真实批次逐项记录 `SkippedByConfiguration`、不执行本功能的黑白名单/有序组且不清空规则，SQLite 审计保留当批开关状态。
- [x] 实现完全可配置的 `priority_groups[]`：纯 C# 引擎支持任意有序组/具名数组、统一 lowercase 和逐级淘汰；schema v13 store 与 GET/PUT expected-revision 全快照 API 支持增删/排序，schema v25 保存每个 revision 的关系型历史快照并支持安全回滚；WebUI 已提供白/黑名单、组/数组 CRUD、启停、上下移动、服务端预览和历史回滚。
- [x] 优选组资格过滤后只有一个候选记录 `SingleCandidateBypass` 且不执行优先级组；多候选每轮剩一个立即短路，最终并列按原 RSS 顺序稳定选择。
- [x] 预置字幕语言、字幕封装、编码、分辨率四组，但引擎不写死组数或内容；name 仅展示，values 才参与匹配。
- [x] 实现优选阶段具名白名单/黑名单数组、黑名单优先和默认 720p 黑名单；schema v13 SQLite CRUD/版本快照、schema v14/v16 批次决策与实际执行组审计、API/WebUI 编辑/预览/回滚和真实 RSS 批次执行均已验证。
- [x] RSS loser 产生 `SuppressedByHigherPriority` 且 winner 不隐式晋级；`POST /api/rss` 依次执行安全 feed 获取/精确 ep_links → legacy Filiter0..4（按需安全页面身份、批内缓存）→ 新黑白名单/有序优选 → winner 原子统一 staging，并兼容 HTTP 200 + code 200/300 与成功消息。
- [x] 实现显式 `PluginCatalog` 注册，禁止反射扫描和动态 DLL 加载；目录校验稳定小写 ID、单一类别、全局重复 ID 和确定性顺序，Mikan/U2/TTG 统一导入已通过目录真实路由。
- [x] 实现外部 C# 插件进程：manifest、JSON Lines、环境隔离、惰性复用、插件级指数退避/自动禁用和独立数据目录；启停/args/vars 使用 revision 原子私有文件，管理 API/schema 表单支持 writeOnly 留空保留/显式清除；stderr 按插件 ID 结构化脱敏限流；source/feed/parser/filter/rename/schedule 六类外部包均以固定 operation、source-generated DTO 和严格结果校验注册进 `PluginCatalog`，非法结果关闭会话并进入退避。
- [x] 提供 `AnimeGo.Plugin.Sdk`、NativeAOT 插件模板和五 RID GitHub Actions 模板：SDK 以 source-generated `JsonTypeInfo` 驱动六类强类型处理器，严格执行 initialize/execute/health/shutdown JSON Lines、环境身份、输入输出上限和稳定业务错误；模板包可生成六类最小实现，五 RID workflow 使用原生 Windows/Linux ARM64 与 macOS ARM64 runner 实际发布，并由 `eng/verify-plugin-template.ps1` 生成六类项目、Release 编译、NativeAOT 发布及四阶段真实进程 smoke。
- [x] 实现 `AnimeGo.PluginTool`：AOT-safe `validate/run/pack` CLI 复用主程序 manifest、配置 schema、进程协议和六类结果校验；严格 fixture UTF-8/JSON/operation/config 边界、包树权限/链接/容量审计、内容摘要、确定性 ZIP 与原子覆盖已完成。专用 fake 测试覆盖退出码、脱敏、生命周期、健康失败、临时/显式 data path、变更竞态和可重复打包；五 RID template workflow 使用原生工具对生成的 NativeAOT filter 执行真实 validate→run→pack。
- [x] 移植 parser manager：保持上游“第一个启用/显式指定 parser”语义，解析无匹配或错误时不自动切换后续实现；未知 ID 使用稳定配置错误。
- [x] 移植 ordered filter manager：按显式配置或目录顺序逐级传递 accepted items，插件错误/无效索引立即终止，拒绝项不会进入后续 filter；空显式链等价于跳过过滤。
- [>] 移植 feed → filter → parse → download pipeline：有界 feed、安全 URL 获取、legacy `/api/rss`、Filiter0..4、来源 EP、新优选、schema v16 审计/租约和 winner→统一 staging 已串联；download worker 到最终整理的真实 qB/container 全链验收仍待实现。
- [>] 通过上游所有插件/parser/filter fixture，以及外部 C# 插件协议故障注入测试：外部协议 fake/真实进程已覆盖成功生命周期、业务错误、严格响应、超限、超时、取消、崩溃、脏 stdout、stderr、并发、健康失败、关闭期限与 manifest 竞态；六类 adapter 已覆盖配置合并、禁用、业务错误、未知/重复字段、索引完整性、URL 指纹和路径逃逸；上游剩余 fixture 全量 parity 仍待完成。

## P7 — 首版 qBittorrent 下载客户端

- [x] 定义稳定 `IDownloadClient` 契约，并将单下载器配置升级为命名实例字典；`bt`/`pt` 客户端、Cookie 会话、实例隔离、按实例串行操作、失败隔离、可选客户端版本/默认保存路径诊断，以及按实例 2～120 秒指数退避/熔断均已实现。
- [x] 实现 `SourceProfile` 和不可变路由快照：Mikan 默认 seed、U2/TTG/Mikan 版本化 CRUD、启停、下载器绑定、Host 白名单、规则开关、category、静态附加 tags、qB 做种分钟、动态 tag 模板、Mikan 私有身份 Cookie、乐观并发和任务/RSS引用保护 API/WebUI/路由预览已完成。Cookie 按来源隔离且跨 Host redirect 剥离；RSS 并发修改不混用新旧 revision。动态模板随任务冻结，元数据确认后在恢复下载前按规范季度日期和首个普通 EP 渲染并写 qB，跳过/失败均有持久状态和事件。schema v36 另为每个 Mikan 来源保存只写 RSS URL、六字段 Cron、启用状态和最近执行审计；后台启动、CRUD 热更新、旧 revision 失效、中断恢复与 passkey 不回显均已验证。
- [x] 初始化默认 Mikan SourceProfile 的 `file_strategy=move`；API 修改只影响新任务，返回值明确提示该模式移动后不继续做种。
- [x] 新增强类型输入适配层：Mikan/U2/TTG 统一校验、别名、mikanid/IMDb 规范化和冲突拒绝已实现；统一/旧入口在请求期执行安全 Torrent staging 并原子保存文件清单，后台 worker 按不可变下载器路由暂停投递 qB、确认 hash 后进入元数据和逐文件准备。
- [x] 实现 qBittorrent 5 WebUI API adapter 和 fake-handler contract tests：登录、torrent/file list、multipart add（category/tags/seedingTimeLimit）、file priority、stop/start/delete、状态映射、严格 hash/index/priority/做种分钟校验与失败响应。
- [x] 实现 staged Torrent 后台 dispatch：SQLite并发租约、崩溃租约恢复、不可变实例路由、paused add、同hash幂等检查、已有/新增任务显式再暂停、qB确认、download job事务与确认后staging清理。
- [x] 接入本机 `TestSpace` portable qBittorrent 隔离沙箱：ignore、独立测试项目、端口所有者/profile/版本、用户名密码 Cookie 登录、list 和三路径 smoke 已通过；qB 专用脚本用 FQN 过滤只启动 `QbittorrentSandboxTests`，不会串跑同项目的 TMDB live 测试；默认 CI 不启动该实例，也未创建 Torrent。
- [x] 实现下载器路径可见性与硬链接能力探测：仅在显式 API/WebUI 操作时向实例 `download_path` 和全局 `save_path` 写入同名随机临时文件，验证后尽力清理；缺目录、权限、跨文件系统/挂载和平台不支持均返回稳定脱敏错误码，Windows/Linux/macOS 使用 AOT-safe 原生调用。
- [x] 建立隔离 Docker Compose 下载环境：专用 Compose 只绑定随机回环端口，使用临时 data/download/qB profile 根目录、非 root AnimeGoNet、只读根文件系统和退出清理；不复用 TestSpace 或生产卷。
- [>] qBittorrent 真实容器 smoke 已接入 Docker CI：从首启日志读取临时密码后设置隔离测试密码，逐实例验证登录、版本、默认路径、reconnect、add/list/files/file-priority/start/stop/delete，并使用仅含 `127.0.0.1:9` tracker 的 5 字节 fixture、唯一 category/tag 和整项目清理。本机 TestSpace 现可显式执行同一安全 fixture 的统一导入→SQLite→dispatch→真实 qB 验收；本机没有 Docker CLI，首次 GitHub Actions 实跑结果仍待验收。
- [>] 双实例容器已同时连接并通过各自路径/硬链接探测；CI route-preview 验证 Mikan→bt、U2/TTG→pt，既有并发快照测试验证绑定修改不改写已创建任务。本机单实例已验证真实统一导入进入绑定 bt、保持暂停、捕获路径快照并彻底清理；统一任务分别进入两个容器的全链仍待 Docker runner 验收。
- [x] 旧 YAML/环境变量出现 Transmission 时读取并生成 `UnsupportedDownloaderType`：按 `ANIMEGO_CLIENT`→显式 `ANIMEGO_CONFIG`/`--config`→`data_path/animego.yaml` 检测，只读取 `setting.client.client` 且不回显凭据；诊断未解除时强制关闭 workers、替换为空下载器 registry、拒绝导入/恢复/连接与路径测试，Web/API 保持可用并显示修复原因，绝不静默转成 qB。

## P8 — 下载、重命名、刮削

- [x] 移植下载管理状态机和 notifier：staged→dispatching→download_preparing→metadata_resolved→download_queued/skip→downloading/downloaded 已接入；后台 qB 快照同步把不可变做种目标、单调累计秒数和 waiting/seeding/completed 写入 schema v33，整理 worker 只按该持久化门禁推进，准备、整理和 cleanup 均有独立租约与安全重试。
- [x] 移植重启恢复、去重、失败重试和删除 callback：dispatch lease 恢复、qB 同 hash 幂等、按实例+hash 运行快照恢复、离线 stale/实例 circuit breaker/健康探测与退避重试、每 job 不可变 download/save root 均已实现。上游重命名完成 callback 直接执行 `DeleteFile:true`；新程序按安全语义替换为持久化 `organizing_cleanup` 租约，固定 `deleteFiles=false`。qB 故障时已落盘媒体和 completion 保持不变，Store cleanup 租约可释放重领，健康探测关闭 circuit 后只重试 qB 任务清理。
- [x] 完成记录仅在下载、文件策略、重命名和必要 NFO/目录库写入全部成功后原子写入：worker 在所有文件、原子 `tvshow.nfo`、上游兼容目录 JSON 及 schema v27 索引全部成功后才于同一事务写 completion、来源 alias 并完成 episode claim，qB cleanup 独立在后；RSS winner 在 Bangumi 页面和 Torrent 网络访问前以 SQLite IMMEDIATE 事务复查同 `mikanid+来源EP` alias，并在 staging 前再次事务复查以关闭并发窗口。命中即返回 `already_completed`，删除业务完成记录后可重新进入。
- [x] 移植 `link`/`link_delete`/`move`/`wait_move`：四种策略均使用不可变路由快照和持久化逐文件操作；link 保留源文件，link_delete 在目标校验及业务完成后删除源文件，move 立即暂停并移动，wait_move 等做种完成后再暂停移动；失败可恢复且 qB 清理固定 `deleteFiles=false`。
- [>] `move` 安全编排：下载完成后暂停、持久化逐文件执行、TMDB规范路径、同卷原子移动/跨卷copy+SHA-256、冲突保全、崩溃恢复、原子 `tvshow.nfo`、目录 JSON/SQLite 索引、完成记录事务及独立 `deleteFiles=false` qB cleanup 已串联并通过 fake-qB+真实临时文件测试；真实 qB/Docker共享路径 E2E 待验收。
- [x] 将媒体整理、做种目标完成、删除下载器任务、删除下载源拆成独立持久化状态：schema v33 固化 `seeding_target_minutes`、单调 `seeding_elapsed_seconds`、waiting/seeding/completed 与完成时间，`0/-1/正数` 语义独立于 qB 瞬时 state；媒体操作、qB cleanup 与四类删除均按独立持久化状态、租约和失败重试推进，qB 删除固定 `deleteFiles=false`，源/媒体文件分别受捕获根目录约束。
- [x] 处理多文件、跨盘、目标冲突和部分失败：逐文件 operation 按 Torrent 相对路径/文件 ID 稳定执行；同卷优先原子 move，跨盘进入 task-owned partial + 容量/SHA-256 校验 + 原子提交；不同内容的既有目标保留源/目标并返回 `target_conflict`；前序文件已完成而后序文件失败时不写业务 completion，解除冲突后仅续做 pending operation，最终一次性完成全部 Episode 记录和独立下载器 cleanup。
- [>] 多文件 Torrent 逐文件去重：qBittorrent 暂停添加、metadata/claim 完成后逐项核对 index/path/size、重复与 ignored 文件 priority=0、wanted 文件 priority=1 后才恢复；全重复任务保持停止并以 `deleteFiles=false` 移除。fake/SQLite并发、恢复、失败以及主视频+绑定字幕 priority→落盘→单 completion 闭环已通过；真实 qB/container E2E 待实现。
- [x] 实现字幕识别与唯一绑定：同目录同 stem 优先、语言/default/forced/SDH 后缀原样保留、不同 stem 按来源 EP 唯一匹配、`.idx/.sub` 分别绑定并保留扩展；匹配后只复用视频的已验证 TMDB EP/claim/priority，未匹配或歧义进入已确认季度 `Other`，整理不产生重复完成记录。
- [x] 串联媒体目录 DB 与 NFO：NFO 与三层目录侧车都位于业务完成记录之前；侧车损坏、越界或索引失败会保持可重试且不写完成记录。
- [x] 任一季度匹配策略成功后，固定使用 TMDB `zh-CN` 名称（缺失时用 TMDB 原名）、Season Number 和 Episode Number 生成 `<TmdbName>/Sxx/Eyyy.ext`；字幕生成 `Eyyy.<保留后缀>.<字幕扩展>`，Other 保留安全清洗后的原文件名，均已串联持久化 move worker。
- [x] 确定性季度结果先执行同号 EP 快速校验；失败且统一 AI 开启、任务从未尝试 AI 时进行一次任务级映射，返回的 TMDB ID/Season 必须与已确认值相同，结果逐集由 TMDB 验证。
- [x] 保留来源名称和来源集号用于审计、去重诊断及 UI 展示：逐文件原始相对路径、来源 EP、本地文件候选与最终 TMDB 身份分别持久化并在任务详情并列显示；完成时写来源 alias，RSS 批次保存早期命中证据。AI 结果必须逐级通过 TMDB API 验证，未验证值不得参与路径、数据库键或 NFO。
- [>] 多文件任务逐集验证 TMDB Episode：已实现独立租约 worker、官方 Episode 身份验证、规范 Episode 持久化、人工/可信 offset、网络失败保持 pending、季度已知时 `Other` 原因，以及跨任务完成/活动 claim 的逐 EP 重复门禁；paused fake qB 的逐文件 priority/恢复、实际临时文件落盘、字幕语言后缀及单一 completion 已串联，真实 qB/container E2E 仍待实现。
- [x] 增加 `tmdb_fail_use_bangumi` 业务兜底开关，默认 `false`；关闭时 TMDB 完全失败沿用失败流程，不继续下载/刮削且不生成 NFO。
- [x] 开关开启后，仅在权威 TMDB 成功访问且最终为确定性 Series 无匹配、已有有效 Bangumi Subject ID 时继续；季度固定本地 `S01`，不依赖 P2/P1，不输出有效 TMDB ID，动画根目录 `tvshow.nfo` 写 `<tmdbid>0</tmdbid>` 和对应 `<bangumiid>`。
- [x] 验证已取得 TMDB Series、仅季度匹配失败时仍走原季度 fallback，不误入 Bangumi 完全失败兜底；网络/认证/配置/协议/输入失败均禁止兜底。
- [ ] 通过状态机、文件策略和合法小文件 E2E。

## P9 — 调度、Web API 与 Web 页面

- [x] 实现六字段 Cron 调度、StartRun 和 NextTime：支持秒级六字段、`?`、list/range/step、英文月份/星期与标准 descriptor，DOM/DOW 沿用 Cron OR 语义；时区/DST、启动立即执行、三次重试、并发任务、热增删唤醒、稳定快照和取消退出均由可控时钟测试覆盖，宿主仅在后台 worker 开启时运行 coordinator。
- [x] 实现 Bangumi/数据库/feed/plugin tasks：旧 Bangumi cache 下载由版本化 AnimeGoNetData 检查/下载/导入调度替代；目录数据库刷新、数据更新和逐 SourceProfile Mikan RSS feed 均由编译期内置 schedule plugin 执行。RSS 调度只携带来源 ID/revision，运行时从 SQLite 取得只写 URL；失败重试、审计、热增删、重启中断恢复和后台禁用门禁已覆盖，无 Python task 或反射发现。
- [>] 实现优雅退出和取消传播：宿主固定 5 秒停止期限；所有后台 Worker、调度等待/重试、qBittorrent 活动调用、配置热应用和 RSS winner 租约清理均响应宿主停止；WebSocket 长连接在 `ApplicationStopping` 时主动关闭。JIT 宿主停止与 win-x64 NativeAOT 停止后句柄清理已验证；Linux/macOS NativeAOT `SIGTERM` 已加入五 RID smoke，待 CI 实机结果后完成。
- [x] 移植上游 HTTP API：计划原称“10 个”，权威 OpenAPI 实际列出 11 个 REST operation + 1 个 WebSocket operation；契约测试逐项比对基线，12/12 均有 AnimeGoNet 路由。最后缺失的 `/api/config` 已实现 `all/default/comment/raw` GET、`all/raw` PUT、legacy envelope/参数错误/Access-Key、写前强类型校验、可选不可覆盖备份和同目录原子替换；更新仅在重启后应用。
- [x] 新增 `/api/v1/ingest` 通用批量 Torrent/URL 导入 API，沿用 `source + data[].torrent + data[].info`；旧 `/api/download/manager` 已转换到同一 command，`/api/rss` 与现代 `/api/v1/rss/ingest` 均已接入来源规则、统一 staging 与后台 qB dispatch。
- [>] 将 passkey Torrent URL 和 `.torrent` announce 视为 secret：profile host白名单及不可变路由快照、逐跳redirect/DNS校验、校验IP固定连接、限时限量、严格Bencode/info-hash、请求期受限 staging、崩溃过期清理、qB确认接收后删除均已实现；AI负向门禁待串联。
- [x] 新增下载器实例和 SourceProfile 的版本化 CRUD、连接测试、路由预览及引用保护 API：SourceProfile CRUD/无副作用预览、下载器脱敏投影/连接测试、data_path 私有覆盖文件、凭据只写 create/update/remove、全局 revision、重启应用和引用保护均已完成。
- [x] 移植 access-key、响应 envelope、参数错误：直接 key、旧 SHA-256 hash、ping/sha256、RSS/manager/plugin/config/Bolt、WebSocket 均接入统一鉴权；legacy HTTP 保持 HTTP 200 + `code=200/300`，配置畸形 JSON/Base64/YAML/强类型值在替换前失败。
- [x] 移植 WebSocket 日志 pause/resume：保留 `/websocket/log`、旧 `type=log/count` 帧和三种控制命令；鉴权、逐连接暂停、1000 条有界缓存、慢消费者有界队列、脱敏及取消均已验证。
- [x] 移植轻量滚动文件日志：固定写入 `data_path/logs/animego.log`，仅 Information 以上，2 MiB、14 份备份、14 天保留；与 WebSocket 共用脱敏格式，宿主停止后由 DI 唯一释放句柄。
- [>] 兼容 `DeQxJ00/AnimeGoHelper`：`/ping`、`/api/rss`、`/api/download/manager`、`/api/plugin/config` 和 `Access-Key` 已覆盖；Kestrel 契约已验证配置上传立即影响 RSS、快速下载仍跳过过滤。原油猴脚本浏览器 E2E 待验收。
- [x] 将旧插件名 `filter/mikan_tool.py` 及等价别名映射到 SQLite 过滤规则；Base64 JSON 可无损同构往返、并发 legacy 上传完整提交，不查找、不创建且不执行 Python 文件。
- [x] 实现 Mikan 过滤 Web UI：RSS 过滤总开关、五档规则 CRUD/启停/排序、关键词 JSON 数组编辑、服务端样例预览及逐档决策详情、旧 JSON 导入导出、revision 冲突和快照回滚均已接入；页面明确警告多 F0“最后结果生效”、空关键词匹配全部标题和区分大小写语义。
- [x] 实现 Mikan RSS 优选 Web UI：原生 TypeScript 页面支持白/黑名单及有序组/数组的增删、启停、上下移动、values 编辑、SourceProfile 独立开关、expected-revision 保存、真实服务端批次 preview（名单结果、winner、实际执行组），以及 schema v25 历史快照选择与 revision 安全回滚；首版使用可键盘操作的上下移动，不依赖拖拽。
- [x] 移植静态页并生成 OpenAPI：静态 TypeScript/HTML/CSS 页面由 Kestrel/AOT smoke 覆盖；官方 .NET 10 AOT-safe 生成器在 `/openapi/v1.json` 输出完整当前 Minimal API 契约，具有确定性 operationId/标签、现代及旧 Access-Key 安全说明、无运行端口/路径/密钥泄露，并由原生进程 smoke 验证。
- [>] 通过 API/WS 契约差分测试：生成文档与所有当前非排除 HTTP endpoint 的 method/path 精确相等，上游 OpenAPI 12 个 operation 也逐项自动对比；legacy config/rss/plugin/manager/Bolt、access-key 与 WebSocket 控制帧已有 Kestrel 契约，全响应字段 golden 与原油猴浏览器 E2E 仍待完成。
- [ ] 创建 Web 前端工程、类型化 API client 和前端测试基线。
- [x] 实现仪表盘和下载器/任务状态：下载状态卡片、进度、连接且非 stale 的跨实例速度汇总、活动/暂停/失败/等待整理/完成/离线指标、qB 状态与 AnimeGoNet 业务阶段独立筛选，以及 `download_preparing`/重复跳过、元数据 Series/Season/Episode 阶段、失败原因、策略尝试时间线、文件归类计数、准备/整理失败详情和显式重试入口均已接入。
- [>] 实现两层下载进度投影：qB规范状态/百分比/容量/速度/ETA/Seeds/Peers与AnimeGoNet业务状态已分离，qB 100%映射为 `downloaded` 而非最终业务完成；下载 API/WebUI 另显示持久化做种目标、状态、累计时间、百分比和完成时间。解析/移动/重命名/字幕/NFO的更细粒度进度仍待串联。
- [x] 实现按实例隔离的qB同步器和`DownloaderTaskSnapshot`：活动约2秒、空闲约10秒、单实例单在途、实例失败隔离、离线保留stale快照、重启按实例+hash恢复，以及首错2秒、连续半开失败指数增长并封顶120秒的熔断已完成；显式连接测试可安全绕过等待窗并在成功后复位。
- [x] 实现下载列表/详情/文件级 priority 与 wanted 进度、筛选搜索分页和状态时间线；详情合并 SQLite 文件分配与 qB 实时文件快照，qB 离线时保留持久化信息并返回安全失败码，不暴露绝对路径或凭据。
- [x] 实现暂停、恢复和 AnimeGoNet 业务重试；写操作校验 job revision，成功/失败均写入 schema v24 审计事件，任务卡片的删除操作仍只进入四类删除中心执行预览/确认。首版不复刻 Tracker/Peer 明细、piece 图、限速、强制校验/汇报和 qB 全局设置。
- [x] 实现多下载器页面：原生 TypeScript 展示命名实例、脱敏端点、路径、凭据状态、连接/失败、引用与任务数量；连接测试显示 qB 客户端版本、默认保存路径、延迟和任务数，路径探测显示 download/save 路径可见性与硬链接能力；支持 revision 安全的凭据只写新建/更新/移除与重启提示。
- [>] 实现输入源页面：原生 TypeScript 已接入 SourceProfile CRUD、内置及已发现 external source adapter 下拉（未启用/缺包明确禁用）、完整启用下载器实例下拉、Host 白名单、规则开关、文件策略、category、静态/动态 tags、做种分钟、Mikan Cookie 只写设置、revision 冲突、move 提示，以及复用真实 adapter 且无副作用的路由预览；重复命中通知待实现。
- [x] 实现手动 RSS/下载提交与操作结果：原生 TypeScript 页面按已启用 SourceProfile 提交单个 Torrent，Mikan RSS 可选择独立来源 revision；带 passkey 的 URL 使用密码输入、请求发出后立即清空且不进本地存储，结果只显示任务、规则、下载器和不可逆指纹。
- [x] 实现配置表单、服务端校验、脱敏 diff 和保存备份：Web 不展示或改写含部署 secret/注释的原始 YAML，而是先 `POST /api/v1/config/preview` 验证 revision 并展示字段级生效方式，明确确认后才写 `application.private.json`；覆盖/恢复前将旧 revision 原子保存到 `data_path/backups`。
- [x] 配置页显式展示四个确定性季度失败开关及一个统一 AI 元数据开关，说明优先级/触发阶段和 Backtrace/AI 前置条件；AI/TMDB 密钥只写不回显，保存前 diff 只显示 `继承/已配置/已清除` 状态，环境锁、即时生效和需重启字段均可见。
- [x] 动画任务详情同时展示逐文件来源名称/来源 EP/本地候选和最终 TMDB 名称/Season/Episode，以及 AI 调用状态、TMDB 验证可信依据、最终失败原因和策略尝试时间线；不采信或展示模型自报的数字置信度，避免把未验证自评分表示成权威结果。
- [x] 实现作品库季度列表：schema v23 已持久化 TMDB Series/Season 名称、首播日期、总集数与 Series/Season poster 路径；P4/P3 联合匹配会再请求官方 Season endpoint，并在正常解析和待补全恢复事务中保存完整普通 Episode snapshot。列表/详情 API、Cover 安全代理/缓存/占位图和静态 TypeScript 页面均已完成；页面显示 TMDB 规范进度、取得策略、验证状态、一致性警告和可筛选 EP 网格，删除完成记录会立即恢复未下载。
- [x] 实现作品库服务端分页排序和前端升/降序：服务端与页面支持最后业务更新时间（默认降序）、TMDB 名称、TMDB Season 开播日期、本地加入日期四种升/降序，空开播日期始终置后并使用 TMDB ID/Season 稳定翻页；排序、方向、页大小、EP 筛选和当前详情保存在浏览器本地。
- [x] 实现 Cover 后端代理、本地缓存和占位图，不向浏览器暴露 TMDB API key；列表查询使用批量投影，避免按作品/EP产生 N+1 查询。`poster_url` 只指向同源 `/api/v1/library/covers/{tmdbSeriesId}/{seasonNumber}`；Season/Series 回退、5 MiB 流式上限、图片魔数校验、并发合并、磁盘缓存与失败占位均有测试。
- [x] 将 TMDB 未解析及 `tmdbid=0` 兜底条目放入“待补全 TMDB”，不生成 TMDB EP 网格、季度封面或完成比例；逐项恢复并验证真实 TMDB 映射后，再事务并入标准作品库。
- [x] 待补全 TMDB 详情展示兜底完成记录、实际去重身份/作用域和跨来源重复风险，但不把它表示为 TMDB EP 下载状态；API 不暴露内部 scope key、媒体路径或伪造 TMDB 身份。
- [x] 实现 TMDB 作品库季度 CRUD：创建只接受 `TMDB Series ID + Season` 并在线验证后保存 Series/Season/完整 EP snapshot；更新是带 `resource_revision` 的 TMDB 权威刷新，不提供手工改名或伪造季度；删除只允许无任务、完成记录、claim、Mikan 人工规则、fallback 记录和待写 NFO 引用的本地投影，绝不顺带删除下载器任务、下载源文件或媒体文件，有引用时引导进入四类删除流程。
- [x] 增加 Mikan 作品规则 CRUD：原生 TypeScript 页面按 `mikanid` 读取并以 expected revision 创建/更新/禁用/清除 Bgm/TMDB Series/Season/EP Offset；影响预览权威区分未来自动应用、可显式重试的失败任务、活动中保护、已解析保护和已整理保护。显式重新匹配只重置未持有运行租约的 `metadata_failed` 任务，不改写已解析/已整理任务、完成记录或媒体文件；可选样例来源 EP 继续执行保存前 TMDB Series→Season→目标 Episode 在线验证。
- [x] 作品详情展示 Series/Season/Episode 的 TMDB 获取阶段、验证状态、人工偏移和最后解析时间：季度详情现在汇总当前 Mikan 人工 EP offset、最多 50 个关联任务和最多 200 条跨 Run 逐次验证记录；保留任务/Run/策略优先级、阶段、结果、稳定错误码、可重试性、耗时与脱敏原因，并明确标记截断，不把当前规则冒充历史实际值。
- [x] 实现四类删除命令及组合删除计划：schema v12 已完成指纹预览、逐项冻结、租约恢复、稳定失败码和部分失败重试；执行顺序为 qB 任务（永不带文件）→源文件→媒体文件→业务记录/claim，文件只允许捕获根目录内精确普通文件且不递归删目录；Minimal API 和 WebUI 四类独立勾选、目标预览、明确确认及 execution 状态查询已接入。
- [x] Web UI 支持按失败阶段、错误码、可重试性和处理状态筛选，并提供分页与最后更新/标题/状态/失败分类排序；提供安全的“重新匹配”，明确区分“可安全重试（需显式）”、需配置修复、需人工处理、处理中、已解析、已跳过和已兜底。当前尚无自动重试编排，因此不把 `retryable=true` 误标为“已经进入自动重试队列”。
- [x] 对当前应用配置环境变量覆盖字段显示最终有效值、环境变量来源和只读锁定状态；API 拒绝改写被锁字段和凭据，保存其他字段时保留锁字段原有底层覆盖/继承语义，避免环境变量移除后遗留伪覆盖。
- [x] 实现插件分类、启停、args/vars 和校验视图：外部包分类/版本/RID/能力、逐包校验错误、运行/退避/自动禁用/reset、revision 持久启停、args JSON 与 schema vars 表单均已接入响应式页面；writeOnly 值仅显示配置状态，留空保留且需显式勾选清除，API/状态/错误不返回原值。
- [ ] 实现缓存/数据库浏览和安全删除。
- [x] 实现实时日志过滤、暂停、恢复和断线重连：静态 TypeScript 页面按级别筛选，安全 DOM 渲染并保留最新 500 条；浏览器隔离验收已覆盖暂停不增长、恢复补发、过滤、手动重连和零 console error。
- [ ] 完成响应式布局、空/错/加载状态和基本可访问性。
- [>] TypeScript 7 strict 类型检查和确定性编译已接入独立 CI job，提交产物必须与源码一致；DOM 单元测试和 Playwright UI E2E 待实现。
- [ ] 用 Tampermonkey + Mikan fixture 页验证“单集”“全集”“上传/获取过滤配置”。

## P10 — 组合与发布

- [>] 完成 Host DI 和 CLI 行为：宿主组合、优雅退出、`--config`、三路径和规范嵌套配置的命令行覆盖已完成；上游其余 CLI parity 待审计。
- [>] 完成 Docker NativeAOT 镜像（双架构 Dockerfile、Buildx CI 和容器 smoke 已建立；本机无 Docker CLI，待 GitHub runner 实跑）。
- [>] 添加非 root、PUID/PGID、healthcheck、SIGTERM、只读根文件系统验证：Linux/macOS NativeAOT smoke 已在成功验收后发送 `SIGTERM`，要求 7 秒内零退出并可删除隔离数据目录；非 root、PUID/PGID 与只读根仍待完成。
- [>] 添加连接外部下载器和内置 Compose 下载器的部署示例：规范 YAML、环境变量、本机 TestSpace 和官方 `/download` Compose 路径契约已记录；远程外部 qB 的容器路径映射完整示例仍待补充。
- [x] 固定官方 Docker：`data_path=/data`、`download_path=/download/incomplete`、`save_path=/download/anime`。
- [x] 提供写有上述绝对路径的 Docker 容器配置；Compose 卷与配置逐项一致，不依赖隐藏路径修正。
- [>] 官方 Compose 将 AnimeGoNet 与下载器的同一宿主目录统一挂载到 `/download`；配置和 smoke 断言已建立，真实容器验证待 CI。
- [>] 验证外部下载器 `client.download_path` 路径转换与错误诊断：主程序已提供 qB 默认保存路径读取和显式路径/硬链接能力探测，临时目录成功/缺失 fixture 已通过；真实外部容器的跨容器映射验收待 Compose 集成环境。
- [ ] 构建并实机验证 `linux/amd64`，按确认结果验证 `linux/arm64`。
- [>] 验证外部 C# 插件目录挂载、平台/RID 校验、非 root 启动和禁用回退：`data/plugins` 包目录与 `data/plugin-data` 写目录已分离；平台/RID、入口执行位、Unix group/world write、链接逃逸、逐包错误隔离、真实本机子进程/环境隔离、自动禁用与显式 reset 已有测试。Docker 只读挂载和真实 Linux 非 root 子进程待验收。
- [ ] 发布并实机验证 `win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`、`osx-arm64` AOT artifacts。
- [ ] 生成 checksums、SBOM、第三方许可证。
- [>] 完成新安装、旧配置升级、旧数据迁移演练：JIT/NativeAOT 新安装首次 YAML、目录和 SQLite 已通过隔离 smoke；最新 win-x64 原生二进制也完成 1.6.1 原字节备份→规范 1.7.1 重写→正常启动，五 RID CI 已加入相同双 smoke。旧 Bolt/目录业务数据迁移演练仍待实现。
- [ ] 完成全链路 JIT/AOT/Docker E2E。
- [ ] 用发布镜像完成 Web UI Playwright E2E。
- [>] 编写用户迁移、部署、插件和运维文档：部署 YAML、Docker 路径和本机 qB 隔离验收文档已完成；旧配置迁移、外部插件和完整运维手册待完成。
- [ ] 标记第一个可用预发布版本。

## P11 — AnimeGoNetData

- [x] 确认独立数据仓库名称为 `AnimeGoNetData`；托管地址由该独立任务配置。
- [x] 定义 `manifest.json`、subjects/episodes JSONL schema 和版本策略：v1 字段、兼容规则、不可变发布、确定性 gzip/JSONL、哈希/大小/数量/ID 范围与引用语义见 `docs/DATA_MANIFEST_V1.md`；主程序已有 NativeAOT-safe 严格 manifest 解析器。
- [x] 定义并验证 `setting.data_update` 配置 schema、默认值、环境变量覆盖和热重载行为：主程序提供默认关闭、04:00 六字段 Cron、可空 manifest URL、自动下载/导入、保留 2 版和 300 秒超时的强类型绑定/校验；Web/API 使用 `data_path/config/application.private.json` 安全私有覆盖而不改写部署 YAML，采用 revision 原子写入。七个字段均支持环境变量只读锁；保存或恢复后共享运行快照与 Cron 任务立即热重排，其他配置字段仍明确要求重启。
- [ ] 实现 Bangumi Archive 下载、校验、清洗、分片和 gzip。
- [ ] 建立每日检查 + 手动触发 GitHub Action。
- [ ] 建立数据唯一性、引用完整性、数量下限和确定性测试。
- [ ] 发布不可变 Release assets、SHA-256 和 latest manifest。
- [x] AnimeGoNet 实现检查更新、流式下载、校验和 staging SQLite 导入：schema v28 已加入版本、运行审计、独立 staging 与版本化 Bangumi Archive 表；本地包导入会先验压缩文件大小/SHA-256，再以有界单行缓冲流式解 gzip/JSONL，校验字段、顺序、分片范围、计数、唯一 ID 与 Subject 引用。schema v29 记录检查/下载/导入阶段与已验证下载目录；HTTP 使用 headers-first、有界 manifest 和 64 KiB 流式 asset 下载，逐资产验证长度/SHA-256 后才原子移动到托管包目录。
- [x] 实现事务切换、上版保留、失败回滚和离线手工导入：存储核心原子切换 active/previous、保留 2–10 版、支持显式回滚和同版本不可变/幂等；离线 ZIP API/WebUI 只接受根目录 `manifest.json + 声明资产`，流式落盘后逐条验证路径、长度、SHA-256、gzip/JSONL、数量和引用，再进入相同事务导入。全部失败路径保持旧 active 并清理 partial。
- [x] Web UI 增加数据版本、更新时间、检查/更新/回滚状态：静态 TypeScript 页面已接入状态刷新、手动检查、仅下载、下载并导入、已下载包延后导入和上一版回滚；显示调度策略、传输字节进度、稳定失败码、active/previous、已安装版本与本地下载包，未配置 manifest 或无可回滚版本时禁用对应动作。
- [x] 分别验证关闭调度、仅检查、自动下载待确认、自动导入和失败保留旧版：调度插件按 `auto_download`/`auto_import` 映射三种动作；单元/SQLite/API/浏览器测试覆盖仅检查、只下载、延后手动导入、自动导入、HTTP/坏资产失败保持旧 active、配置即时读取、Cron 增改删/失败回滚、环境锁、Web 表单和静态生产资源。
