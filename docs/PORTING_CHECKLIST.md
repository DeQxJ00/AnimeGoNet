# AnimeGo `develop` → AnimeGoNet 1:1 移植清单

本清单把 `wetor/AnimeGo@c7475dfc55a374cd0dd08821bf17125dab1e3145` 的可观察业务面映射到 AnimeGoNet 模块。状态只在对应测试证据通过后更新；`保留` 表示要求行为等价，`替换` 表示用户已确认的实现替换，`例外` 表示明确不移植。

状态：`基线` 已盘点、`待实现` 尚无 .NET 实现、`进行中` 已有未完成实现、`已验证` 验收通过、`暂缓` 表示所有者明确移出首版且保留的代码不构成支持承诺、`未验证` 表示功能/门禁已生成但对应平台或外部发布尚无完整运行证据且不声称成功、`例外` 经确认排除。

## 入口、配置与基础设施

| 上游路径/行为 | AnimeGoNet 目标 | 类型 | 状态 | 验收证据 |
|---|---|---:|---:|---|
| `cmd/animego`：启动、退出、信号 | `AnimeGoNet.App` composition root | 保留 | 未验证 | 固定 5 秒停止期限，活动 qB Worker/调度/WS/配置热应用与 RSS 清理的宿主取消传播已验证；win-x64 与 Ubuntu CT linux-x64 NativeAOT 的 SIGTERM/句柄/SQLite 保留通过，linux-arm64、macOS arm64 与 CTRL+C 仍未验证 |
| `cmd/plugin` | `AnimeGo.PluginTool` validate/run/pack | 替换 | 已验证 | 严格 fixture/config/typed-result tests、确定性 ZIP、真实 NativeAOT 插件进程 smoke |
| `configs/default.go`、`models.go` | `Configuration` 强类型模型与默认值 | 保留+扩展 | 已验证 | `configs` 全部生产文件/导出符号与 `config_test.go` 入口由机器清单锁定；Docker/Native 路径、Web、双 qB、Mikan move、元数据/重试/缓存/调度均为强类型安全默认。12 份固定上游 YAML 迁移，旧 Mikan feed 的 display name、带 passkey URL、Cron 与 enable 落入 SourceProfile/SQLite；旧字段按保留映射、业务替换或明确例外分类，无 Python/Transmission/旧不安全默认泄漏 |
| `configs/check.go`、`init.go` | 配置校验、目录初始化 | 保留+扩展 | 已验证 | 首次创建使用 CreateNew/0600、旧配置先完整解析与规范化再备份/原子替换；路径边界、Web、qB URL/类型/ID、来源唯一 ID/路由/Host/Cookie/策略、TMDB/Bangumi/AI/Torrent/调度/数据更新均 fail-closed；旧 `refresh_second` 最小值行为由独立 HostedService 节奏替换 |
| `configs/update.go`、`version/v_*` | 1.1.0→1.7.1 迁移链 | 保留 | 已验证 | 固定 `develop@c7475df` 的 12 份历史 YAML 均以 SHA-256 锁定并迁移到规范 1.7.1；只接受上游 13 个精确版本，范围内伪版本在备份/写入前拒绝；原字节备份、原子替换、幂等、无备份开关与非 qB fail-closed tests |
| `configs/utils.go` 环境变量 | 部署配置环境变量覆盖 | 保留 | 已验证 | 上游全部 `ANIMEGO_*`、规范嵌套键、扁平键和命令行按实际 Provider 层级解析，跨别名保持命令行→环境→YAML；三路径、命名下载器、SourceProfile、统一 AI 旧双键和显式空值均有冲突层 tests。应用全部可编辑字段、下载器实例与来源字段投影环境/命令行 `locked_fields`，API/WebUI 拒绝越级改写且私有 JSON/SQLite 不固化部署凭据；受鉴权配置页按本地易用要求回填配置值，错误与日志仍不回显；Web 安全默认、标准 URL 覆盖和真实 Kestrel tests 已验证 |
| `assets/assets.go` | 编译期嵌入静态 WebUI/默认资源 | 替换 | 已验证 | 静态资源随 win-x64 NativeAOT 产物发布并通过 HTTP smoke |
| Python 资源释放与 gpython | C# 内置实现、显式编译期注册 | 例外 | 例外 | 启动无 Python；兼容别名诊断 |
| `assets/plugin/feed/parser/filter/rename/schedule` builtin | 五类 C# 内置插件 | 替换 | 已验证 | 同一显式目录；legacy RSS、Mikan filter/parser、媒体整理、staging schedule 委托 tests；固定 develop 的 59 个 plugin/fixture/Go 测试入口由机器清单逐文件映射并锁定 SHA-256 与证据目标，5 个真实 RSS fixture 和 filter 13/4/9/1 结果直接复现；无 Python/DLL 动态加载 |
| 外部可执行插件包 | RID-specific C# process package | 新增 | 已验证 | manifest、JSON Lines、真实进程环境隔离、惰性复用/退避/reset、配置 API/UI、六类 typed adapter、stderr、SDK/模板及 AOT-safe PluginTool 均有 fake/真实进程/五 RID 门禁；Ubuntu 24.04 x86_64 CT 已实际验证 linux-x64 NativeAOT 插件只读包、非 root UID、可写 plugin-data 和启用→统一导入→禁用回退 |
| parser 首个启用实现、filter 顺序串联 | `TitleParserManager` / `OrderedFeedFilterManager` | 保留 | 已验证 | 首个/显式 parser 不 fallback；filter accepted 逐级传递、错误短路、无效/重复 index、空链 tests；Mikan RSS 生产链回归 |
| `internal/models`、`internal/constant`、`internal/exceptions`、`pkg/exceptions` | 强类型领域模型、常量、稳定错误语义 | 保留+替换 | 已验证 | 固定上游提交的机器清单逐文件/逐导出类型穷尽映射到闭合 enum/record、SQLite 状态机和编译期常量；测试校验 HEAD、文件/类型无漏项、每个替代目标真实存在。`IStableError`/`StableErrorSemantic` 保留 Go marker error 的跨包装识别，RSS/Torrent/Mikan HTML/Data manifest/Cron 解析公开统一 `ParseFailed`；正常重复/不存在使用显式结果。完整依据见 `docs/DOMAIN_MODEL_MAPPING.md` |
| `internal/logger` | `Microsoft.Extensions.Logging` | 替换 | 已验证 | 编译期 provider fan-out、统一 URL/凭据脱敏、有界 WebSocket stream，以及 `data_path/logs/animego.log` 的 Info+、2 MiB/14份/14天滚动文件均通过并发、轮转、生命周期和 NativeAOT smoke |

## 输入、网络与数据源

| 上游路径/行为 | AnimeGoNet 目标 | 类型 | 状态 | 验收证据 |
|---|---|---:|---:|---|
| `internal/pkg/request` | AOT-safe `HttpClient` pipeline | 保留+扩展 | 已验证 | 真实 loopback socket已验证直连时固定已校验 IP、原 URI Host、固定 User-Agent、禁止自动 redirect 和流式响应；生产 SSRF 策略继续拒绝 loopback/private。唯一 `outbound_proxy.url + hosts` 支持精确域名/子域通配、HTTP(S)/SOCKS5、未命中直连并覆盖 Mikan/TMDB/Bangumi/封面/AI/MCP/数据更新；qB 与固定 AniDB 查询直连。代理 Torrent 仍逐跳执行 host/DNS/redirect 门禁，但由 forward proxy 解析目标，不声称 DNS 连接钉死。逐次超时、可配置额外重试/间隔、请求重建、取消传播及 429/5xx 与非重试错误边界已通过故障注入。Mikan SourceProfile 私有 Cookie 在 RSS/Torrent 请求中只发往原始 Host，跨 Host redirect 剥离；除受鉴权配置读取端点按需回填外，错误、日志和业务响应不回显。 |
| `pkg/utils`、`pkg/xpath`、`internal/models/utils.go`、TMDB date helpers | 纯函数与安全平台抽象 | 保留+替换 | 已验证 | `StableHash` 固定 UTF-8 lowercase SHA-256 并接入 access-key、ingest URL 指纹及 RSS 身份；Tag/去后缀/UTF-8 byte 相似度/日期差保持 golden parity；路径、文件名、NFO、取消等待、序列化和异常隔离使用强类型/AOT-safe 实现。无可观察业务调用的 MD5/interval helper 与仅服务 Python/反射/panic 的函数不复制，完整逐函数映射见 `docs/PURE_FUNCTION_PARITY.md` |
| `internal/pkg/torrent` | torrent/magnet/bencode parser | 保留 | 已验证 | 严格 v1 Bencode、原始 info-hash、单/多文件与安全 staging 已验证；magnet 的 40 hex/32 Base32、首个 xt/dn 和 tracker 计数已按上游 fixture 验证且不保留 tracker/passkey；固定 `c7475df` 的 4 个真实 `.torrent` 已逐项验证 info-hash、名称、总量和全部 17 个文件 |
| `internal/animego/feed/rss.go` | RSS URL/file/raw parser | 保留 | 已验证 | 5 MiB 有界 raw/file/URL 读取、真实 loopback chunked HTTP、原始 path/query/Host、首个 enclosure、缺失跳过、非法 length=0、Mikan pubDate 原文与带偏移规范值、稳定失败码与安全 XML tests 已通过；固定上游 `Mikan.xml`/`Mikan.json` 全部 13 项逐字段、`2822_370.xml`、非法 XML、缺 enclosure/非法长度共 5 个 fixture 直接通过；`/api/rss` 已接入 |
| `anisource/mikan` | Mikan 页面/RSS、`mikanid`、groupid | 保留+扩展 | 已验证 | RSS source URL/channel link 的 path/query 正整数 mikanid、Episode HTML `.mikan-rss` 的 `bangumiId/subgroupid`，以及 `/Home/Bangumi/{mikanid}` 中 `p.bangumi-info` 的可信 Bangumi Subject 链接均已覆盖上游 fixture；SourceProfile 级 `.AspNetCore.Identity.Application` Cookie 支持旧 YAML 迁移、SQLite 隔离、受鉴权配置回填、RSS/Torrent 原始 Host 注入和跨 Host redirect 剥离；SSRF 防护、严格 UTF-8/容量限制、歧义与伪造域名拒绝、schema v26/31 批次及凭据迁移、统一导入 task 持久化和失败重试 tests 已通过 |
| `anisource/bangumi` | Bangumi Subject/Episode/关系 | 保留 | 已验证 | Subject/关系及 Episode v0 source-generated DTO、User-Agent、分页/容量上限、身份/日期校验、安全失败分类、前传稳定遍历、普通 EP 日期候选、逐次超时和安全可取消重试已通过；活动 AnimeGoNet Data v2 的 SQLite Subject/完整 Episode/关系快照优先读取，v1 无关系证据时仅关系回退在线 API，其他缺失/不完整/零集未知按原规则回退，版本激活与回滚无需重启；在线 Bangumi 故障 fixture 已证明 P3 从 v2 零网络取得前作后仍完成 TMDB Series/Season 验证 |
| `anisource/themoviedb` | TMDB Series/Season/Episode | 保留+扩展 | 已验证 | 上游 discover 参数、Series季度摘要、四步后缀正则、UTF-8 byte SimilarText/0.75、普通季度日期选择、AOT DTO、API key/Bearer、zh-CN→原名回退、三级官方端点验证、安全 failure taxonomy、Bangumi 日期候选、逐次超时和安全可取消重试及自动 Series/Season/Episode worker tests 已通过；季度首播日期按确认后的 `±1` 日业务窗口匹配（有意收窄上游窗口）；实际 `TmdbClient` 随机 loopback 证明 `name` 全部清理轮次→`name_cn`→每响应全部合格 Series 的完整验证早停；Mikan 有 bgmid 时先做 Bangumi/TMDB 普通 EP `±1` 日映射，失败后仅实际单文件可用文件名 EP 与最近日期补判，最多 7 日且 TMDB EP 必须同号，最终 Episode 仍经官方 endpoint 验证；SQLite `bolt/themoviedb` 成功响应缓存按 Base URL/语言/operation 分区，144 小时默认 TTL、旧 `themoviedb_cache_hour` 迁移、WebUI 配置/锁/精确删除、无凭据/搜索词泄漏、到期/损坏/身份伪造回源及 NativeAOT source-generated JSON 已验证 |
| Bangumi archive/cache | SQLite-backed archive refresh | 替换存储 | 已验证 | 官方 `aux/latest.json`/ZIP SHA 门禁、AOT-compatible DataBuilder 的动画/正片/动画关系筛选、文本/日期/小数 EP 规范化、Subject 范围分片、确定性 schema-v2 JSONL.gz/manifest/离线 ZIP、原子输出、唯一/双端引用完整性、生产数量下限及真实 SQLite 导入已验证；v1 继续兼容，v2 关系可供 P3 离线遍历但最终 TMDB 仍在线验证；版本化下载/导入、保留/回滚和活动版本 read-through 完整串联，Actions 每日/手动构建但不自动发布到主程序仓库 |
| 外部 Mikan 调用 | `/api/v1/ingest` + Mikan legacy adapter | 扩展 | 已验证 | Mikan 统一校验、版本化 SourceProfile、legacy contract、安全 Torrent staging、后台 qB dispatch 与本机合法下载整理闭环均已验证；Ubuntu 24.04 x86_64 CT 的 NativeAOT 容器全链也已实跑通过 |
| 外部 U2 调用 | `inner_plugin_u2` 专用 API + AnimeGoHelper U2 油猴脚本 | 扩展 | 已验证 | 真实 U2 表格 DOM 读取标题/u2id/AniDB/分类；前端构造 passkey 下载 URL并强制人工确认 TV/Movie；后端独立鉴权、URL/ID 校验、SourceProfile 路由和脱敏审计测试。无站点登录、Cookie 或 RSS 自动抓取 |

## 解析、规则与元数据编排

| 上游路径/行为 | AnimeGoNet 目标 | 类型 | 状态 | 验收证据 |
|---|---|---:|---:|---|
| `internal/animego/parser` | 标题/季度/EP/字幕组解析 | 保留 | 已验证 | Go develop 的 3 个 ParseEp fixtures、扩展整数模式、小数/特别篇隔离、Mikan RSS 最后可靠标记、19 组 AutoBangumi Python 标题/季度/EP/字幕/字幕组/分辨率/来源 golden fixtures 均已覆盖；RSS 字幕组解析与 raw parser 对全部 golden 输入交叉一致，持久化前的年份/分辨率/歧义/非正片安全层已验证；任务详情把持久 RSS batch/revision/decision/groups 与实际 Torrent `file_episode_candidate` 跨请求并列审计且不返回 URL/指纹 |
| `builtin_parser.py` | 编译期 C# parser | 替换 | 已验证 | 与 raw_parser.py 共用的完整解析主体已由 `AutoBangumiRawParser` 编译期替换；无 Python 运行时，develop Python golden fixture 逐字段通过 |
| `Auto_Bangumi/raw_parser.py` | C# 1:1 文件 EP 候选解析 | 替换 | 已验证 | 19 组 develop Python golden 覆盖全部输出字段和原始 E04/EP04 不识别语义；独立安全层才拒绝年份/分辨率/歧义/非正片，并将弱数字限制为 1～9999、先排除超范围哈希再判断歧义，数据层证明只对 Mikan adapter 落候选 |
| `internal/animego/filter` | 有序规则管理器 | 保留+扩展 | 已验证 | 编译期注册过滤器按稳定顺序串行执行；上一规则仅将 accepted 候选传给下一规则；显式空链等价于上游 `skipFilter`；业务错误、无效结果与意外异常均立即停止且后续规则不执行；固定 `Mikan.xml` 直接复现上游 13 个输入、4 个 NC-Raws、9 个有效 1080p 和 inline regex 单候选结果；生产 Mikan RSS 显式使用 `mikan-tool` 链，外部过滤器必须显式启用；PluginManager 回归 tests |
| `mikan_tool.py` `Filiter0..4` | 内置 C# MikanTool | 替换 | 已验证 | pure differential、schema v15、legacy config API、Episode identity、schema v16 audit、安全页面抓取/批内缓存/真实 RSS 前置执行，以及五档 WebUI CRUD/排序、开关、可解释预览、legacy JSON 导入导出和快照回滚均已验证；发布镜像浏览器全链已在 Ubuntu CT 通过 |
| RSS 黑白名单→有序规则组 | `MikanRssRuleEngine` | 扩展 | 已验证 | schema v13 规则、API/WebUI、有界 RSS、来源 EP、schema v14/16 审计、legacy filter、`/api/rss`、winner→统一 staging，以及 schema v25 关系型历史快照和 revision 安全回滚均已验证；同一 mikanid 的多个 `UngroupedBypass` 可仅预取 Torrent 元数据，多视频文件逐 EP 通过 Bangumi/TMDB 确定性验证后作为原子覆盖集合与普通单集按 RSS title 重选，部分胜出整体回退、完整胜出压制重叠单集并复用 staging，全程零 AI；统一任务详情按 `ingest_task_id` 安全投影一个任务关联的全部历史 batch/entry ordinal 与实际执行组，重复请求不丢入口证据且不返回 URL 派生 candidate ID；首版以可键盘操作的上下移动排序代替拖拽 |
| Mikan 人工规则 | `MikanWorkMetadataRule` | 扩展 | 已验证 | 作品级共享、乐观并发、最高优先级 Series/Season/EP Offset TMDB 验证、无效阻断及可选 `sample_source_episode` 保存前 Series→Season→目标 Episode 预验证；管理 API/WebUI 已支持 revision-safe 创建/更新/禁用/清除、权威影响分类和只重置无租约失败任务的显式重匹配，已解析/已整理/完成记录/媒体文件保持不变 |
| `mikanid+groupid` offset 学习 | SQLite evidence/trusted cache | 扩展 | 已验证 | 来源 EP 明确为文件名解析 EP；默认关闭、可信门槛 1～100 可配置且默认 3、重复 EP 不计数、冲突撤销、已验证 Episode 自动学习、AI 前任务级命中、零 AI/零 TMDB Episode 调用、主视频/多语言字幕关联、fake qB priority/恢复、规范实际落盘、单一 completion、`deleteFiles=false` cleanup、Learning/Trusted/ConflictReset API/WebUI 与自动状态安全清理已通过 |
| TMDB 季度失败链 | `TMDBFailSkip=4`→`TMDBFailBacktrace=3`→`TMDBFailUseTitleSeason=2`→`TMDBFailUseFirstSeason=1` | 扩展 | 已验证 | 日文名→中文名及每名多轮清理均以完整 `tmdbid+Season` 为成功条件；P3 对每个前作重新联合搜索且可恢复不同 Series，无前传/多层/同层多候选/缺日期/回溯到底/防环/Bangumi与TMDB失败/取消等确定性 fixture 已验证，并以真实 loopback HTTP 证明 Bangumi 503、TMDB 429 重试后跨两层前作联合验证；P2 只读任务 title、P1 本地 S01 且均不验证 TMDB Season |
| AI 元数据匹配 | 单开关、单Prompt、每任务最多一次、默认关闭、600 秒超时 | 扩展 | 已验证 | fake AI/MCP/TMDB 已覆盖关闭零请求、未配置、超时、429 重试/耗尽、认证、外层/模型畸形 JSON、多个 choices 歧义、不存在 ID、文件数量冲突、单文件 AI 名称回显不影响原始 Torrent 文件身份、多文件回显乱序在联网前拒绝、MCP schema 缓存，以及 Skip→Backtrace→AI→Title→First、阶段间禁止二次调用、EP/字幕/Other、跨季度、Season 0、重复目标和身份越界；Mikan Torrent 发布日期只作 AI 参数且没有拒绝窗口；Bangumi/TMDB 季度与 EP 主日期匹配均允许 `±1` 日，单文件最近日期补判超过 7 日/编号不一致以及多文件主匹配失败均进入一次任务级 AI；AniDB 零参数固定公网连接与 IMDb external find 的 Movie 程序侧剔除已验证；五 RID NativeAOT workflow 运行发布二进制 loopback smoke，真实后台 worker 完成 fake AI 两轮→MCP 工具→fake TMDB Series/Season/Episode 验证→SQLite/API 权威状态落库，win-x64 本机已通过 |
| 特别篇/小数 EP | 已知季度 `Other`，不伪造整数 EP | 扩展 | 已验证 | 48.5 与 SP/OVA/OAD/PV/NCOP/NCED/Menu/S00 已阻止形成普通整数候选；Season 确认后未匹配视频持久化稳定 Other 原因，保留原名移动到 `Sxx/Extras`；同任务已有成功视频时，孤立字幕及其他非视频附件改用不计入 Other 的 `Extras` 分类；两者均不写 Episode completion/alias/claim 或 `Eyyy.e_json` 伪进度 |

## 存储与业务状态

| 上游路径/行为 | AnimeGoNet 目标 | 类型 | 状态 | 验收证据 |
|---|---|---:|---:|---|
| `pkg/cache/bolt` | SQLite KV/TTL 显式 SQL | 替换 | 已验证 | schema v22、`bolt`/`bolt_sub` bucket 隔离、JSON upsert/batch、绝对 TTL、惰性/批量过期清理和原子失败 tests 已通过 |
| `.bolt` 二进制直接读取 | 可选 JSON 导出/导入 | 例外 | 已验证 | 独立 Go 工具只读解码固定六个 bucket；schema-v1 包保留 JSON key/value 与绝对 TTL；.NET 有界验证、过期跳过、schema v39 IMMEDIATE 事务、内容指纹审计及重复导入不覆盖新数据；migration report 不含 key/value |
| `pkg/dirdb` | SQLite library tables + NFO | 替换 | 已验证 | 上游三层 JSON sidecar 由原子 Writer 兼容输出并在 NFO/业务 completion 前落盘；Scanner 只读取明确 sidecar、逐项隔离损坏，SQLite refresh 事务替换并审计 issue，增量 upsert 同路径覆盖不重复；崩溃遗留 atomic partial 不参与扫描且不阻塞下次写入；scan/upsert/recovery + 整理流水线 tests |
| 上游下载/解析实体 | 显式领域模型与 source-generated JSON | 保留+扩展 | 已验证 | 固定上游 `internal/models` 全文件/导出类型由 `UPSTREAM_DOMAIN_CONTRACTS.psv` 穷尽映射；来源证据、TMDB 权威身份、逐文件候选、解析 Run/Attempt、下载/整理/删除状态拆为闭合 record/enum/SQLite 状态机；测试穷尽所有公开 API DTO 与 endpoint 签名中的闭合 generic envelope，并要求 `ApiJsonContext.GetTypeInfo` 非空，因此新增 API contract 漏登记会在 JIT 阶段失败，无反射 `map[string]any` 回退 |
| TMDB EP 完成记录 | `(series,season,episode)` 全局去重 | 扩展 | 已验证 | SQLite 唯一约束、并发完成写入、逐文件 claim、同任务字幕共享、跨任务完成/进行中精确跳过及失败释放已验证；正常整理与 completion 同事务写来源 alias，RSS winner 以 IMMEDIATE 事务复查同 `mikanid+来源EP` 并保存命中证据，业务记录删除级联清除 alias 后可重新导入；qB 文件 priority 已接入下载准备 |
| TMDB 完全失败记录 | `tmdbid=0` + bgmid + 待补全 | 扩展 | 已验证 | 权威 SemanticNoMatch 白名单、AI 优先恢复、有效 bgmid、固定本地 S01 且不依赖 P2/P1、`anime_series(tmdbid=0)`、无伪造 EP 的 Other 整理、根级 NFO、fallback completion、下载恢复前 claim/完成与进行中重复早停、显式失败释放、待补全 summary/detail API/UI 已验证；Run 级 TMDB 访问确认/兜底资格/拒绝原因由列表和详情 API/WebUI 原样投影，六类非权威失败均经处理器门禁测试；唯一普通 Bangumi Episode ID 提供跨来源最高可信 scope，歧义/小数/特别篇保守降级；schema v20 恢复合并保存 alias 并标记 `DuplicateAfterResolution`；人工恢复逐项在线验证 TMDB；schema v21 在原兜底目录可恢复地重写真实 TMDB/Bangumi NFO |
| 元数据解析尝试 | failure kind/reason/timeline + 三级最终证据 | 扩展 | 已验证 | schema v32 固化 Series/Season/Episode `resolution_source + run_id + attempt_id`，SQLite 触发器拒绝跨 Run/Stage/策略伪造引用；逐文件 Episode/字幕精确 Attempt、混合证据摘要、任务/作品库 API/WebUI、失败时间线和显式 retry tests |
| 四类删除 | 业务/下载器任务/源文件/媒体文件 | 扩展 | 已验证 | schema v12 指纹预览与逐项目标冻结、四类独立选择、API/WebUI 明确确认、租约恢复 executor 和逐项状态已接入；固定 qB `deleteFiles=false`，源/媒体只删捕获根内普通文件且不递归；执行顺序、部分失败重试、缺失幂等、越界/符号链接拒绝、完成记录与 alias/claim 精确清理均有 tests，删除同 TMDB EP 不影响其他 EP |

## 下载、整理与通知

| 上游路径/行为 | AnimeGoNet 目标 | 类型 | 状态 | 验收证据 |
|---|---|---:|---:|---|
| `internal/client/qbittorrent` | 多命名 qBittorrent adapter | 保留+扩展 | 已验证 | Cookie会话、命名实例、paused add、SourceProfile category/static tags/seedingTimeLimit 不可变快照、同hash接管再暂停、确认接收、AOT-safe file list/filePrio/addTags、元数据后置动态 tag、逐文件去重后恢复、全重复安全移除、download job、租约恢复、按实例单在途轮询/熔断和离线 stale 快照已验证；portable v5.2.3、本机真实下载/整理及 Ubuntu 24.04 x86_64 CT 双实例统一投递、合法 WebSeed 下载→Bangumi/TMDB→move/NFO/sidecar→API/WebUI→qB 清理的全链门禁均通过 |
| `internal/client/transmission` | Unsupported diagnostic only | 例外 | 已验证 | `ANIMEGO_CLIENT`、显式旧配置路径和 `data_path/animego.yaml` 只读检测；`UnsupportedDownloaderType`/不可读旧配置 fail-closed，workers/registry/ingest/控制/连接探测均阻断，Web 可进入修复；AOT/API tests |
| `internal/animego/downloader` | 持久化任务状态机 | 保留+扩展 | 已验证 | SQLite schema v10 的 media organization 租约、按 Torrent 路径稳定执行的逐文件 operation、跨盘校验复制、目标冲突保全、部分完成后 pending-only 恢复、独立 cleanup 重试与完成记录事务门禁已由后台 worker 串联；schema v33 另持久化不可变做种目标、单调累计秒数、完成门禁与审计。本机真实 qB 单/多文件闭环及 Ubuntu CT 容器全链均通过 |
| `clientnotifier` | 下载/做种/完成事件编排 | 保留 | 已验证 | qB 快照同步驱动持久化 waiting/seeding/completed，link/link_delete 先发布媒体后等待做种门禁，wait_move 等门禁完成；上游完成 callback 的 `DeleteFile:true` 已明确替换为独立 `deleteFiles=false` cleanup，覆盖失败释放、新租约、实例 circuit 打开、健康探测恢复、媒体/completion 保全及成功清理；Ubuntu CT 容器状态转换通过 |
| `renamer` 与 rename Python | C# 整理器 | 替换 | 已验证 | 编译期 `anime-library` C# rename 插件完整替换 Python；TMDB 规范 EP、已确认季度 Other、字幕多语言后缀、原子 NFO/三层 sidecar、四种文件策略及 SQLite organization/cleanup 租约均由持久化 worker 串联；冲突/部分完成/重启恢复与固定 qB `deleteFiles=false` 已用真实临时文件 + fake qB tests 验证 |
| `link/link_delete/move/wait_move` | 跨平台文件策略 | 保留 | 已验证 | 四策略已接入不可变 route snapshot：link/link_delete 的 NativeAOT 硬链接、做种门控、安全源文件删除，move/wait_move 的安全移动、逐文件/NFO/completion/cleanup 持久化与临时真实 FS tests 已通过；本机真实 move 闭环通过，跨容器同 inode/跨卷 E2E 未验证 |
| Mikan 默认整理 | `move` | 扩展默认 | 已验证 | 默认profile、paused preparation、真实临时文件 move/NFO/completion 与 fake-qB deleteFiles=false cleanup flow tests |
| 字幕整理 | EP 绑定、重命名、保留语言后缀 | 扩展 | 已验证 | 同stem/唯一来源EP、歧义Other、多语言/default/forced/SDH、ass/srt/idx/sub、单TMDB请求/claim/completion及真实临时文件move tests |
| Docker 路径映射 | `/data`、`/download/incomplete`、`/download/anime` | 扩展 | 已验证 | Ubuntu 24.04 x86_64 CT 已实际验证 NativeAOT 容器、双 qB 共享 `/download`、路径探测、下载和 move 整理；linux-arm64 镜像由跨架构发布项单独跟踪 |

## 调度、HTTP API 与 WebUI

| 上游路径/行为 | AnimeGoNet 目标 | 类型 | 状态 | 验收证据 |
|---|---|---:|---:|---|
| `internal/schedule` 六字段 Cron | HostedService scheduler | 保留 | 已验证 | 纯 C# 秒级六字段 parser 支持 `?`、list/range/step、月份/星期名及标准 descriptor；Cron DOM/DOW OR、NextTime、时区/DST、StartRun、固定3次/3秒重试、热增删、并发执行、取消与 HostedService tests。目录数据库、AnimeGoNetData 与逐来源 Mikan RSS 均使用编译期内置插件；schema v36 RSS 调度已验证启动注册、CRUD 热替换、旧 revision 零网络、重叠旁路、中断恢复、失败审计和 passkey 脱敏；win-x64 NativeAOT smoke |
| `/ping`、`/sha256` | Minimal API compatibility endpoints | 保留 | 已验证 | 实际 Kestrel contract tests + win-x64 NativeAOT smoke |
| `/api/rss` | Mikan legacy → unified ingest | 保留内部替换 | 已验证 | 上游 JSON、精确 ep_links、legacy envelope/message、安全 feed/Episode 获取、Filiter0..4、批内 identity cache、失败隔离、新优选和 winner 原子 staging Kestrel tests |
| `/api/v1/rss/ingest` | 现代 Mikan RSS 手动导入 | 扩展 | 已验证 | 明确 SourceProfile、adapter 预检后再抓取、规则 revision、winner 原子 staging、错误脱敏和带 passkey URL 不回显 Kestrel tests |
| `/api/plugin/config` | C# built-in rule/config adapter | 保留语义 | 已验证 | 原请求名与 Base64 JSON、HTTP 200 + code 200/300、成功消息、等价别名、完整 SQLite replacement/revision/source、无 Python 文件 Kestrel tests |
| Mikan source adapter | `IInputSourceAdapter` + `PluginCatalog` | 替换静态选择 | 已验证 | 显式注册顺序、未知 adapter、无效插件输出和真实统一导入 normalizer tests |
| U2 source adapter | 编译期 adapter + `inner_plugin_u2` 手动入口 | 扩展 | 已验证 | 自定义 U2 SourceProfile、专用 v1 请求映射、统一 staging/路由及 API 审计测试；不静默生成默认 SourceProfile/文件策略 |
| `/api/config` | legacy deployment config + typed private overrides | 保留+扩展 | 已验证 | legacy `all/default/comment/raw` GET 与 `all/raw` PUT 保持 HTTP 200 + code 200/300、query 覆盖 body、独立 WebUI AccessKey 和“重启后应用”；JSON/Base64/YAML/版本/强类型值先在同目录隔离文件验证，通过后才以 CreateNew 保存可选原字节备份并原子替换。现代 `/api/v1/config` 返回含当前凭据的本机 editable projection，并通过明文 preview、revision PUT/DELETE、私有 0600 覆盖和 Web 两步确认管理；日志、运行状态和错误仍脱敏 |
| `/api/bolt*` | compatibility view over SQLite | 替换 | 已验证 | bucket/key 列表、JSON value/绝对 Unix TTL、HTTP 200 + code 200/300、幂等删除、`bolt_sub` 只读和 WebUI AccessKey Kestrel tests |
| `/api/download/manager` | legacy Mikan → unified ingest | 保留内部替换 | 已验证 | Kestrel contract 使用同一规范化/路由/持久化路径并保留 legacy envelope |
| `/websocket/log` | AOT-safe WebSocket logs | 保留 | 已验证 | 非 upgrade 兼容响应、直接/旧 hash 鉴权、旧帧 envelope、逐连接 pause/resume/terminate、1000 条缓存、异常命令、敏感字段脱敏、WebUI 和 win-x64 NativeAOT upgrade/control smoke 已通过 |
| 新管理 API | sources/downloaders/rules/anime/delete/status | 扩展 | 已验证 | status、统一 ingest、现代 RSS ingest、downloads、metadata task、SourceProfile CRUD/引用保护/category/tags/做种/路由预览、下载器配置回填/连接与路径测试、Mikan 人工作品规则影响/显式重匹配、TMDB 权威季度 CRUD 及四类删除 API 已实现；配置页按“本地易用优先”回填 qB、Mikan RSS/Cookie 与 TMDB/AI 凭据，应用 Access Key 仍不回传；官方 .NET 10 AOT-safe OpenAPI 覆盖全部当前路由和 12 个上游 operation |
| `internal/web/static` | 静态 TypeScript/HTML/CSS WebUI | 替换+扩展 | 已验证 | HTML/CSS/JS Kestrel tests + AOT smoke 已通过；TypeScript 7 strict ES module、同源安全的共享类型化 JSON client、确定性产物 CI，以及真实 DOM 状态/可访问性单测已建立；运行配置、手动导入、下载/元数据、待补全 TMDB、Mikan 规则、SourceProfile、下载器、作品库、opaque 缓存浏览/精确删除及实时日志页面均使用安全 DOM API；本机发布 NativeAOT Chromium 2/2 与 Ubuntu CT linux-x64 发布镜像全链 1/1 均通过 |

## 构建、发布与平台

| 上游行为 | AnimeGoNet 目标 | 类型 | 状态 | 验收证据 |
|---|---|---:|---:|---|
| Go release workflows | .NET 10 build/test | 替换 | 已验证 | Windows/Linux/macOS CI 与五 RID NativeAOT Actions 已远端通过；固定 Go 1.22.10 Linux amd64 容器的上游串行 `go test -json` 已在 Ubuntu CT 真实通过并校验原始事件/stderr/summary/SHA-256 |
| 多架构发布 | win-x64/win-arm64/linux-x64/linux-arm64/osx-arm64 | 替换矩阵 | 已验证 | 五 RID NativeAOT 矩阵及稳定/预发布标签闭环已建立；每个 RID 在上传前从实际产物和精确 NuGet graph 生成逐文件 `SHA256SUMS`、CycloneDX 1.5 SBOM 与第三方许可证清单，五份完整 artifact 再确定性打包为 ZIP 与独立校验和；五 RID publish/smoke 已由 GitHub Actions 验证，首个正式 `v1.0.0` Release 仍待标签推送 |
| MIPS/386/macOS x64 | 不在首版 RID | 例外 | 例外 | 文档化 |
| Go Dockerfile | NativeAOT runtime image | 替换 | 未验证 | amd64/arm64 Buildx 与容器 smoke 已生成，正式标签会发布带不可变 digest 和 GitHub/Sigstore 来源证明的 GHCR 多架构镜像；Ubuntu 24.04 x86_64 CT 的 linux-x64 NativeAOT 镜像及完整容器 smoke 已通过，linux-arm64 容器运行仍未验证 |
| 嵌入资源 | AOT 静态资源与配置模板 | 保留语义 | 已验证 | win-x64 published binary 静态 WebUI smoke |
| 用户迁移、插件与运维手册 | 可执行操作文档 | 新增 | 已验证 | 新安装/旧 YAML/旧 Bolt/媒体 sidecar 的隔离迁移和完整回滚，外部 C# 插件 validate/run/pack、安装/升级/reset/卸载，以及状态、停机备份、SQLite quick_check、升级恢复、四类删除和故障处置均已文档化；README 入口、相对链接、关键安全边界、Ubuntu CT 证据与剩余跨架构范围由契约测试锁定 |

## 提交门禁

每个从“待实现”转为“已验证”的模块必须在独立提交中包含：模块测试、执行命令与结果、使用的上游 fixture、NativeAOT 验证结论、已批准偏差。任何映射变化先更新本清单，再实现代码。
