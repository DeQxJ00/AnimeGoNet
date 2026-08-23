# AnimeGoNet TODO

状态约定：`[ ]` 未开始，`[>]` 进行中，`[x]` 已完成，`[~]` 功能/门禁已生成但尚未完成全部验证（不是实现完成或运行成功），`[!]` 阻塞。只有验证矩阵对应项通过后才能勾选完成并提交；`[~]` 必须逐项写明未验证范围。

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
- [x] 确认 Mikan `pubDate` 仅作为统一 AI 的可选参数；可选 Bangumi 最近 EP 提示不能直接决定 TMDB 集号，Torrent 发布日期不设置通过/拒绝窗口。季度首播日期与普通 EP 首播日期的主匹配均允许 ±1 日；EP 主匹配失败时，仅实际单文件任务可用文件名 EP 对 Bangumi 普通 EP 与最近 TMDB EP 做最多 7 日的一致性补判，超过 7 日、编号不一致、证据缺失或多文件失败进入统一 AI，AI 关闭时进入已确认季度 Other。
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
- [x] 原多源示例路由已完成通用骨架：Mikan（bgmid必填）→ `bt` qBittorrent；U2/TTG → `pt` 仅保留 adapter/API/路由回归夹具，不作为首版支持承诺。
- [x] 确认项目只支持 qBittorrent 下载器及其多命名实例；取消 Transmission 适配计划，旧类型仅作不支持诊断。
- [x] U2/TTG 原外部油猴/扩展/API 提交设计已记录，但项目所有者现已确认首版暂缓；主程序不新增站点登录、抓取、账号/Cookie 或默认来源配置。
- [x] 确认沿用并强类型化 Mikan `source + data[].torrent + data[].info` 批量格式，所有来源统一调用 `/api/v1/ingest`；旧 API 转同一 command。
- [x] 确认跨输入源按 `(TMDB Series, Season, Episode)` 全局去重；只跳过已完成 EP，同剧集和多文件 Torrent 中的其他 EP 不受影响。
- [x] 确认 Mikan RSS 同集优选的黑白名单是前置资格过滤，单候选也执行；只有资格过滤后同一 `mikanid+来源EP` 仍有多个候选时才运行可配置优先级组。
- [x] 确认默认 Mikan SourceProfile 使用 `move`：下载完成后移动到媒体库、不继续做种；Web可改其他策略且只影响新任务。
- [x] 确认 U2/TTG 首版暂缓：不选择默认文件策略、不生成默认 SourceProfile、不做站点业务验收；现有通用 adapter/API/路由骨架保留，未来恢复范围时重新确认策略。
- [x] Linux Go 容器基线 job 已验证：Ubuntu 24.04 x86_64 CT 使用官方 `golang:1.22.10-bookworm` 与上游 `c7475df`，以 `CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go test -p 1 -count=1 -json ./...` 串行执行；结果 exit 0、3109 条事件、100 个上游 skip，`events.jsonl`、stderr、稳定 summary 和 SHA-256 均已取回校验。Docker Hub 下载使用所有者提供的代理，官方摘要和清理已记录。
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

- [x] 移植所有领域模型、枚举和错误类型：固定 `develop@c7475df` 的机器清单逐文件/逐导出类型覆盖 `internal/models`、`internal/constant`、`internal/exceptions`、`pkg/exceptions`，每项标记保留/强类型替代/NativeAOT 例外并绑定真实目标文件；契约测试同时校验上游 HEAD、目录无漏项和目标存在。旧 `ExistError`/`NotFoundError`/`ParseFailedError` 由显式业务结果及可跨 InnerException 识别的 `IStableError`/`StableErrorSemantic` 替代，结构解析异常已统一暴露稳定 `ParseFailed` 语义。
- [x] 建立首阶段强类型配置/目录模型与校验：Docker 三路径、命名 qBittorrent、Mikan `move` 默认、AI 600 秒和高风险 fallback 默认关闭。
- [x] 固化 API JSON source-generation context：所有公开、闭合 API DTO 以及 `ApiEndpoints` 方法签名中出现的闭合泛型 envelope 都由测试穷尽反射并要求 `ApiJsonContext` 返回生成元数据；新增 DTO/endpoint 漏登记会在 JIT 测试中失败，win-x64 NativeAOT 发布与 API smoke 继续作为运行门禁。
- [x] 完成上游 hash、name、path、时间等纯函数的可观察行为映射：SHA-256 统一由 `StableHash` 生成 UTF-8 小写 hex 并用于 access-key、统一导入 URL 指纹和 RSS batch/candidate 身份；四步去后缀与 UTF-8 byte 相似度保持上游 parity；动态 tag 的季度/星期、Mikan 时间解析、TMDB 日期差、Unix 秒均使用强类型时间；路径/文件名改由 `PathBoundary`、`MediaPathPlanner` 和安全文件执行器实现跨平台边界。反射 map 转换、Python/资源 MD5、panic recovery 等仅服务旧实现的 helper 明确由 source-generated DTO、编译期资源和 HostedService 异常隔离替换，见 `docs/PURE_FUNCTION_PARITY.md`。
- [x] 移植默认 YAML 与注释：首次启动 `CreateNew` 原子生成 1.7.1、无 BOM UTF-8、Unix `0600`；涵盖路径、命名 qB、来源绑定、TMDB/Bangumi/AI、四档失败链、Torrent、Cron 和数据更新，secret 为空且高风险开关默认关闭。
- [x] 移植环境变量覆盖：上游全部 `ANIMEGO_*`、规范嵌套键、现有扁平键、旧 qB 键和命令行均按 Provider 实际层级解析，跨别名仍固定命令行→环境→YAML→默认，路径/下载器/来源/统一 AI 旧双键均有冲突层测试；Web 监听安全默认和标准 `--urls`/`ASPNETCORE_URLS` 高优先级有真实 Kestrel 测试；全局代理、SourceProfile 与下载器实例保持各自部署语义。全部可编辑应用字段及来源/下载器字段均在 API/WebUI 投影环境/命令行控制键、拒绝改写，私有 JSON/SQLite 不复制部署凭据或越级覆盖；配置编辑页面按便利优先直接回填当前有效凭据，日志、运行轨迹和错误响应仍统一脱敏。
- [x] 移植配置检查、路径初始化和资源释放：严格 YAML 输入边界、强类型值校验、三路径和下载器子目录边界、首次目录/文件初始化、宿主释放均有测试；固定上游 `configs` 的全部生产文件/导出符号和 `config_test.go` 入口已由机器清单穷尽映射。旧 Mikan feed 的 name/URL/Cron/enable 同时迁入 SourceProfile seed 与 SQLite，异常输入 fail closed 且不回显 URL。
- [x] 移植配置 `1.1.0` → `1.7.1` 升级链与备份：只接受上游明确列出的 13 个版本，12 份固定 `develop@c7475df` 历史 YAML 以 SHA-256 锁定并逐份迁移验证；旧 qB `setting:`/`advanced:` 默认保存同目录原字节 `CreateNew` 版本化备份，再经同目录临时文件原子重写规范 1.7.1；路径/qB/Mikan策略/category/做种/动态 tag 模板/TMDB/代理/失败链/Cron 与 `advanced.source|anidata.mikan.cookie` 已迁移，错误值及上游不存在的范围内版本均在落盘前拒绝，Transmission 保持原文件并 fail closed。
- [x] 新配置加入 Skip/Backtrace/TitleSeason/FirstSeason 四档确定性季度策略和一个任务级 AI 元数据开关，全部默认 `false`；规范 YAML/扁平键/API/WebUI 已使用 `ai_use_metadata_match`，旧双键兼容读取、新安装默认注释及旧 YAML 自动重写新默认值均已完成。WebUI 另已闭合 OpenAI-compatible Base URL、模型、API Key 私密覆盖/清除状态、TMDB MCP 与 Bangumi MCP 地址，且五项均遵守部署字段锁。
- [x] 增加 OpenAI-compatible AI 配置 DTO、扁平环境变量、source-generated JSON 上下文和日志脱敏；本机配置 API/WebUI 与 AI 匹配测试工具直接回填当前 API Key、provider/base/model/代理和工具端点，测试页旧 localStorage 不覆盖主配置连接项。
- [x] 领域模型拆分来源字段与 TMDB 规范字段：统一导入持久保存 source profile/revision、adapter、来源标题、item/work ID、Mikan/group/Bangumi/AniDB/IMDb 和发布日期；解析 Run/逐文件/作品库只把经验证的 `TmdbSeries`/`TmdbSeason`/`TmdbEpisode` 写入规范字段。任务详情新增独立 `source_evidence`，原始 item/work ID 仅投影域隔离 SHA-256 指纹，WebUI 明示“不作为 TMDB 规范字段”；URL、passkey 和原始不透明 ID不返回。
- [x] 增加 `MikanWorkMetadataRule`：`mikanid` 唯一键、`BangumiSubjectId`、`TmdbSeriesId`、`TmdbSeasonNumber`、有符号 `EpisodeOffset`、启用/版本/审计字段；数据层已实现 revision 冲突保护、禁用和清除，API/编排接入在对应阶段继续。
- [x] 将上游 `assets/plugin/filter/Auto_Bangumi/raw_parser.py` 1:1 移植为 NativeAOT 友好的 C# 内置解析器：19 组由 develop 分支 Python 产出的 golden fixture 已逐字段覆盖标题、季度、集号、字幕、发布组、分辨率和来源，并明确保留不识别 E04/EP04 等原始语义。独立 `FileEpisodeCandidateResolver` 才拒绝年份/分辨率占位、歧义和非正片，只对 Mikan SourceProfile 落逐文件候选；AI/TMDB 验证后的本地统一偏移计算及“不一致只禁止学习”已在 Episode worker 完成。
- [x] Mikan 文件名弱数字 EP 候选限制为 `1～9999`：超范围数字按哈希/校验码排除后再判断歧义，显式季度集号及最终 TMDB Episode 仍走各自验证边界；Kokoore `- 07 ... [13335833]` 确定性得到 EP 7，不再因此进入 AI。
- [x] 已建立 NativeAOT-safe Torrent 文件和 Mikan RSS title EP 安全分类层：兼容上游 Go `ParseEp` 的 `[04]`/`[04v2]`/` - 11`/`EP12`/`第12话`，RSS title 另支持不受扩展名截断的最后可靠标记；小数集与 SP/OVA/OAD/PV/NCOP/NCED/Menu/S00E 均不形成普通整数。入库的 Mikan `file_episode_candidate` 由 raw_parser.py 兼容层与独立安全层决定，其他 adapter 固定不写；确认 Season 后仍逐文件经 TMDB Episode API 验证。统一任务详情现通过持久关联展示一个任务来自哪些 RSS batch/entry、规则与 Legacy revision、实际决策/有序组和入口来源 EP，并与逐文件候选并列审计；不返回原始 Mikan/Torrent URL、candidate ID 或其指纹。
- [x] 增加 `MikanOffsetEvidence`/`MikanTrustedOffsetCache` SQLite 模型、事务状态机和默认关闭配置；按 `(mikanid,groupid,来源EP)` 唯一约束累计三个不同正整数 EP，并在冲突/歧义时撤销可信状态；命中后在 AI/TMDB Episode 调用前本地计算并验证目标 EP，API/WebUI 显示 Learning/Trusted/ConflictReset。
- [x] 可信 EP Offset 支持按整个 `mikanid`、整个 `groupid` 或精确 `(mikanid,groupid)` 建黑名单；命中后禁止读取与学习，新增时事务清理已有自动证据/缓存，WebUI支持增删。
- [x] 增加持久化通知中心：Bark详细参数、通用 Webhook、Discord、Slack、Telegram、Server酱、PushPlus；支持事件订阅、测试发送、失败隔离和发送审计 WebUI。
- [x] 增加 Series/Season/Episode 三层 `TmdbResolutionSource` 和解析运行/策略尝试引用：schema v32 在解析完成事务中固化 Series/Season 的 Run+Attempt，并为每个 Episode/字幕文件保存精确 Attempt；API、任务面板和作品库均显示权威来源及证据引用，混合文件明确下钻到逐文件详情。
- [x] 通过全部配置/模型 parity tests：`UPSTREAM_CONFIGURATION_CONTRACTS.psv`/`UPSTREAM_CONFIGURATION_TESTS.psv` 锁定上游配置面，12 份历史 YAML 继续锁定 SHA-256 和保留字段；领域模型、枚举和错误另由 `UPSTREAM_DOMAIN_CONTRACTS.psv` 穷尽校验，目标文件与替代测试必须真实存在。

## P3 — 存储

- [x] 建立 SQLite schema v1、幂等事务迁移器与显式 SQL 完成记录 store；启用 foreign key/WAL/busy timeout。
- [x] 实现 SQLite KV/TTL store：schema v22 `cache_buckets/cache_entries`、原子批量 JSON、绝对 TTL、惰性/全局过期清理与并发写入已验证。
- [x] 实现 bucket/list/get/delete 兼容接口：`bolt`/`bolt_sub` 隔离、旧 envelope、只读 archive 删除保护和 Access-Key 已验证。
- [x] 移植目录 JSON 数据库扫描/索引/写入：按上游结构原子写 `anime.a_json`、`anime.s_json`、`*.e_json`，启动扫描并以 schema v27 建立 SQLite 索引/运行/拒绝审计；每日 6 点六字段 Cron 刷新，API/WebUI 可查看和手动刷新。
- [x] 移植全局 TMDB Episode 去重索引、来源 alias 和完成记录删除：全局完成唯一键、并发 TryAdd、逐文件 EpisodeClaim、已完成/进行中精确跳过及失败释放均已完成；通用 alias repository 支持规范化写入/来源 EP 查询，正常整理在 completion 同一事务写 alias，删除完成记录级联移除 alias 并释放对应 completed claim。
- [x] 实现 `tmdbid=0` 的 `FallbackEpisodeClaim`、`FallbackCompletionRecord` 和分层唯一键：schema/约束、事务 claim/release/complete、同作用域早停、同任务多文件共享 claim，以及失败释放后重试均已接入。
- [x] TMDB 恢复后事务合并 fallback 完成记录和 alias；多个记录收敛到同一 TMDB Episode 时标记 `DuplicateAfterResolution`，不重复下载、不自动删除文件；恢复前逐级在线验证 TMDB Series/Season/Episode。
- [x] 移植 `tvshow.nfo` 生成和更新：整理时原子生成，TMDB fallback 恢复后以持久化重写作业更新，均限制在捕获的 save root 内。
- [x] 实现旧 Go 已知 bucket → schema-v1 JSON 导出及 .NET 幂等导入：固定 `bolt`/`bolt_sub` 六个上游 bucket、原始 JSON key/value 与绝对 TTL；64 MiB/50000 entry 有界验证、未知/损坏整包拒绝、过期跳过、IMMEDIATE 单事务、schema v39 内容指纹审计和重复导入不覆盖新数据均有测试。只读 Go 导出器与独立 .NET CLI 均拒绝危险隐式路径/覆盖；五 RID NativeAOT artifact 附带导入器，操作和回滚见 `docs/LEGACY_DATA_MIGRATION.md`。
- [x] 通过存储故障恢复、并发和迁移测试：8 个独立连接并发首次启动只记录一次 schema v38；单个 migration 的 DDL 与版本记录同事务回滚、修复后可续跑；历史缺口/改名及高于应用的数据库版本 fail closed；各 Store 的唯一约束、租约恢复、原子导入、删除重试和重开/TTL 并发均有自动测试。范围与非声明项见 `docs/STORAGE_RELIABILITY.md`。

## P4 — HTTP、Feed、Torrent

- [x] 移植超时、重试、Host redirect、Cookie/API key：Mikan、TMDB API、TMDB 图片和 Bangumi API 地址均可由 YAML/API/WebUI 修改，Mikan 反代改写保留私密 path/query 且只信任明确配置 host 的私网解析。只重试连接/超时/429/5xx，每次重建请求，404/认证/协议失败不重试，调用方取消立即终止。TMDB API key/Bearer、Bangumi User-Agent 与 Mikan SourceProfile 级 `.AspNetCore.Identity.Application` Cookie 均已覆盖；Cookie 仅发往原始 Host，跨 Host redirect 必定剥离，API/WebUI 只显示配置状态、不回显值。
- [x] 实现唯一的按域名选择全局代理：`outbound_proxy.url + hosts` 可由 YAML/API/WebUI 配置，支持小写精确域名和 `*.example.com`（不匹配 apex）；只对命中域名使用无凭据 HTTP(S)/SOCKS5 代理，未命中保持直连。TMDB/Bangumi 地址下不再有独立代理；按所有者确认，未正式运行前直接移除旧 `tmdb_proxy_url`、`bangumi_proxy_url`、`ANIMEGO_PROXY_URL`，不保留迁移包袱。已覆盖 Mikan RSS/Torrent、TMDB/Bangumi、封面、AI/MCP 和数据更新；qBittorrent 与固定 AniDB 参考请求明确直连。Torrent 每跳仍先执行 SourceProfile host allowlist、DNS 地址和 redirect/HTTPS downgrade 门禁；走代理时由显式代理解析/连接目标，不声称保持直连模式的 DNS 连接钉死。
- [x] 移植 RSS 文件/URL/raw parse：已实现 5 MiB 上限、禁用 DTD/外部实体、首个 enclosure、无 enclosure 跳过、非法 length 归零、Mikan `pubDate` 日期兼容和稳定错误码；URL/文件读取边界可注入测试，尚未暴露为公网抓取 API。
- [x] 实现 Bencode/torrent/magnet/info-hash：严格 v1 Bencode、原始 info 字节 SHA-1、单/多文件清单、padding/路径/数量/总量校验已完成；magnet 现按上游支持首个 `urn:btih` 的 40 位 hex/32 位 Base32、首个 dn 和 tracker 计数，并保证返回/异常不保留 URI、tracker 或 passkey。
- [x] 通过本地 fixture HTTP、RSS、torrent parity tests：RSS raw/file/注入式 HTTP、缺字段、损坏 XML、DTD、错误脱敏、mikanid、两条 magnet 及上游固定提交四个真实 `.torrent` 的 info-hash/名称/总量/17 个文件 parity 已通过；真实 loopback socket server 另验证 chunked RSS、原始请求 path/query、Host/User-Agent、禁止自动 redirect、固定已校验 IP 连接与流式响应。生产 SSRF 策略仍拒绝 loopback/private 地址。

## P5 — 数据源

- [x] 移植 Mikan：RSS/页面身份、`mikanid`/`groupid`、作品页 `bgmid` 发现、五档 legacy filter、新黑白名单与有序优选、SourceProfile 级私有身份 Cookie、winner 统一导入均已接入；固定上游 RSS/filter/parser fixture、边界故障和本机统一导入闭环均已有独立验证。
- [x] 从 Mikan RSS/页面 `/Home/Bangumi/{mikanid}` 提取并持久化正整数 `mikanid`：RSS source URL 优先、channel link 回退及 path/query 解析已验证；RSS winner 会安全抓取对应作品页，仅接受 `p.bangumi-info` 内 `bgm.tv`/`bangumi.tv` 的正整数 Subject 链接，把 `bgmid` 与发现状态/失败码写入 schema v26 批次和统一导入任务。成功结果按批次复用；失败不下载且允许下一次显式 RSS 处理重试。
- [x] 移植 Bangumi API：已按上游 `/v0/subjects/{bgmid}`、官方 `/v0/subjects/{bgmid}/subjects` 与分页 `/v0/episodes` 实现 AOT-safe Subject/关系/Episode 客户端、固定 User-Agent、日期/身份/分页校验和稳定网络/协议失败分类；主程序默认先读活动 AnimeGoNet Data Archive 的 SQLite Subject/完整 Episode 快照，schema v2 也读取经过双端引用校验的 Subject 关系，v1/缺失/不完整/零集未知按证据类型回退在线 API，版本激活/回滚无需重启即生效；目标 Episode 身份已命中但 `airdate` 为空时按目标精确触发在线 Episode 列表刷新，无关缺失日期不扩大联网范围；P3 已证明在在线 Bangumi 明确不可用时仍以 v2 关系和 Subject 零网络回溯，再独立完成 TMDB Series/Season 验证。
- [x] 移植 Bangumi Archive 下载/缓存刷新：主程序已支持 manifest/asset 下载、校验、原子版本导入、活动/前一版本切换与回滚，并已把活动版本接入 Bangumi Subject/Episode 读取；独立 AOT DataBuilder 已完成官方 Archive SHA 门禁、动画/正片清洗、确定性分片/gzip/manifest/离线包及生产数量下限，发布到独立数据仓库仍由 P11 的不可变 Release 项单独跟踪。
- [x] 移植 TMDB 搜索、相似度和季度匹配：上游 discover/tv 查询参数、四步去后缀、UTF-8 byte 相似度、0.75 阈值、普通季度过滤、zh-CN DTO 与 Series/Season/Episode 三级身份验证已实现；上游季度 90 天窗口按已确认业务语义有意收窄为季度首播日期 ±1 日。每个 Bangumi `name/name_cn` 的全部唯一搜索词及每轮全部合格 Series 均以完整 `tmdbid+Season` 为成功条件。受控 loopback HTTP 用实际 `TmdbClient` 验证日文轮次穷尽→中文名→同响应第二候选成功；Bangumi Episode 正整数唯一身份、小数/重复/特别篇拒绝、EP ±1 日主匹配、单文件 7 日最近日期与文件名一致性补判、逐文件 TMDB Episode 验证均有 fixture。Search/Series/Season/Episode SQLite 成功缓存、失败不缓存及安全重试也已验收；无 `air_date` 的 Episode 及包含此类 Episode 的完整 Season 不缓存，避免空日期锁定整个 TTL。
- [x] 实现 AnimeGoNet 新增的 `TMDBFailBacktrace` / `tmdb_fail_backtrace`（默认 `false`）：P3 需要 `bgmid`，并按每个 Bangumi 前作的日文名、中文名和开播日期重新联合搜索，可恢复不同 TMDB Series；多层、同层稳定排序、缺日期继续、visited 防环、成功早停和错误后继续低优先级策略均已覆盖。受控 loopback 同时验证 Bangumi 503 与 TMDB 429 重试、二级前作、完整 Series/Season endpoint 和请求次数。
- [x] 实现统一 `ai_use_metadata_match`（默认 `false`）：一个共享解析器按下载任务发送总标题、候选视频相对文件名/字节容量及可空作品级 `bgmid`/`anidbid`/`imdbid`，一次返回整个文件列表的 TMDB Series/Season/Episode 映射；不得以跨站标题不一致否定任务绑定，也不得直接复制来源 EP。
- [x] 季度与 Episode 阶段共用同一 AI 尝试门禁和 `ai_metadata` 审计；确定性季度成功后普通 EP 匹配失败可首次触发，季度阶段成功或失败尝试过 AI 后均禁止 Episode 阶段再次调用；历史 `ai_season`/`ai_episode` 记录仍能阻止重复调用。
- [x] 实现 Mikan 单文件发布日期 Prompt 提示和显式开关：保留完整 `pubDate`，无偏移时按 SourceProfile 时区解析；`published_at` 只在 Mikan AI 输入出现且不设日期拒绝窗口，可选 `bgm_episode_candidate` 失败时安全回通用统一 AI 流程。
- [x] 同步 AI 测试程序：增加可编辑开关和手工 `bgm_episode_candidate`、只读有效门禁；覆盖两种单文件Torrent、实际多文件禁用、无bgmid/日期/候选禁用和优先分支失败回退。
- [x] 使用固定 JSON 请求/响应 DTO 调用 OpenAI-compatible API；输入文件数量和结果完整性先校验：单文件任务对应唯一，忽略模型文件名回显；多文件任务必须逐项原样回显以防乱序串 EP。回显值从不参与落盘，始终以原始 Torrent 文件列表为准；模型候选再由 TMDB Series/Season/Episode API 二次验证。
- [x] 主程序 AI 模型配置增加推理程度 `none / low / medium / high`：WebUI/API/私有覆盖/部署 YAML/命令行/字段锁完整贯通并回填，`none` 不发送 reasoning，其余值进入正式 OpenAI-compatible 请求；不修改 Prompt。
- [x] 放宽 AI 已匹配结果的冗余 `reason`：顶层或文件 `matched=true` 时允许模型附带解释文字并忽略其业务含义，仍强制有效 TMDB ID、Season/Episode、文件身份/数量一致，并继续执行 TMDB Series/Season/Episode 完整验证；`matched=false` 仍必须提供具体原因。此项不修改 Prompt。
- [x] 实现 AOT-safe 本地 Streamable HTTP MCP 客户端和 function-calling 工具循环；BGM/TMDB 工具使用命名空间，覆盖同步 JSON、`tools/call` 202 后按 session GET SSE、request id 校验、工具 schema 缓存、超时、取消、响应上限和失败隔离。AI/MCP 的 DNS、连接、一般网络、鉴权、限流、服务端、HTTP拒绝、SSE、协议、工具 `isError` 与模型未使用必需 TMDB MCP 均有独立稳定错误码，不再伪装成普通匹配失败；真实 MCP 回放已验证。
- [x] 实现可空 `anidbid` → `tmdbtv` 候选查询：URL 固定、模型零参数、响应有界、禁止重定向/代理并将 DNS 连接钉在公网地址；候选仍需 TMDB MCP 与主程序 API 验证。
- [x] 实现可空 `imdbid` 规范化和固定零参数 `lookup_imdb_tmdb_tv`：主程序调用 TMDB MCP external ID/find，程序侧删除 Movie 结果，只返回正整数 TV Series 候选，最终 Series/Season/Episode 逐级验证。
- [x] AI 和确定性匹配均拒绝 Season 0；Series/Season 已确认但 Episode 未匹配的小数集、特别篇、普通 TMDB EP 不存在、AI 未匹配和孤立字幕均持久化 `Other` 及稳定原因，实际保留原名整理到 `<TmdbName>/Sxx/Other/`；不生成 Episode completion/alias/claim 或 `Eyyy.e_json` 伪进度。AI 仅在统一开关开启且任务此前未尝试时调用，季度阶段尝试后不会在 Episode 阶段重复调用。
- [x] 将确定性季度失败策略固定为 Skip=4、Backtrace=3、TitleSeason=2、FirstSeason=1；四级确定性策略已按优先级接入并验证早停/错误降级，独立统一 AI 阶段已接入且 Series/Season/Episode 结果必须经 TMDB 验证。
- [x] 为 P3 建立完整图/故障 fixture：无前传直接穷尽；缺日期仍继续遍历；同层多前传按开播日期降序/ID 升序；关系循环由 visited 终止；回溯到首部仍不匹配会穷尽日文/中文名及每名清理词；TMDB 网络失败保留稳定类型/码且不伪装成无匹配；Bangumi 请求失败向编排层传播；取消立即中断且不产生 fallback 结果。
- [x] 为 AI 禁用/未配置/超时/限流/畸形 JSON/伪造 ID/多候选/文件数量冲突/缓存建立 fake-server 测试：统一开关关闭时零请求/零审计；配置缺失在联网前失败；超时、429 重试与耗尽、认证和外层/模型 JSON 错误使用稳定安全分类；多个 provider `choices` 作为歧义拒绝；不存在的 TMDB Series 经权威二次验证拒绝；单文件 AI 回显文件名即使被改写也不覆盖原始文件身份，多文件乱序继续在 TMDB 访问前拒绝；同 MCP endpoint 的工具 schema 只发现一次但每次会话仍重新初始化。
- [x] 建立发布二进制 AI 元数据闭环：五 RID NativeAOT workflow 使用随机 loopback fixture 和临时 SQLite，由正式后台 worker 执行 AI 两轮→TMDB MCP 工具→TMDB Series/Season/Episode 二次验证，并从公开任务 API 验证 `ai_metadata`、`tmdb_verified` 与权威 S02E07 落库；qB 显式禁用且不读取真实密钥/TestSpace。
- [x] 移植 Mikan → Bangumi → TMDB 编排与 fallback：RSS winner 已按上游作品页关系自动发现并持久化 `bgmid`，携带 `bgmid` 的已下载任务由内置 worker 执行 Bangumi Subject → TMDB Series → 日期季度，并持久化每次策略；Backtrace、统一 AI 和固定 S01 的 Bangumi 完全兜底均已串联。页面缺链接、歧义、非可信域名、网络失败分别使用稳定失败码，失败批次不提前下载。
- [x] 自动编排之前应用 Mikan 作品级人工规则；完整 TMDB Series/Season 覆盖由专用 worker 优先领取并权威验证，EP Offset 已在逐文件 TMDB Episode 验证前应用且无效时阻断静默回退；可信自动 offset 已与字幕绑定、qB 文件 priority/恢复、实际文件整理、单一 completion 和安全 cleanup 串联。命中时主视频与字幕共享本地推导的 TMDB EP，零 AI、零 TMDB Episode 请求，字幕保留语言后缀且不创建第二 completion。
- [x] 人工规则无效时记录人工覆盖策略失败并阻止静默自动覆盖；清除/禁用后可通过 `POST /api/v1/metadata/tasks/{taskId}/retry` 显式重新匹配，事务性恢复自动策略队列且保留历史运行记录，并拒绝活动租约/非失败状态。
- [x] 区分 TMDB 无结果、季度无匹配、瞬时网络错误和认证/配置错误：客户端稳定分类 SemanticNoMatch/Network/RemoteService/Authentication/Configuration/Protocol/InvalidInput 且异常脱敏；连接/逐次超时/429/5xx 可配置重试，404/认证/协议失败不重试，取消立即传播。
- [x] 为完整失败保存 `failure_kind`、`tmdb_access_confirmed`、`bangumi_fallback_eligible/denial_reason`：SQLite Run 持久化、最终门禁、列表/详情 API 与 WebUI 决策说明均已串联；只有权威 `SemanticNoMatch + access_confirmed` 可进入兜底，Network/RemoteService/Authentication/Configuration/Protocol/InvalidInput/Ambiguous 全部经过处理器测试证明拒绝且不创建 `tmdbid=0`。
- [x] 持久化元数据解析运行与策略尝试记录：阶段、策略、优先级、结果、错误码、脱敏原因、可重试性、次数、耗时和时间戳；SQLite 重启后可按任务查询，版本化 API 与任务卡片策略时间线均已接入。
- [x] 为统一 AI 元数据尝试持久化可审计用量：schema v40 在实际承载 AI 调用的单条 attempt 保存 provider 返回模型、累计 prompt/completion/total token、HTTP 请求数与工具调用数；多轮 MCP function-calling 累加且 429 重试计入请求数，任务详情/时间线 API 与 WebUI 可见，API key、Prompt、工具正文和来源 URL 不入用量表。
- [x] 将已验证独立 AI Tester 1:1 内置到主程序：保留原 `UiRunRequest/UiRunResponse` 字段、Responses/Chat 请求格式、最多 8 轮工具调用、stateful→stateless 续轮、用量累计、`request_identity`、结果 JSON 校验、Mikan pubDate 门禁、本地文件 EP 候选/offset、完整 AI 请求审计与工具 Request/Response Content；提供 `/api/v1/ai-test/run-stream` NDJSON、按 `run_id` 停止、可信 Torrent `import_id` 和 Mikan Episode 导入，并兼容原 `/api/run*`/`/api/import-*` 路径。WebUI 只调整为主程序侧栏样式与分区布局，不再简化协议。API Key 从主配置直接回填但不写入浏览器 localStorage，passkey Torrent URL 不返回，Tester 结构校验之后额外显示主程序 TMDB Series/Season/Episode 二次验证；AI 请求审计仍脱敏。46 项 API/协议 parity 测试通过；真实模型/MCP 浏览器验收待本地配置后执行。
- [x] AI 匹配测试工具的完整请求审计改为两层折叠：每轮 AI 请求/工具调用可展开，内部请求 Content、工具请求 Content 与返回 Content 也能分别展开收起；原始完整内容保持不变，长 JSON 默认不铺满页面。TMDB MCP、Mikan/BGM 与 AniDB 的已启用状态统一为绿色，关闭状态保持中性灰色。
- [x] 通过 fixture parity 和受控 live smoke：纯函数/DTO/fake 覆盖标题清理、相似度、全部候选、季度日期、Bangumi Episode 与失败分类；两个随机 loopback Kestrel fixture 分别验证 name/name_cn 多轮搜索和 P3 跨两级回溯重试，不访问真实 TMDB/Bangumi 或测试密钥。

## P6 — 插件与业务流水线

- [x] 实现 `AnimeGo.Plugin.Abstractions` 和 source/feed/parser/filter/rename/schedule 六类强类型 C# 插件契约；契约项目启用 trim/AOT analyzer，稳定 DTO 不引用 Web、SQLite 或下载客户端。
- [x] C# 移植 builtin feed/parser/filter/rename/schedule；`mikan-rss`、`mikan-title`、`mikan-tool`、`anime-library`、`staged-torrent-dispatch` 均由编译期目录注册并接入真实 feed→filter→parse→staging、整理与调度入口，默认运行无 Python 运行时或脚本加载路径。
- [x] 实现内置 C# MikanTool 五级黑白名单规则：纯 C# 引擎、schema v15 规则/快照、legacy `/api/plugin/config`、Episode identity parser、schema v16 逐候选审计，以及 `/api/rss` 的安全页面抓取/批内缓存/Filiter0..4 前置执行均已串联；被拒绝或身份失败的候选不进入新优选与 staging。现代管理 API 与 WebUI 已支持总开关、五档 CRUD/排序/启停、精确 JSON 数组关键词、服务端逐档预览、旧 JSON 导入导出、revision 冲突和快照回滚。
- [x] 默认 Mikan SourceProfile 的 `mikan_rss_filter_enabled` 已默认 `true` 并真实控制 `/api/rss`；关闭时零页面请求、逐项记录 `SkippedByConfiguration`、继续优选/staging且规则不变。来源 CRUD/UI 已完成；RSS 从请求起点显式贯穿同一 SourceProfile revision/双开关/下载器路由快照，并发修改只影响下一次请求。
- [x] 增加独立 `mikan_rss_priority_enabled` 批次优选开关：默认 profile 已启用，schema v13 规则版本、默认初始化、预览 API 和真实 `/api/rss`/现代 RSS 批次均已接入；禁用时真实批次逐项记录 `SkippedByConfiguration`、不执行本功能的黑白名单/有序组且不清空规则，SQLite 审计保留当批开关状态。
- [x] 在“Mikan 手动设置 / 导入任务”增加“执行已保存 RSS”：直接使用所选已启用 Mikan SourceProfile 的服务端 RSS URL 触发正式抓取、过滤、优选和统一导入链，不要求启用自动 Cron；临时 URL 测试入口继续保留，结果共用逐候选批次审计展示且不回显 passkey。
- [x] 一级“连接与配置”改名为“设置与备份”；Mikan RSS 手动区增加“管理来源与 Cookie”直达入口并明确 Cookie 位于“设置与备份 / 输入源”，选择 Mikan 来源后直接回填。TMDB 成功响应缓存的新部署默认值调整为 144 小时，显式旧配置值继续保留。
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
- [x] 移植 feed → filter → parse → download pipeline：有界 feed、安全 URL 获取、legacy `/api/rss`、Filiter0..4、来源 EP、新优选、schema v16 审计/租约、winner→统一 staging、真实 qB 下载、SQLite snapshot、TMDB 已验证边界、move/NFO/sidecar/completion 和安全 cleanup 已串联；Ubuntu CT linux-x64 Docker 双实例统一导入与完整链路已通过。
- [x] 通过上游所有插件/parser/filter fixture，以及外部 C# 插件协议故障注入测试：固定 `develop@c7475df` 的 59 个 plugin/feed/filter/parser/Python fixture 与 Go 测试入口逐文件归类为 ported/replaced/removed/documentation，机器测试锁定精确清单、SHA-256、证据目标和无遗漏；5 个真实 RSS fixture 逐字段/失败码通过，filter fixture 的 13 个输入、4 个 NC-Raws、9 个有效 1080p 及 inline regex 单候选结果由编译期 C# 直接复现。Python 运行时及任意 Python 扩展仍明确移除；外部 C# 协议 fake/真实进程已覆盖成功生命周期、业务错误、严格响应、超限、超时、取消、崩溃、脏 stdout、stderr、并发、健康失败、关闭期限与 manifest 竞态，六类 adapter 已覆盖配置合并、禁用、业务错误、未知/重复字段、索引完整性、URL 指纹和路径逃逸。

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
- [x] qBittorrent 真实容器 smoke 已接入 Docker CI 并在 Ubuntu 24.04 x86_64 CT 实跑通过：从首启日志读取临时密码后设置隔离测试密码，逐实例覆盖登录、版本、默认路径、reconnect、add/list/files/file-priority/start/stop/delete；合法 128 KiB WebSeed 完成统一导入→真实下载→SQLite→move/NFO/sidecar/completion→`deleteFiles=false` cleanup，唯一 category/tag/hash、容器、镜像和临时目录均精确清理。
- [x] 双实例容器统一导入门禁已在 Ubuntu 24.04 x86_64 CT 实跑通过：隔离 fixture 提供不同 info-hash，AnimeGoNet 后台 worker 通过 `/api/v1/ingest` 将 `mikan-ci` 实际投递到 `bt`、将测试用 U2 route 骨架实际投递到 `pt`；staged 响应不泄露 URL，目标实例 hash/category/tag/暂停状态/保存路径正确，另一实例不存在同 hash，测试任务/文件/tag/category 已精确删除。U2/TTG 首版业务仍暂缓。
- [x] 旧 YAML/环境变量出现 Transmission 时读取并生成 `UnsupportedDownloaderType`：按 `ANIMEGO_CLIENT`→显式 `ANIMEGO_CONFIG`/`--config`→`data_path/animego.yaml` 检测，只读取 `setting.client.client` 且不回显凭据；诊断未解除时强制关闭 workers、替换为空下载器 registry、拒绝导入/恢复/连接与路径测试，Web/API 保持可用并显示修复原因，绝不静默转成 qB。

## P8 — 下载、重命名、刮削

- [x] 移植下载管理状态机和 notifier：staged→dispatching→download_preparing→metadata_resolved→download_queued/skip→downloading/downloaded 已接入；后台 qB 快照同步把不可变做种目标、单调累计秒数和 waiting/seeding/completed 写入 schema v33，整理 worker 只按该持久化门禁推进，准备、整理和 cleanup 均有独立租约与安全重试。
- [x] 修复 qB 完成快照触发重复元数据 Run 的竞态：元数据领取仅接受 `download_preparing` 或准备尚未完成的旧 `downloaded` 任务，`preparation_state=completed` 后只能进入整理；schema v44 自动恢复“文件均已解析、qB 已完成、整理仍 pending”却卡在 `metadata_season_resolved` 的既有任务。
- [x] 移植重启恢复、去重、失败重试和删除 callback：dispatch lease 恢复、qB 同 hash 幂等、按实例+hash 运行快照恢复、离线 stale/实例 circuit breaker/健康探测与退避重试、每 job 不可变 download/save root 均已实现。上游重命名完成 callback 直接执行 `DeleteFile:true`；新程序按安全语义替换为持久化 `organizing_cleanup` 租约，固定 `deleteFiles=false`。qB 故障时已落盘媒体和 completion 保持不变，Store cleanup 租约可释放重领，健康探测关闭 circuit 后只重试 qB 任务清理。
- [x] 完成记录仅在下载、文件策略、重命名和必要 NFO/目录库写入全部成功后原子写入：worker 在所有文件、原子 `tvshow.nfo`、上游兼容目录 JSON 及 schema v27 索引全部成功后才于同一事务写 completion、来源 alias 并完成 episode claim，qB cleanup 独立在后；RSS winner 在 Bangumi 页面和 Torrent 网络访问前以 SQLite IMMEDIATE 事务复查同 `mikanid+来源EP` alias，并在 staging 前再次事务复查以关闭并发窗口。命中即返回 `already_completed`，删除业务完成记录后可重新进入。
- [x] 移植 `link`/`link_delete`/`move`/`wait_move`：四种策略均使用不可变路由快照和持久化逐文件操作；link 保留源文件，link_delete 在目标校验及业务完成后删除源文件，move 立即暂停并移动，wait_move 等做种完成后再暂停移动；失败可恢复且 qB 清理固定 `deleteFiles=false`。
- [x] `move` 安全编排：下载完成后暂停、持久化逐文件执行、TMDB规范路径、同卷原子移动/跨卷copy+SHA-256、冲突保全、崩溃恢复、原子 `tvshow.nfo`、目录 JSON/SQLite 索引、完成记录事务及独立 `deleteFiles=false` qB cleanup 已串联；本机 TestSpace 与 Ubuntu CT linux-x64 共享 `/download` 均以合法 128 KiB 文件完成真实 qB E2E。
- [x] 增加已整理 `Other` 文件的显式重新适配：任务库先预览文件存在性、大小和共享路径引用，只保留已确认 TMDB Series/Season 并重新执行 Episode 规则/AI；成功后从旧 `Other` 直接安全移动到规范 EP 路径，仍失败则原地保留，历史 Run/AI 日志不清除且不重新下载、不暂停/清理 qB。schema v47 保存逐文件审计；共享路径、文件缺失、活动租约和非 move 策略全部 fail closed。详见 [`docs/OTHER_FILE_READAPTATION.md`](docs/OTHER_FILE_READAPTATION.md)。
- [x] 将媒体整理、做种目标完成、删除下载器任务、删除下载源拆成独立持久化状态：schema v33 固化 `seeding_target_minutes`、单调 `seeding_elapsed_seconds`、waiting/seeding/completed 与完成时间，`0/-1/正数` 语义独立于 qB 瞬时 state；媒体操作、qB cleanup 与四类删除均按独立持久化状态、租约和失败重试推进，qB 删除固定 `deleteFiles=false`，源/媒体文件分别受捕获根目录约束。
- [x] 处理多文件、跨盘、目标冲突和部分失败：逐文件 operation 按 Torrent 相对路径/文件 ID 稳定执行；同卷优先原子 move，跨盘进入 task-owned partial + 容量/SHA-256 校验 + 原子提交；不同内容的既有目标保留源/目标并返回 `target_conflict`；前序文件已完成而后序文件失败时不写业务 completion，解除冲突后仅续做 pending operation，最终一次性完成全部 Episode 记录和独立下载器 cleanup。
- [x] 多文件 Torrent 逐文件去重：qBittorrent 暂停添加、metadata/claim 完成后逐项核对 index/path/size、重复与 ignored 文件 priority=0、wanted 文件 priority=1 后才恢复；全重复任务保持停止并以 `deleteFiles=false` 移除。除 fake/SQLite 并发、恢复和失败外，本机 TestSpace 已用四文件合法 Torrent 验证真实 qB `1,1,0,0` priority、未下载重复 EP/ignored 海报、主视频+绑定字幕落盘和单 completion；Ubuntu CT 另验证单文件容器全链。
- [x] 实现字幕识别与唯一绑定：同目录同 stem 优先、语言/default/forced/SDH 后缀原样保留、不同 stem 按来源 EP 唯一匹配、`.idx/.sub` 分别绑定并保留扩展；匹配后只复用视频的已验证 TMDB EP/claim/priority，未匹配或歧义进入已确认季度 `Other`，整理不产生重复完成记录。
- [x] 串联媒体目录 DB 与 NFO：NFO 与三层目录侧车都位于业务完成记录之前；侧车损坏、越界或索引失败会保持可重试且不写完成记录。
- [x] 任一季度匹配策略成功后，固定使用 TMDB `zh-CN` 名称（缺失时用 TMDB 原名）、Season Number 和 Episode Number 生成 `<TmdbName>/Sxx/Eyyy.ext`；字幕生成 `Eyyy.<保留后缀>.<字幕扩展>`，Other 保留安全清洗后的原文件名，均已串联持久化 move worker。
- [x] 确定性季度结果先执行同号 EP 快速校验；失败且统一 AI 开启、任务从未尝试 AI 时进行一次任务级映射，返回的 TMDB ID/Season 必须与已确认值相同，结果逐集由 TMDB 验证。
- [x] 保留来源名称和来源集号用于审计、去重诊断及 UI 展示：逐文件原始相对路径、来源 EP、本地文件候选与最终 TMDB 身份分别持久化并在任务详情并列显示；完成时写来源 alias，RSS 批次保存早期命中证据。AI 结果必须逐级通过 TMDB API 验证，未验证值不得参与路径、数据库键或 NFO。
- [x] 多文件任务逐集验证 TMDB Episode：已实现独立租约 worker、官方 Episode 身份验证、规范 Episode 持久化、人工/可信 offset、网络失败保持 pending、季度已知时 `Other` 原因，以及跨任务完成/活动 claim 的逐 EP 重复门禁；本机 TestSpace 已从隔离 SQLite 的合成“已验证 Episode”边界执行真实 qB 逐文件 priority/恢复/下载、字幕语言后缀 move、单一 completion 和安全 cleanup；Ubuntu CT 容器全链另以 Bangumi 日期证据匹配并最终验证 TMDB Episode。
- [x] 增加 `tmdb_fail_use_bangumi` 业务兜底开关，默认 `false`；关闭时 TMDB 完全失败沿用失败流程，不继续下载/刮削且不生成 NFO。
- [x] NFO 默认仅在 `tmdbid=0` 的 Bangumi 完全兜底写 `bangumiid`；新增默认关闭的 `write_bangumi_id_when_tmdb_matched` YAML/API/WebUI 选项，显式开启才在 TMDB 成功时写入，并提示共享 TMDB 根目录的覆盖风险。
- [x] 开关开启后，仅在权威 TMDB 成功访问且最终为确定性 Series 无匹配、已有有效 Bangumi Subject ID 时继续；季度固定本地 `S01`，不依赖 P2/P1，不输出有效 TMDB ID，动画根目录 `tvshow.nfo` 写 `<tmdbid>0</tmdbid>` 和对应 `<bangumiid>`。
- [x] 验证已取得 TMDB Series、仅季度匹配失败时仍走原季度 fallback，不误入 Bangumi 完全失败兜底；网络/认证/配置/协议/输入失败均禁止兜底。
- [x] 通过状态机、文件策略和合法小文件 E2E：显式本机集成动态生成 BitTorrent v1/128 KiB payload，以随机 `127.0.0.1` web seed 完成真实 qB `paused→priority 1→resume→download`、SQLite `downloaded`、Mikan `move`、NFO/sidecar/completion、`organizing_cleanup→organized` 和 `deleteFiles=false` 精确清理；源/目标字节完全一致，三次连续实跑通过，默认 solution/CI 仍不接触 TestSpace。

## P9 — 调度、Web API 与 Web 页面

- [x] 实现六字段 Cron 调度、StartRun 和 NextTime：支持秒级六字段、`?`、list/range/step、英文月份/星期与标准 descriptor，DOM/DOW 沿用 Cron OR 语义；时区/DST、启动立即执行、三次重试、并发任务、热增删唤醒、稳定快照和取消退出均由可控时钟测试覆盖，宿主仅在后台 worker 开启时运行 coordinator。
- [x] 实现 Bangumi/数据库/feed/plugin tasks：旧 Bangumi cache 下载由版本化 AnimeGoNetData 检查/下载/导入调度替代；目录数据库刷新、数据更新和逐 SourceProfile Mikan RSS feed 均由编译期内置 schedule plugin 执行。RSS 调度只携带来源 ID/revision，运行时从 SQLite 取得只写 URL；失败重试、审计、热增删、重启中断恢复和后台禁用门禁已覆盖，无 Python task 或反射发现。
- [~] 优雅退出和取消传播实现及门禁已生成但跨平台未验证：宿主固定 5 秒停止期限；所有后台 Worker、调度等待/重试、qBittorrent 活动调用、配置热应用和 RSS winner 租约清理均响应宿主停止；WebSocket 长连接在 `ApplicationStopping` 时主动关闭。JIT、win-x64 NativeAOT 及 Ubuntu CT linux-x64 NativeAOT 的 SIGTERM 零退出和句柄/SQLite 保留已验证；linux-arm64、macOS arm64 待原生 CI 实机结果。
- [x] 移植上游 HTTP API：计划原称“10 个”，权威 OpenAPI 实际列出 11 个 REST operation + 1 个 WebSocket operation；契约测试逐项比对基线，12/12 均有 AnimeGoNet 路由。最后缺失的 `/api/config` 已实现 `all/default/comment/raw` GET、`all/raw` PUT、legacy envelope/参数错误/Access-Key、写前强类型校验、可选不可覆盖备份和同目录原子替换；更新仅在重启后应用。
- [x] 新增 `/api/v1/ingest` 通用批量 Torrent/URL 导入 API，沿用 `source + data[].torrent + data[].info`；旧 `/api/download/manager` 已转换到同一 command，并补齐未经修改 AnimeGoHelper 只提交 Episode URL 时的 `mikanid+groupid+bgmid` 解析与持久化；`/api/rss` 与现代 `/api/v1/rss/ingest` 均已接入来源规则、统一 staging 与后台 qB dispatch。
- [x] 补齐 Mikan `/RSS/MyBangumi?token=...` 多番组聚合订阅：逐 item 从 Episode 页面解析 `mikanid+groupid`，同 URL 批内缓存，按 mikanid 拆成独立可审计子批次后分别执行五级过滤、有序优选、Bangumi 发现、去重和统一导入；单项身份失败隔离并保留稳定错误码。手动 WebUI 显示每个子批次的 mikanid/bgmid，真实私有 RSS 只读 smoke 已验证全部当前 item，未访问 Torrent/qB。
- [x] 将成功解析的 Mikan Episode URL → `mikanid+groupid` 和后续 `mikanid→bgmid` 均升级为 SQLite 长期缓存：默认 8760 小时，可配置，`0` 表示永久，跨刷新/重启复用；聚合 RSS、legacy 过滤和 AI 测试工具统一命中。只有完整有效 ID 才写入，失败、网络异常、缺 groupid 和带查询参数 Episode URL 不做缓存；`bolt/mikan_episode_identity` 与 `bolt/mikan_bangumi_identity` 可在“系统缓存”逐条查看和手动删除，不保存页面 HTML、Cookie、Torrent URL 或 passkey。
- [x] 将 passkey Torrent URL 和 `.torrent` announce 视为 secret：profile host白名单及不可变路由快照、逐跳redirect/DNS校验、校验IP固定连接、限时限量、严格Bencode/info-hash、请求期受限 staging、崩溃过期清理、qB确认接收后删除均已实现；AI 使用显式白名单数据边界，输入类型无 URL/fingerprint/announce/暂存字节/route/Cookie/凭据字段，统一导入 E2E 验证实际 URL、passkey 与 SQLite fingerprint 均不可达 matcher 和最终 Prompt。
- [x] 新增下载器实例和 SourceProfile 的版本化 CRUD、连接测试、路由预览及引用保护 API：SourceProfile CRUD/无副作用预览、下载器脱敏投影/连接测试、data_path 私有覆盖文件、凭据只写 create/update/remove、全局 revision、重启应用和引用保护均已完成。
- [x] 移植 access-key、响应 envelope、参数错误：直接 key、旧 SHA-256 hash、ping/sha256、RSS/manager/plugin/config/Bolt、WebSocket 均接入统一鉴权；legacy HTTP 保持 HTTP 200 + `code=200/300`，配置畸形 JSON/Base64/YAML/强类型值在替换前失败。
- [x] WebUI 独立鉴权提供可输入 AccessKey 的登录窗口：裸地址或凭据失效出现 401 时单实例弹出，验证 `web.webui_access_key` 后自动重试全部等待请求；明文不落浏览器存储，可选择 session 或长期记住 SHA-256，并可从顶部入口更换/清除。插件 `inner_plugin_mikan.access_key` 保持独立。
- [x] “设置与备份”增加 WebUI 监听 IP/主机名与端口编辑：直接回填 `web.host`、`web.port`，端口 0–65535 与主机格式由前后端共同校验，保存部署 YAML 并备份、重启生效；页面明确 `ANIMEGO_WEB_HOST/PORT` 与 ASP.NET Core URLs 环境覆盖优先级更高。
- [x] 移植 WebSocket 日志 pause/resume：保留 `/websocket/log`、旧 `type=log/count` 帧和三种控制命令；鉴权、逐连接暂停、1000 条有界缓存、慢消费者有界队列、脱敏及取消均已验证。
- [x] 将实时日志升级为详细日志工作区：兼容旧文本帧并在浏览器拆分 UTC 时间、级别、类别、Event ID、消息、异常和脱敏原文；支持级别/关键词/类别/Event ID 组合筛选、自动滚动、长行换行、单条展开和复制当前脱敏结果，浏览器仍只保留最新 500 条。HTTP 连接另有“全部 / 仅外部 HTTP 请求（Mikan/TMDB/Bangumi 等） / 仅 WebUI/API 入站 / 排除 HTTP”独立选项和外部请求快捷按钮，ASP.NET Core 入站轮询及仅包含本机监听 URL 的启动消息不会与真实外部请求混在一起；默认 Mikan、TMDB、Bangumi、AI、Torrent、封面和数据更新客户端显式记录脱敏的开始/状态/耗时，query、Cookie、passkey、API Key、正文及异常消息不进入日志。
- [x] 将“日志”独立为一级工作区：运行日志增加时间、仅异常、业务域快速筛选及级别统计；新增跨任务 AI 调用日志 API/页面，按任务、阶段、结果、模型和时间分页查询已持久化 provider usage，汇总成功/失败、Token、HTTP 与工具调用，并可回到任务详情。Prompt、响应正文和凭据继续不进入审计。
- [x] AI 调用日志持久化并显示当次通过主程序 TMDB 最终验证的 Series/Season/Episode 与 Episode 名称；多文件/跨季度按唯一 EP 有序展示，失败调用明确显示未通过验证。schema v52 从现有 `episode_resolution_attempt_id` 回填历史证据，后续人工重新适配不会改写旧 AI 审计。
- [x] AI 调用日志独立持久化并显示“AI 触发原因”：季度 AI 记录最后一个确定性季度失败码，Episode AI 汇总本批未决文件原因；Mikan 兼容解析被拒绝时保留精确拒绝码。schema v55 起记录，旧调用明确显示“历史调用未记录”，不把 AI 返回结果或最终错误冒充触发原因。
- [x] 增加默认关闭的 AI Debug 完整链路：按 run_id 在 `data_path/ai-debug` 独立保存 AI 前任务输入、同一 run 已落库的确定性尝试、发布时间证据、Prompt 模板/最终渲染 Prompt、全部 AI/MCP 请求响应 Body、解析候选、TMDB 本地验证和用量；AI 调用日志提供分段时间线查看与单条删除。Authorization Header、API Key、Cookie、passkey 和 Torrent URL 不落盘，普通 SQLite AI 审计仍保持轻量。
- [x] 移植轻量滚动文件日志：固定写入 `data_path/logs/animego.log`，仅 Information 以上，2 MiB、14 份备份、14 天保留；与 WebSocket 共用脱敏格式，宿主停止后由 DI 唯一释放句柄。
- [x] 兼容 `DeQxJ00/AnimeGoHelper`：固定原脚本 `78a9d0d8` 与 SHA-256，不修改执行；`/ping`、`/api/rss`、`/api/download/manager`、`/api/plugin/config` 和 `Access-Key` 已覆盖。Kestrel 契约验证配置上传立即影响 RSS、快速下载仍跳过过滤；Chromium Mikan fixture 验证可见“单/全”控件、上传/获取配置和无损 Base64 往返。
- [x] 将旧插件名 `filter/mikan_tool.py` 及等价别名映射到 SQLite 过滤规则；Base64 JSON 可无损同构往返、并发 legacy 上传完整提交，不查找、不创建且不执行 Python 文件。
- [x] 实现 Mikan 过滤 Web UI：RSS 过滤总开关、五档规则 CRUD/启停/排序、关键词 JSON 数组编辑、服务端样例预览及逐档决策详情、旧 JSON 导入导出、revision 冲突和快照回滚均已接入；页面明确警告多 F0“最后结果生效”、空关键词匹配全部标题和区分大小写语义。
- [x] 实现 Mikan RSS 优选 Web UI：原生 TypeScript 页面支持白/黑名单及有序组/数组的增删、启停、上下移动、values 编辑、SourceProfile 独立开关、expected-revision 保存、真实服务端批次 preview（名单结果、winner、实际执行组），以及 schema v25 历史快照选择与 revision 安全回滚；首版使用可键盘操作的上下移动，不依赖拖拽。
- [x] 移植静态页并生成 OpenAPI：静态 TypeScript/HTML/CSS 页面由 Kestrel/AOT smoke 覆盖；官方 .NET 10 AOT-safe 生成器在 `/openapi/v1.json` 输出完整当前 Minimal API 契约，具有确定性 operationId/标签、现代及旧 Access-Key 安全说明、无运行端口/路径/密钥泄露，并由原生进程 smoke 验证。
- [x] 将超长单页重构为左侧一级菜单和页面内二级菜单；后续按实际管理边界补充独立“下载工具配置”，并将“测试工具”明确命名为“AI 匹配测试工具”。全部工作区通过 hash 可直接定位/前进后退，默认只显示当前二级页面；窄屏左栏收成可访问抽屉，原表单、revision、轮询和 API 契约不变。
- [x] 增加一级“Bangumi缓存”：将 AnimeGoNetData/Bangumi 离线 Subject、Episode、前传关系的版本状态、检查、下载、离线导入和回滚页面迁入独立工作区；通用 `bolt/themoviedb` 缓存继续留在系统缓存管理，避免混淆。schema v45 持久记录由活动 AnimeGoNetData 直接满足的 Subject、完整 Episode 集和前传关系读取次数，页面保留累计命中、分类计数与最近命中时间；schema v46 进一步逐条保存命中类型、bgmid、返回条数、数据版本与时间，提供类型筛选和服务端分页。在线回源和不完整 Episode 不计入。
- [x] 通过 API/WS 契约差分测试：生成文档与所有当前非排除 HTTP endpoint 的 method/path 精确相等；机器 golden 穷尽固定上游 OpenAPI 的 11 个 HTTP + 1 个 WebSocket operation，并由真实 Kestrel 响应逐层校验 root/data/item、失败 envelope、日志帧和 control 帧精确字段。原 AnimeGoHelper 浏览器 E2E 同样已完成。
- [x] 创建 Web 前端工程、类型化 API client 和前端测试基线：TypeScript 7 strict 工程输出原生 ES module；共享 JSON client 只接受同源绝对路径、集中携带 Access-Key、序列化类型化请求体，并以稳定错误类型处理结构化失败/非 JSON 响应；运行状态与目录数据库请求已接入，Node 内置 runner 的 5 项安全/协议测试及 CI 产物差分门禁已建立。
- [x] 实现仪表盘和下载器/任务状态：下载状态卡片、进度、连接且非 stale 的跨实例速度汇总、活动/暂停/失败/等待整理/完成/离线指标、qB 状态与 AnimeGoNet 业务阶段独立筛选，以及 `download_preparing`/重复跳过、元数据 Series/Season/Episode 阶段、失败原因、策略尝试时间线、文件归类计数、准备/整理失败详情和显式重试入口均已接入。
- [x] 实现两层下载进度投影：qB规范状态/百分比/容量/速度/ETA/Seeds/Peers与AnimeGoNet业务状态已分离，qB 100%映射为 `downloaded` 而非最终业务完成；下载 API/WebUI 另显示持久化做种目标、状态、累计时间、百分比和完成时间。schema v37 以 SQLite 持久化 `rename_planning/media_transfer/subtitle_transfer/nfo_write/directory_index/cleanup_downloader/completed` 阶段及单位进度；重试保留失败阶段，文件传输从已完成的不可变 operation 续算，`link/link_delete` 清理租约过期不会误回退并重复文件整理。
- [x] 实现按实例隔离的qB同步器和`DownloaderTaskSnapshot`：活动约2秒、空闲约10秒、单实例单在途、实例失败隔离、离线保留stale快照、重启按实例+hash恢复，以及首错2秒、连续半开失败指数增长并封顶120秒的熔断已完成；显式连接测试可安全绕过等待窗并在成功后复位。
- [x] 实现下载列表/详情/文件级 priority 与 wanted 进度、筛选搜索分页和状态时间线；详情合并 SQLite 文件分配与 qB 实时文件快照，qB 离线时保留持久化信息并返回安全失败码，不暴露绝对路径或凭据。
- [x] 实现暂停、恢复和 AnimeGoNet 业务重试；写操作校验 job revision，成功/失败均写入 schema v24 审计事件，任务卡片的删除操作仍只进入四类删除中心执行预览/确认。首版不复刻 Tracker/Peer 明细、piece 图、限速、强制校验/汇报和 qB 全局设置。
- [x] 修复活动 Episode claim 消失后的旧重复跳过任务：仅 `episode_claimed_by_another_task` 保留 paused qB 载荷并显示“重新检查占用”；显式重试原子复查 completion/claim，空闲时重新 claim、恢复 Episode 文件和下载准备，仍占用或已完成时返回独立稳定冲突，旧 priority/wanted 不会沿用。
- [x] 实现多下载器页面：原生 TypeScript 展示命名实例、脱敏端点、路径、凭据状态、连接/失败、引用与任务数量；连接测试显示 qB 客户端版本、默认保存路径、延迟和任务数，路径探测显示 download/save 路径可见性与硬链接能力；支持 revision 安全的凭据只写新建/更新/移除与重启提示。
- [x] 实现输入源页面：原生 TypeScript 已接入 SourceProfile CRUD、内置及已发现 external source adapter 下拉（未启用/缺包明确禁用）、完整启用下载器实例下拉、Host 白名单、规则开关、文件策略、category、静态/动态 tags、做种分钟、Mikan Cookie 只写设置、revision 冲突、move 提示，以及复用真实 adapter 且无副作用的路由预览。schema v38 增加默认开启的来源级重复命中通知开关并固化进任务路由快照；RSS 来源 alias/并发 winner 与规范 TMDB Episode 重复会通过事件 4301 写入脱敏实时日志，关闭仅抑制通知且不改变全局去重。
- [x] 实现手动 RSS/下载提交与操作结果：原生 TypeScript 页面按已启用 SourceProfile 提交单个 Torrent，Mikan RSS 可选择独立来源 revision；单条 Mikan 导入可输入 `/Home/Episode/{40位ID}`，按所选 SourceProfile/Cookie 复用长期身份缓存并解析分组 RSS，自动填充 title、Torrent URL、source item/work ID、`mikanid`、`groupid` 和 `bgmid`，解析动作本身不暂存 Torrent、不访问 qB。带 passkey 的 URL 请求发出后立即清空且不进本地存储，最终导入结果只显示任务、规则、下载器和不可逆指纹。
- [x] 实现配置表单、服务端校验、脱敏 diff 和保存备份：Web 不展示或改写含部署 secret/注释的原始 YAML，而是先 `POST /api/v1/config/preview` 验证 revision 并展示字段级生效方式，明确确认后才写 `application.private.json`；覆盖/恢复前将旧 revision 原子保存到 `data_path/backups`。
- [x] 实现总配置归档：版本化 JSON 覆盖应用私有配置、下载器、输入源、RSS 规则、Mikan 五级过滤、人工作品规则和外部插件；WebUI 支持导出、SHA-256 预检、确认导入、手动备份、下载、恢复和删除。导入/恢复前自动创建安全备份并采用同 ID 覆盖、包外项目保留的安全合并；归档明确包含凭据与 qB 实例路径，但排除部署根目录、运行任务、下载历史、可信 offset、缓存、日志及媒体文件。API 流程、错误摘要、4 MiB 上限与 NativeAOT 均纳入验收。
- [x] 总配置归档增加默认关闭的每日自动备份：按主机本地日历日每天最多一份，服务重启可补齐当天缺口；保留份数可在 WebUI 配置且默认 10，仅轮转 `automatic-*`，绝不删除手动或导入/恢复前安全备份。策略使用 AOT-safe JSON 原子保存并随总配置归档迁移。
- [x] 配置页显式展示四个确定性季度失败开关及一个统一 AI 元数据开关，说明优先级/触发阶段和 Backtrace/AI 前置条件；AI/TMDB 密钥及保存前 diff 直接回填明文，环境锁、即时生效和需重启字段均可见。
- [x] 唯一正式 AI Prompt 已纳入应用配置、私有覆盖、部署锁和 WebUI 编辑器：后台 Worker 与“AI 匹配测试工具”默认读取同一份有效模板；模板保留全部契约标记且限 128 KiB，预览只显示版本/长度/短哈希，保存后重启生效。
- [x] 动画任务详情同时展示任务级 `source_evidence`、逐文件来源名称/来源 EP/本地候选和最终 TMDB 名称/Season/Episode，以及 AI 调用状态、TMDB 验证可信依据、最终失败原因和策略尝试时间线；来源 profile/revision、标题、Mikan/Bangumi/AniDB/IMDb/发布时间与不透明 ID 指纹独立成区，不采信或展示模型自报的数字置信度，也不把来源值表示成 TMDB 权威结果。
- [x] 实现作品库季度列表：schema v23 已持久化 TMDB Series/Season 名称、首播日期、总集数与 Series/Season poster 路径；P4/P3 联合匹配会再请求官方 Season endpoint。正常任务先保存 Series/Season，待 Episode 确定性/AI 判断及最终 TMDB 验证结束后才事务替换正式 Episode snapshot，避免未完成 EP 判断被整季缓存冲突阻断；待补全恢复仍在最终恢复事务保存完整 snapshot。列表/详情 API、Cover 安全代理/缓存/占位图和静态 TypeScript 页面均已完成；页面显示 TMDB 规范进度、取得策略、验证状态、一致性警告和可筛选 EP 网格，删除完成记录会立即恢复未下载。
- [x] 实现作品库服务端分页排序和前端升/降序：服务端与页面支持最后业务更新时间（默认降序）、TMDB 名称、TMDB Season 开播日期、本地加入日期四种升/降序，空开播日期始终置后并使用 TMDB ID/Season 稳定翻页；排序、方向、页大小、EP 筛选和当前详情保存在浏览器本地。
- [x] 实现作品库服务端搜索：按 TMDB 规范名称、原名、季度名和精确 Series ID 检索全部作品，不受当前分页限制；搜索词与排序/页大小一同保存在浏览器本地，提交或清除搜索都会回到第一页并关闭旧详情。
- [x] 统一作品库 Cover 卡片标题高度：TMDB 规范名称最多显示两行并保留两行布局空间，超出部分省略，鼠标悬停显示完整名称，避免长短标题造成卡片信息和 EP 进度条错位。
- [x] 恢复作品库列表 Cover 的标准 `2:3` 高宽比：封面不再跟随卡片文字高度纵向拉伸，维持原三列响应式断点；同时压缩卡片内容间距，使两行标题、身份信息和进度在标准 Cover 高度内稳定展示。
- [x] 增加显式外部媒体扫描与补录：作品库支持全库手动扫描，季度详情支持单季度手动扫描，默认不运行后台扫描；只接受 `save_path/<TMDB规范名>/Sxx/E###.<视频扩展名>` 的非空直接视频文件，逐集验证现有 TMDB Episode snapshot，以 `external_import` 幂等写入规范完成记录并完成同 EP 活动 claim。`Other`、未知 EP、非标准命名、符号链接和同 EP 多视频均跳过并在 WebUI 返回相对路径明细，不移动/删除文件、不伪造 sidecar/NFO/来源 alias。
- [x] 统一 WebUI 基础控件视觉规范：输入框、搜索框、下拉框、文件选择器和主/次/危险按钮共用高度、字体、圆角、边框、背景、placeholder、disabled 与 hover token；修复任务筛选按钮被 Grid 拉高、下载器动态按钮使用浏览器默认样式及缓存下拉框未着色，并保留导航、作品卡片和代码编辑区的专用样式。
- [x] 固定 WebUI 根字号为 `16px`，使全部 `rem` 尺寸不再随 Chrome/Firefox/Edge 的默认“字号”设置变化；保留浏览器无障碍“最小字号”的用户优先级，不采用强制缩放绕过。
- [x] 修正 Mikan 手动设置的单 Torrent 提交鉴权：WebUI 改用独立 `/api/v1/ingest/manual` 管理入口并复用统一导入处理，接受 `web.webui_access_key`；外部插件 `/api/v1/ingest` 继续只接受 `inner_plugin_mikan.access_key`，避免 401 后误弹 WebUI 密钥仍无法提交。
- [x] 下载任务卡片显示已落库的 TMDB Series/Season/Episode：同页任务使用一次批量 SQLite 查询，按 Series+Season 合并多文件 EP；未解析任务不显示猜测占位。日志一级菜单新增“匹配日志”，按最近更新可视化 Series→Season→Episode 策略流程，支持展开来源/TMDB 对照与完整 Attempt 时间线；下载卡片可按 task_id 直达并自动展开。
- [x] 统一 WebUI 后台刷新策略：下载任务、元数据任务、待补全 TMDB、作品库与数据更新只在对应二级页面可见时轮询；列表内容未变化、详情展开、编辑控件聚焦或对话框打开时不重建 DOM，下载/元数据列表在安全更新时保持首个可见卡片位置，后台失败保留当前内容。
- [x] 扩展总览关键统计与直达入口：按下载与整理、匹配与人工处理、资产与运行规模显示 15 个权威统计；`download_skipped_duplicate` 独立显示“重复跳过”且不计入等待整理，下载及匹配计数点击后应用目标列表的精确服务端筛选，其余进入对应作品库、来源或下载器页面；五类 API 并行且允许部分失败，真实 WebUI 跳转已验收。
- [x] 总览增加主进程运行内存、按逻辑 CPU 数归一化的实时 CPU 占用和当前 `data_path` 容量；目录扫描跳过重解析点并缓存 60 秒，三项均提供直达日志或目录设置的按钮入口，并由 API、静态契约和真实 WebUI 验收。
- [x] 实现 Cover 后端代理、本地缓存和占位图，不向浏览器暴露 TMDB API key；列表查询使用批量投影，避免按作品/EP产生 N+1 查询。`poster_url` 只指向同源 `/api/v1/library/covers/{tmdbSeriesId}/{seasonNumber}`；Season/Series 回退、5 MiB 流式上限、图片魔数校验、并发合并、磁盘缓存与失败占位均有测试。
- [x] 将 TMDB 未解析及 `tmdbid=0` 兜底条目放入“待补全 TMDB”，不生成 TMDB EP 网格、季度封面或完成比例；逐项恢复并验证真实 TMDB 映射后，再事务并入标准作品库。
- [x] 待补全 TMDB 详情展示兜底完成记录、实际去重身份/作用域和跨来源重复风险，但不把它表示为 TMDB EP 下载状态；API 不暴露内部 scope key、媒体路径或伪造 TMDB 身份。恢复后的原任务详情会显示同 bgmid/TMDB/save-root 关联的 NFO 重写作业 `pending/writing/failed/completed`、尝试次数、稳定失败码和重试/完成时间，响应不返回 save root 或系列目录名。
- [x] 实现 TMDB 作品库季度 CRUD：创建只接受 `TMDB Series ID + Season` 并在线验证后保存 Series/Season/完整 EP snapshot；更新是带 `resource_revision` 的 TMDB 权威刷新，不提供手工改名或伪造季度；删除只允许无任务、完成记录、claim、Mikan 人工规则、fallback 记录和待写 NFO 引用的本地投影，绝不顺带删除下载器任务、下载源文件或媒体文件，有引用时引导进入四类删除流程。
- [x] 增加 Mikan 作品规则 CRUD：原生 TypeScript 页面按 `mikanid` 读取并以 expected revision 创建/更新/禁用/清除 Bgm/TMDB Series/Season/EP Offset；影响预览权威区分未来自动应用、可显式重试的失败任务、活动中保护、已解析保护和已整理保护。显式重新匹配只重置未持有运行租约的 `metadata_failed` 任务，不改写已解析/已整理任务、完成记录或媒体文件；可选样例来源 EP 继续执行保存前 TMDB Series→Season→目标 Episode 在线验证。
- [x] 作品详情展示 Series/Season/Episode 的 TMDB 获取阶段、验证状态、人工偏移和最后解析时间：季度详情现在汇总当前 Mikan 人工 EP offset、最多 50 个关联任务和最多 200 条跨 Run 逐次验证记录；保留任务/Run/策略优先级、阶段、结果、稳定错误码、可重试性、耗时与脱敏原因，并明确标记截断，不把当前规则冒充历史实际值。
- [x] 实现四类资源删除命令及独立任务记录删除：四类资源仍为业务完成记录、qB 任务、源文件、媒体文件；schema v48 新增显式“AnimeGoNet 任务记录”，执行顺序固定为 qB 任务（永不带文件）→源文件→媒体文件→业务记录/claim→任务记录。任务记录删除不会隐式代表前四类操作；有 qB 任务时必须同次选择删除 qB 任务。Other 重新适配结果处于待人工审核时，API 和 WebUI 均禁止删除任务记录。
- [x] Other 重新适配升级为从来源重跑：只复用已下载/已整理实体文件，从任务保存的无查询参数 Mikan Episode URL 重新获取 `mikanid+groupid` 和 `mikanid→bgmid`，跳过旧 Mikan 身份、Bangumi Archive、TMDB 成功响应与可信 offset 的读取缓存，重新执行 Series→Season→Episode；新成功响应仍回写长期缓存，人工覆盖优先级不变，旧运行/策略/AI 日志不删除，强制重跑不受历史 AI 已尝试标记抑制。共享媒体路径使用校验复制并保留源文件，独占路径保持 move；整理完成后必须由任务中心人工审核，审核前不能删除任务记录。
- [x] Other 人工审核改为可核对的表格化前后对照：schema v49 在重新适配开始时逐文件固化原归类、Other 原因及 TMDB Series/Season/Episode；审核弹窗按文件展示“信息项 / 适配前 / 适配后”表格，归类、TMDB 名称/编号、失败原因、原/新媒体位置、Episode 取得策略和共享文件复制语义分行显示，用户确认后才写入审核通过。旧批次没有固化的字段明确显示未知，不由 WebUI 猜测。
- [x] 重新适配完成状态与审核状态分离：底层任务继续使用真实 `organized` 状态，任务卡明确显示“重新适配待审核 / 重新适配审核完成”；审核投影返回 `processing / awaiting_review / review_completed`、执行完成时间、审核完成时间和最终文件统计，审核通过后仍可只读查看最终对照。
- [x] Other 人工审核支持逐文件修正 TMDB Series/Season/Episode：schema v50 以 `manual_review_override` 单独审计，服务端先执行 TMDB 三段权威验证；唯一目标复用既有文件安全重新整理，独占源迁出 Other、共享源复制保留；目标已完成/被占用时保留 Other 且不自动删除。审核通过只标记“重新适配审核完成”，任务保持真实 `organized`，删除仍必须走独立显式操作。
- [x] Web UI 支持按失败阶段、错误码、可重试性和处理状态筛选，并提供分页与最后更新/标题/状态/失败分类排序；提供安全的“重新匹配”，明确区分“可安全重试（需显式）”、需配置修复、需人工处理、处理中、已解析、已跳过和已兜底。当前尚无自动重试编排，因此不把 `retryable=true` 误标为“已经进入自动重试队列”。
- [x] 对当前应用配置环境变量覆盖字段显示最终有效值、环境变量来源和只读锁定状态；API 拒绝改写被锁字段和凭据，保存其他字段时保留锁字段原有底层覆盖/继承语义，避免环境变量移除后遗留伪覆盖。
- [x] “设置与备份”已拆为“目录与路径 / AI 与 MCP / 网络与代理 / WebUI / 导入导出与备份”；`paths`、`network`、`ai` 分区 API 只合并各自字段并保留 revision 冲突保护，避免不同页面旧值互相覆盖。目录页直接编辑全局 `download_path` 与 `save_path`，显示 qB 路径映射到媒体库的完整流向；`data_path` 另行合并写入部署 YAML 并备份，明确不自动搬迁任何 SQLite、私有配置、日志或缓存，迁移仍需停机复制完整旧目录。
- [x] 动画库季度详情支持“Mikan 下载全部 / EP 自动补完”：从该季度历史任务提取可审计的 `source_profile_id + mikanid` 关联，即使旧任务缺少 `groupid` 也可从 `/Home/Bangumi/{mikanid}` 的字幕组列表发现可用 groupid；已有 groupid 默认勾选，用户可改选或多选，再按配置的 Mikan Base URL 分别读取 `/RSS/Bangumi`。页面展示字幕组、来源 EP、人工/可信 Offset 推导的目标 TMDB EP、完成去重状态和可勾选列表；已有来源完成 alias 或目标 TMDB EP 默认不选，非普通 EP 默认不选，未知 Offset 明示后交给正式元数据流程。确认时重新获取作品页与 RSS、校验字幕组归属、季度 revision 与候选身份并复用现有黑白名单、有序规则、SQLite 去重和统一导入；选择超过 12 条由 WebUI 二次确认，不等待下载完成且不向响应/页面暴露 Torrent URL 或 passkey。
- [x] 动画库季度详情按已确认的 `tmdb_series_id + tmdb_season_number` 提供 TMDB 季度页面外链；固定新标签页打开官方 `/tv/{tmdbid}/season/{season}`，不从标题或未验证候选推断。
- [x] 实现插件分类、启停、args/vars 和校验视图：外部包分类/版本/RID/能力、逐包校验错误、运行/退避/自动禁用/reset、revision 持久启停、args JSON 与 schema vars 表单均已接入响应式页面；配置 API/WebUI 直接回填 `writeOnly` 值并保留显式清除，运行状态、错误和日志不返回原值。
- [x] 完成本机配置敏感值全局直显：主配置密钥及保存前差异、qB 用户名/密码、Mikan Cookie/RSS URL、AI Tester API Key、手动 Torrent/RSS URL 和外部插件 `writeOnly` 值均可直接查看；186 个定向 App/API/WebUI 测试验证回填与日志/运行状态脱敏边界。
- [x] 实现“系统缓存”浏览和安全删除：现代 `/api/v1/cache` 分页显示 `bolt`/`bolt_sub` 的真实 bucket 与 key，并通过单条详情接口按需返回未截断的完整 `value_json`；最大 8 MiB 的 value 不进入列表响应，避免整页放大。分页读取惰性清理过期项，`bolt_sub` 永久只读，`bolt` 单项删除需要二次确认及绑定当前 key/value/TTL/更新时间的 opaque token，预览后变化返回冲突。静态 TypeScript 页面使用纯文本 DOM 展示、Access-Key、Kestrel/OpenAPI 和 NativeAOT 均已接入；不开放任意 SQL、整 bucket 删除或业务表删除。
- [x] 实现实时日志过滤、暂停、恢复和断线重连：静态 TypeScript 页面按级别筛选，安全 DOM 渲染并保留最新 500 条；浏览器隔离验收已覆盖暂停不增长、恢复补发、过滤、手动重连和零 console error。
- [x] 完成响应式布局、统一空/错/加载状态和基本可访问性：主异步区域共享显式状态机与安全文本节点，loading/empty/error 使用对应 busy/status/alert 语义；提供首个键盘跳转入口、全局可见焦点、44px 控件目标、reduced-motion 和 620px 移动端收敛布局；静态 DOM 契约自动检查唯一 ID、section/dialog/控件名称、非正 tabindex 与初始状态，390×844 / 1280×800 本机 Kestrel 验收均无横向溢出和 console error。
- [x] TypeScript 7 strict 类型检查和确定性编译已接入独立 CI job，提交产物必须与源码一致；共享 API client 与 DOM 状态/可访问性 Node 单元测试均已接入。本机 win-x64 NativeAOT 已通过 Chromium 桌面/390px 移动端 Playwright 2/2，Ubuntu CT linux-x64 发布镜像完整链路 Playwright 1/1 通过。
- [x] 用未修改 AnimeGoHelper 原脚本 + Tampermonkey API/Mikan 隔离 fixture 页验证“单集”“全集”“上传/获取过滤配置”；两条 Chromium 用例同时校验 SHA-256 Access-Key、真实旧请求体/响应 envelope 和零 console/page error。
- [x] 一级“插件”拆分“内部插件 / 外部插件”；内部插件页提供 `Web API / AnimeGoHelper (Mikan) 油猴插件`，明文回填/修改部署 AccessKey（新部署默认 `123456`）、自动显示 `/api` 地址、固定 `PluginName=inner_plugin_mikan`，并保留旧 `filter/mikan_tool.py` 后端别名。
- [x] 将外部插件/API 与 WebUI 鉴权完全拆分：`inner_plugin_mikan.access_key` 仅保护 AnimeGoHelper、兼容插件接口和精确的统一导入端点，`web.webui_access_key` 独立保护其余管理 API/日志 WebSocket且默认留空；两把密钥使用不同 header/query、不能交叉授权。应用配置页明文回填并保存备份，不再展示专用地址；裸地址由登录窗口/顶部 AccessKey 入口完成鉴权，旧查询参数书签继续兼容。旧 `web.access_key` 只作启动兼容读取，WebUI 保存时迁移删除；Mikan URL 解析与 RSS 手动导入保持在 WebUI 边界。

## P10 — 组合与发布

- [x] 用 `测试数据.csv` 的 29 条真实 Mikan 输入完成可续跑链路审计：29/29 已真实执行 Mikan 输入、隔离 qB 投递/文件清单、规则筛选、SQLite 去重和 Bangumi/TMDB 映射；第 2–4 行另完成真实 BT 下载与 move 整理。为避免公网下载时间影响业务验收，第 5–30 行按显式 `synthetic_file` 测试模式在每次 `run-*` 隔离目录按 Torrent 清单生成测试文件，再走正式 move、重命名、NFO/sidecar、completion 与 `deleteFiles=false` 清理；其中 18 条完成整理、7 条按同 TMDB+EP 完成记录正确 `SkippedDuplicate`。全部命中 CSV 期望，AI 0 次/0 token；报告不含凭据、passkey、Torrent URL 或媒体绝对路径。合成载荷不记作真实下载。

- [x] 完成 Host DI 和 CLI 行为：固定上游 `cmd/animego` 的 `config/debug/web/backup` 四个开关与对应 `ANIMEGO_*` 环境别名均已审计；兼容 Go 单短横线及现代双横线，裸 bool 等价 true，非法值在创建运行目录前失败；debug 同时放开宿主与滚动文件 Debug 日志，`web=false` 使用无监听 `IServer` 保留后台 worker，`-h/-help/--help` 不启动宿主。五 RID NativeAOT workflow 验证 help 和 headless 零 TCP 监听。
- [~] Docker NativeAOT 双架构功能已生成但 arm64 未验证：双架构 Dockerfile、Buildx CI 和容器 smoke 均已建立；Ubuntu 24.04 x86_64 CT 已真实构建并运行 linux-x64 镜像，linux-arm64 构建/运行仍待原生或 Buildx runner 结果。
- [x] 非 root、PUID/PGID、healthcheck、SIGTERM、只读根文件系统门禁已在 Ubuntu 24.04 x86_64 CT 实跑通过：smoke 强制任意非 root UID/GID、只读 rootfs、`/tmp` noexec tmpfs 与 no-new-privileges，并确认 `/data`/`/download`/`/tmp` 可写、healthcheck、7 秒 SIGTERM 零退出和 SQLite 保留。
- [x] 添加连接外部下载器和内置 Compose 下载器的部署示例：规范 YAML、环境变量、本机 TestSpace、官方双 qB Compose 均已记录；外部/远程双 qB 示例只启动 AnimeGoNet，强制地址和凭据由未跟踪环境变量传入，明确两端同一共享存储必须映射成相同 `/download`，并记录连接测试、硬链接路径探测、显式 Torrent 验收和清理边界。
- [x] 固定官方 Docker：`data_path=/data`、`download_path=/download/incomplete`、`save_path=/download/anime`。
- [x] 提供写有上述绝对路径的 Docker 容器配置；Compose 卷与配置逐项一致，不依赖隐藏路径修正。
- [x] 官方 Compose 已将 AnimeGoNet 与下载器的同一宿主目录统一挂载到 `/download`；Ubuntu 24.04 x86_64 CT 已真实验证 `/data`、`/download/incomplete/{bt|pt}`、`/download/anime` 的共享映射、写入和整理结果。
- [x] 外部下载器 `client.download_path` 路径转换与错误诊断已验证：主程序读取 qB 默认保存路径并执行显式路径/硬链接能力探测；Ubuntu CT 双 qB 外部容器的 `/download` 跨容器映射与完整下载/整理通过。
- [~] Linux x64/arm64 NativeAOT 构建、原生 smoke 与 artifact 门禁已生成但 arm64 未验证：Ubuntu 24.04 x86_64 CT 已实际构建并运行 linux-x64 NativeAOT 镜像、API/SQLite/WebUI/SIGTERM smoke；`ubuntu-24.04-arm` 的 linux-arm64 原生 runner 结果仍待验收，不以 x64 结果代替。
- [x] 外部 C# 插件目录挂载、非 root 启动和禁用回退门禁已在 Ubuntu 24.04 x86_64 CT 实跑通过：专用 linux-x64 NativeAOT source fixture 直接引用官方 SDK，包子目录只读且 `plugin-data` 可写；统一导入实际启动插件并记录非 root UID、确认包不可写，API 禁用后证明不再执行。其他 RID 由统一跨架构发布项继续跟踪。
- [~] 五 RID NativeAOT artifact 发布链已生成但远端未验证完整：`win-x64` 本机 publish/smoke 与 Ubuntu CT linux-x64 NativeAOT 运行已通过；`win-arm64`、`linux-arm64`、`osx-arm64` 必须等待各自原生 GitHub runner，五份 artifact 尚未整体发布成功。
- [x] 生成 checksums、SBOM、第三方许可证：五 RID NativeAOT workflow 在上传前从实际 publish 目录和精确 NuGet restore graph 确定性生成 `SHA256SUMS`、CycloneDX 1.5 `sbom.cdx.json` 与 `THIRD-PARTY-LICENSES.txt`；逐文件哈希、ordinal 排序、SPDX/许可证文件、路径脱敏和重复运行字节一致均有真实脚本测试。
- [x] 完成新安装、旧配置升级、旧数据迁移演练：JIT/NativeAOT 新安装首次 YAML、目录和 SQLite 已通过隔离 smoke；win-x64 原生二进制完成 1.6.1 原字节备份→规范 1.7.1 重写→正常启动，五 RID CI 已加入相同双 smoke。旧 Bolt 以只读 Go schema-v1 JSON 导出后由 .NET schema v39 单事务导入；跨平台 CI 的组合 smoke 在同一隔离目录验证 3 条旧 sidecar 索引、六个 bucket、过期跳过、重复导入和重启保留。
- [x] 全链路 JIT/AOT/Docker E2E 已在 Ubuntu 24.04 x86_64 CT 实跑通过：确定性合法 Torrent/WebSeed、Bangumi/TMDB fixture、统一导入、Mikan move、SQLite 去重、双 qB 路由、真实下载、Series/Season/Episode 验证、整理/NFO/三层 sidecar、作品库/下载/元数据 API、静态 WebUI Playwright 和精确 qB 清理均完成；Episode 以 Bangumi 日期证据匹配并最终验证 TMDB，AI 未调用。
- [x] 发布镜像 Web UI Playwright E2E 已验证：固定 Playwright 1.62.0/Chromium，本机 win-x64 NativeAOT 2/2 通过；Ubuntu CT 在无宿主 Node 的条件下由官方 Playwright 容器对同一 linux-x64 NativeAOT 完整链路结果执行 1/1 页面断言并通过，无 console/page error。
- [x] 编写用户迁移、部署、插件和运维文档：部署 YAML、Docker/外部 qB 路径和本机隔离验收之外，已补齐用户迁移、外部 C# 插件安装/升级/回滚、日常状态检查、停机备份/恢复、SQLite 校验、版本回退和故障处理手册；README 统一入口与文档链接/安全边界契约测试已通过。Ubuntu CT linux-x64 Docker 验证已记录，linux-arm64 与外部发布边界继续明确标记为未验证。
- [~] 首个可用预发布自动化已生成但远端未验证：仅 `vMAJOR.MINOR.PATCH-SUFFIX` 标签在五 RID 全部成功后才下载完整 artifacts、逐 RID 验证并确定性打包，随后以 `--verify-tag --prerelease --latest=false` 创建不可覆盖的 GitHub Prerelease；实际标签推送与远端 Release 待仓库所有者执行/验收。

## P11 — AnimeGoNetData

- [x] 确认独立数据仓库名称为 `AnimeGoNetData`；托管地址由该独立任务配置。
- [x] 定义 `manifest.json`、subjects/episodes JSONL schema 和版本策略：v1 字段、兼容规则、不可变发布、确定性 gzip/JSONL、哈希/大小/数量/ID 范围与引用语义见 `docs/DATA_MANIFEST_V1.md`；主程序已有 NativeAOT-safe 严格 manifest 解析器。
- [x] 定义并验证 `setting.data_update` 配置 schema、默认值、环境变量覆盖和热重载行为：主程序提供默认关闭、04:00 六字段 Cron、可空 manifest URL、自动下载/导入、保留 2 版和 300 秒超时的强类型绑定/校验；Web/API 使用 `data_path/config/application.private.json` 安全私有覆盖而不改写部署 YAML，采用 revision 原子写入。七个字段均支持环境变量只读锁；保存或恢复后共享运行快照与 Cron 任务立即热重排，其他配置字段仍明确要求重启。
- [x] 实现 Bangumi Archive 下载、校验、清洗、分片和 gzip：官方 `aux/latest.json` 锁定 URL/文件名/时间/SHA-256，AOT DataBuilder 原子生成兼容 v1 客户端读取的 schema-v2 Subject/Episode/关系 assets、manifest 和离线包；关系只保留双端均为动画 Subject 的记录，原始 `relation_type` 保留供 P3 前传回溯。
- [x] 建立每日检查 + 手动触发 GitHub Action：每日 23:00 UTC 检查官方 Archive，亦支持 `workflow_dispatch`；只读权限构建并上传短期 Actions artifact，不误发到主程序仓库。
- [x] 建立数据唯一性、引用完整性、数量下限和确定性测试：重复 Subject/Episode ID、输出 Episode→Subject 引用、生产 30000/300000 下限、字节确定性及失败零暴露均有契约测试。
- [~] AnimeGoNetData 不可变 Release 功能及门禁已生成但外部仓库未验证：DataBuilder 已确定性生成覆盖 manifest/在线资产/离线 ZIP 的 `SHA256SUMS`；每日 Action 只向独立 `AnimeGoNetData` 仓库创建 draft，逐字节复用或补齐 draft 资产，远端完整复验后才发布并更新 GitHub latest 指针，已发布 tag 永不覆盖。2026-08-11 已修复真实 workflow 中关系数量参数缺少 Bash 续行的阻断，提取 shell 语法与 DataBuilder 9/9 通过；外部仓库变量、最小权限 token 和首次真实 Release 仍待仓库所有者配置/验收。
- [x] AnimeGoNet 实现检查更新、流式下载、校验和 staging SQLite 导入：schema v28 已加入版本、运行审计、独立 staging 与版本化 Bangumi Archive 表，schema v42 新增关系 staging/活动表；本地包导入会先验压缩文件大小/SHA-256，再以有界单行缓冲流式解 gzip/JSONL，校验字段、顺序、分片范围、计数、唯一 ID、Episode 引用与关系双端引用。schema v29 记录检查/下载/导入阶段与已验证下载目录；HTTP 使用 headers-first、有界 manifest 和 64 KiB 流式 asset 下载，逐资产验证长度/SHA-256 后才原子移动到托管包目录。schema v43 增加单文件 EP 最近日期补判的可追踪证据来源。
- [x] 实现事务切换、上版保留、失败回滚和离线手工导入：存储核心原子切换 active/previous、保留 2–10 版、支持显式回滚和同版本不可变/幂等；离线 ZIP API/WebUI 只接受根目录 `manifest.json + 声明资产`，流式落盘后逐条验证路径、长度、SHA-256、gzip/JSONL、数量和引用，再进入相同事务导入。全部失败路径保持旧 active 并清理 partial。
- [x] Web UI 增加数据版本、更新时间、检查/更新/回滚状态：静态 TypeScript 页面已接入状态刷新、手动检查、仅下载、下载并导入、已下载包延后导入和上一版回滚；显示调度策略、传输字节进度、稳定失败码、active/previous、已安装版本与本地下载包，未配置 manifest 或无可回滚版本时禁用对应动作。
- [x] 分别验证关闭调度、仅检查、自动下载待确认、自动导入和失败保留旧版：调度插件按 `auto_download`/`auto_import` 映射三种动作；单元/SQLite/API/浏览器测试覆盖仅检查、只下载、延后手动导入、自动导入、HTTP/坏资产失败保持旧 active、配置即时读取、Cron 增改删/失败回滚、环境锁、Web 表单和静态生产资源。
