# AnimeGo `develop` → AnimeGoNet 1:1 移植清单

本清单把 `wetor/AnimeGo@c7475dfc55a374cd0dd08821bf17125dab1e3145` 的可观察业务面映射到 AnimeGoNet 模块。状态只在对应测试证据通过后更新；`保留` 表示要求行为等价，`替换` 表示用户已确认的实现替换，`例外` 表示明确不移植。

状态：`基线` 已盘点、`待实现` 尚无 .NET 实现、`进行中` 已有未完成实现、`已验证` 验收通过、`例外` 经确认排除。

## 入口、配置与基础设施

| 上游路径/行为 | AnimeGoNet 目标 | 类型 | 状态 | 验收证据 |
|---|---|---:|---:|---|
| `cmd/animego`：启动、退出、信号 | `AnimeGoNet.App` composition root | 保留 | 进行中 | JIT/Kestrel 与 win-x64 NativeAOT 进程 smoke 已通过；SIGTERM/CTRL+C 待验证 |
| `cmd/plugin` | C# 内置插件测试与未来 `PluginTool` | 替换 | 待实现 | 协议/fixture tests |
| `configs/default.go`、`models.go` | `Configuration` 强类型模型与默认值 | 保留+扩展 | 进行中 | Docker/Native 默认配置 tests 已通过；旧 YAML parity 待实现 |
| `configs/check.go`、`init.go` | 配置校验、目录初始化 | 保留+扩展 | 进行中 | 路径/下载器/目录边界 tests 已通过；完整旧配置校验待实现 |
| `configs/update.go`、`version/v_*` | 1.1.0→1.7.1 迁移链 | 保留 | 待实现 | 12 份历史 YAML golden |
| `configs/utils.go` 环境变量 | 部署配置环境变量覆盖 | 保留 | 待实现 | precedence/redaction tests |
| `assets/assets.go` | 编译期嵌入静态 WebUI/默认资源 | 替换 | 已验证 | 静态资源随 win-x64 NativeAOT 产物发布并通过 HTTP smoke |
| Python 资源释放与 gpython | C# 内置实现、显式编译期注册 | 例外 | 例外 | 启动无 Python；兼容别名诊断 |
| `internal/constant`、`exceptions` | 强类型常量、稳定错误码 | 保留 | 待实现 | domain tests |
| `internal/logger` | `Microsoft.Extensions.Logging` | 替换 | 待实现 | redaction/stream tests |

## 输入、网络与数据源

| 上游路径/行为 | AnimeGoNet 目标 | 类型 | 状态 | 验收证据 |
|---|---|---:|---:|---|
| `internal/pkg/request` | AOT-safe `HttpClient` pipeline | 保留 | 待实现 | host/proxy/retry fake-server tests |
| `internal/pkg/torrent` | torrent/magnet/bencode parser | 保留 | 进行中 | 严格v1 Bencode、原始info-hash、单/多文件与安全staging已验证；magnet和4个上游fixture parity待实现 |
| `internal/animego/feed/rss.go` | RSS URL/file/raw parser | 保留 | 已验证 | 5 MiB 有界 raw/file/可注入 URL 读取、首个 enclosure、缺失跳过、非法 length=0、Mikan pubDate 原文与带偏移规范值、稳定失败码与安全 XML tests 已通过；`/api/rss` 已接入 |
| `anisource/mikan` | Mikan 页面/RSS、`mikanid`、groupid | 保留+扩展 | 进行中 | RSS source URL/channel link 的 path/query 正整数 mikanid cases，以及 Episode HTML `.mikan-rss` 的 `bangumiId/subgroupid` 上游 fixture、容错与失败码已通过；安全抓取、缓存、持久化待实现 |
| `anisource/bangumi` | Bangumi Subject/Episode/关系 | 保留 | 进行中 | Subject/关系及 Episode v0 source-generated DTO、User-Agent、分页/容量上限、身份/日期校验、安全失败分类、前传稳定遍历、普通 EP 日期候选与自动编排 fake tests 已通过；SQLite cache 待实现 |
| `anisource/themoviedb` | TMDB Series/Season/Episode | 保留+扩展 | 进行中 | 上游 discover 参数、Series季度摘要、四步后缀正则、UTF-8 byte SimilarText/0.75、普通季度/90天日期选择、AOT DTO、API key/Bearer、zh-CN→原名回退、三级官方端点验证、安全 failure taxonomy、Bangumi 日期候选与自动 Series/Season/Episode worker tests 已通过；cache 待实现 |
| Bangumi archive/cache | SQLite-backed archive refresh | 替换存储 | 待实现 | archive fixture/migration tests |
| 外部 Mikan/U2/TTG 调用 | `/api/v1/ingest` + Mikan legacy adapter | 扩展 | 进行中 | 统一校验、版本化 SourceProfile 路由、逐项结果、legacy contract、安全Torrent staging及后台 qB dispatch 已验证；真实双实例/container E2E待实现 |

## 解析、规则与元数据编排

| 上游路径/行为 | AnimeGoNet 目标 | 类型 | 状态 | 验收证据 |
|---|---|---:|---:|---|
| `internal/animego/parser` | 标题/季度/EP/字幕组解析 | 保留 | 进行中 | Go ParseEp 整数模式与安全小数/特别篇分类已由 NativeAOT C# parser 覆盖；Torrent 文件入库和 Mikan RSS title 最后可靠标记均有独立 tests，完整 parser fixtures/字幕组待实现 |
| `builtin_parser.py` | 编译期 C# parser | 替换 | 待实现 | Python differential fixtures |
| `Auto_Bangumi/raw_parser.py` | C# 1:1 文件 EP 候选解析 | 替换 | 待实现 | 原脚本 differential tests |
| `internal/animego/filter` | 有序规则管理器 | 保留+扩展 | 待实现 | 顺序、skip、异常 tests |
| `mikan_tool.py` `Filiter0..4` | 内置 C# MikanTool | 替换 | 进行中 | pure differential、schema v15、legacy config API、Episode identity、schema v16 audit，以及安全页面抓取/批内缓存/真实 RSS 前置执行已验证；WebUI 管理与原油猴浏览器 E2E 待实现 |
| RSS 黑白名单→有序规则组 | `MikanRssRuleEngine` | 扩展 | 进行中 | schema v13 规则、API/WebUI、有界 RSS、来源 EP、schema v14/16 审计、legacy filter、`/api/rss` 及 winner→统一 staging 已验证；过滤 WebUI历史/回滚待实现 |
| Mikan 人工规则 | `MikanWorkMetadataRule` | 扩展 | 已验证 | 作品级共享、乐观并发、最高优先级 Series/Season/EP Offset TMDB 验证、无效阻断、显式重试及可选 `sample_source_episode` 保存前 Series→Season→目标 Episode 预验证 tests 已通过 |
| `mikanid+groupid` offset 学习 | SQLite evidence/trusted cache | 扩展 | 已验证 | 默认关闭、3 个不同 EP 建立信任/冲突撤销、已验证 Episode 自动学习、AI 前任务级命中、零 AI/零 TMDB Episode 调用、本地 completion、Learning/Trusted/ConflictReset API/WebUI 与自动状态安全清理已通过 |
| TMDB 季度失败链 | Skip=4→Backtrace=3→Title=2→First=1 | 扩展 | 进行中 | Skip 早停、前传多层/多候选/缺日期/防环/错误降级、Title 优先于 First、日期直接命中 timeline tests 已通过；关系网络重试与 live fixture 待实现 |
| AI 季度/EP 匹配 | 独立默认关闭、600 秒超时 | 扩展 | 进行中（任务级契约、OpenAI-compatible HTTP、本地 MCP、TMDB 二次验证、季度 AI、后置 EP-AI、Mikan pubDate 内部证据及 Bangumi 普通 EP 候选门控、跨季度逐文件状态已完成） | fake AI/MCP/TMDB 已覆盖 Skip→Backtrace→AI→Title→First、EP/字幕/Other、跨季度视频与关联字幕、无法归属文件安全拒绝、顺序/Season 0/重复目标/身份越界/429/认证/网络失败、人工规则抑制、实际文件数、31 天日期窗口与通用 AI 降级；后续发布二进制 fake AI HTTP 端到端 smoke |
| 特别篇/小数 EP | 已知季度 `Other`，不伪造整数 EP | 扩展 | 进行中 | 48.5 与 SP/OVA/OAD/PV/NCOP/NCED/Menu/S00 已阻止形成普通整数候选，并在 Season 确认后持久化 Other 原因；实际整理路径待实现 |

## 存储与业务状态

| 上游路径/行为 | AnimeGoNet 目标 | 类型 | 状态 | 验收证据 |
|---|---|---:|---:|---|
| `pkg/cache/bolt` | SQLite KV/TTL 显式 SQL | 替换 | 进行中 | schema/migration 已验证；KV/TTL API 待实现 |
| `.bolt` 二进制直接读取 | 可选 JSON 导出/导入 | 例外 | 例外 | migration report |
| `pkg/dirdb` | SQLite library tables + NFO | 替换 | 待实现 | scan/upsert/recovery tests |
| 上游下载/解析实体 | 显式领域模型与 source-generated JSON | 保留+扩展 | 进行中 | ingest command/response 和 JSON context 已验证；其余模型待实现 |
| TMDB EP 完成记录 | `(series,season,episode)` 全局去重 | 扩展 | 进行中 | SQLite 唯一约束、并发完成写入、逐文件 claim、同任务字幕共享、跨任务完成/进行中精确跳过及失败释放已验证；alias/delete 和 qB 文件 priority 待实现 |
| TMDB 完全失败记录 | `tmdbid=0` + bgmid + 待补全 | 扩展 | 进行中 | 权威 SemanticNoMatch 白名单、AI 优先恢复、有效 bgmid/季度门禁、`anime_series(tmdbid=0)`、无伪造 EP 的 Other 整理、根级 NFO、fallback completion、下载恢复前 claim/完成与进行中重复早停、显式失败释放、待补全 summary/detail API 与只读 UI 已验证；Bangumi Episode ID scope、人工恢复和规范 completion 合并待实现 |
| 元数据解析尝试 | failure kind/reason/timeline | 扩展 | 待实现 | persistence/retry tests |
| 四类删除 | 业务/下载器任务/源文件/媒体文件 | 扩展 | 进行中 | 四类独立 flags/schema 已建；preview/executor 待实现 |

## 下载、整理与通知

| 上游路径/行为 | AnimeGoNet 目标 | 类型 | 状态 | 验收证据 |
|---|---|---:|---:|---|
| `internal/client/qbittorrent` | 多命名 qBittorrent adapter | 保留+扩展 | 进行中 | Cookie会话、命名实例、paused add、SourceProfile category/static tags/seedingTimeLimit 不可变快照、同hash接管再暂停、确认接收、AOT-safe file list/filePrio、逐文件去重后恢复、全重复安全移除、download job、租约恢复、按实例单在途轮询/熔断和离线 stale 快照已验证；portable v5.2.3 登录/list/路径 smoke 已通过，动态元数据 tag 与真实容器 file-priority/reconnect 待实现 |
| `internal/client/transmission` | Unsupported diagnostic only | 例外 | 例外 | migration diagnostic test |
| `internal/animego/downloader` | 持久化任务状态机 | 保留+扩展 | 进行中 | SQLite schema v10 另含 media organization job租约、逐文件operation、独立cleanup重试与完成记录事务门禁；qB状态/paused preparation/不可变路径/实例故障恢复 tests 已通过，实际整理 worker 待接入 |
| `clientnotifier` | 下载/做种/完成事件编排 | 保留 | 进行中 | qB下载完成与做种结束分阶段驱动四种策略；link/link_delete 先发布媒体后等待 Complete，wait_move 等待 Complete，独立 deleteFiles=false cleanup 与崩溃恢复 tests 已通过；真实容器状态转换待实现 |
| `renamer` 与 rename Python | C# 整理器 | 替换 | 进行中 | C# TMDB canonical path planner、跨平台名称清洗和 Other 路径 tests 已通过；字幕/NFO/持久化 worker 待实现 |
| `link/link_delete/move/wait_move` | 跨平台文件策略 | 保留 | 进行中 | 四策略已接入不可变 route snapshot：link/link_delete 的 NativeAOT 硬链接、做种门控、安全源文件删除，move/wait_move 的安全移动、逐文件/NFO/completion/cleanup 持久化与临时真实 FS tests 已通过；跨容器同 inode 与跨卷失败 E2E 待实现 |
| Mikan 默认整理 | `move` | 扩展默认 | 已验证 | 默认profile、paused preparation、真实临时文件 move/NFO/completion 与 fake-qB deleteFiles=false cleanup flow tests |
| 字幕整理 | EP 绑定、重命名、保留语言后缀 | 扩展 | 已验证 | 同stem/唯一来源EP、歧义Other、多语言/default/forced/SDH、ass/srt/idx/sub、单TMDB请求/claim/completion及真实临时文件move tests |
| Docker 路径映射 | `/data`、`/download/incomplete`、`/download/anime` | 扩展 | 进行中 | 容器配置、Compose 共享卷和 CI smoke 已建立；Docker runner 实跑待验收 |

## 调度、HTTP API 与 WebUI

| 上游路径/行为 | AnimeGoNet 目标 | 类型 | 状态 | 验收证据 |
|---|---|---:|---:|---|
| `internal/schedule` 六字段 Cron | HostedService scheduler | 保留 | 待实现 | clock/cancel/startup tests |
| `/ping`、`/sha256` | Minimal API compatibility endpoints | 保留 | 已验证 | 实际 Kestrel contract tests + win-x64 NativeAOT smoke |
| `/api/rss` | Mikan legacy → unified ingest | 保留内部替换 | 已验证 | 上游 JSON、精确 ep_links、legacy envelope/message、安全 feed/Episode 获取、Filiter0..4、批内 identity cache、失败隔离、新优选和 winner 原子 staging Kestrel tests |
| `/api/plugin/config` | C# built-in rule/config adapter | 保留语义 | 已验证 | 原请求名与 Base64 JSON、HTTP 200 + code 200/300、成功消息、等价别名、完整 SQLite replacement/revision/source、无 Python 文件 Kestrel tests |
| `/api/config` | typed deployment config | 保留+扩展 | 进行中 | 脱敏生效值 GET、safe editable desired projection、版本化 PUT/DELETE、Web 编辑/恢复、TMDB 密钥三态、application.private.json 原子写入/0600/重启应用与鉴权 tests 已通过；配置来源/环境变量 precedence 待实现 |
| `/api/bolt*` | compatibility view over SQLite | 替换 | 待实现 | response contract tests |
| `/api/download/manager` | legacy Mikan → unified ingest | 保留内部替换 | 已验证 | Kestrel contract 使用同一规范化/路由/持久化路径并保留 legacy envelope |
| `/websocket/log` | AOT-safe WebSocket logs | 保留 | 待实现 | auth/stream/cancel tests |
| 新管理 API | sources/downloaders/rules/anime/delete/status | 扩展 | 进行中 | status、统一 ingest、downloads、metadata task、SourceProfile CRUD/引用保护/category/tags/做种/路由预览、下载器脱敏投影/凭据只写/连接与路径测试及四类删除 API 已实现；anime CRUD 与 OpenAPI 待实现 |
| `internal/web/static` | 静态 TypeScript/HTML/CSS WebUI | 替换+扩展 | 进行中 | HTML/CSS/JS Kestrel tests + AOT smoke 已通过；运行配置私密覆盖编辑/恢复、下载/元数据面板、SourceProfile 版本化 CRUD 与下载器编辑器使用安全 DOM API，作品库等完整管理 UI 与发布镜像浏览器 E2E 待实现 |

## 构建、发布与平台

| 上游行为 | AnimeGoNet 目标 | 类型 | 状态 | 验收证据 |
|---|---|---:|---:|---|
| Go release workflows | .NET 10 build/test | 替换 | 进行中 | Windows/Linux/macOS Actions 已建立且 YAML 通过解析；远端运行待验收 |
| 多架构发布 | win-x64/win-arm64/linux-x64/linux-arm64/osx-arm64 | 替换矩阵 | 进行中 | 五 RID NativeAOT 矩阵已建立；win-x64 本机 publish/smoke 已通过 |
| MIPS/386/macOS x64 | 不在首版 RID | 例外 | 例外 | 文档化 |
| Go Dockerfile | NativeAOT runtime image | 替换 | 进行中 | amd64/arm64 Buildx 与容器 smoke 已建立；本机无 Docker，待 CI 实跑 |
| 嵌入资源 | AOT 静态资源与配置模板 | 保留语义 | 已验证 | win-x64 published binary 静态 WebUI smoke |

## 提交门禁

每个从“待实现”转为“已验证”的模块必须在独立提交中包含：模块测试、执行命令与结果、使用的上游 fixture、NativeAOT 验证结论、已批准偏差。任何映射变化先更新本清单，再实现代码。
