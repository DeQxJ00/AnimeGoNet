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
| `internal/animego/feed/rss.go` | RSS URL/file/raw parser | 保留 | 待实现 | 5 个 RSS fixture parity |
| `anisource/mikan` | Mikan 页面/RSS、`mikanid`、groupid | 保留+扩展 | 待实现 | Mikan fixtures + URL cases |
| `anisource/bangumi` | Bangumi Subject/Episode/关系 | 保留 | 待实现 | Bangumi fixtures |
| `anisource/themoviedb` | TMDB Series/Season/Episode | 保留+扩展 | 进行中 | 上游 discover 参数、Series季度摘要、四步后缀正则、UTF-8 byte SimilarText/0.75、普通季度/90天日期选择、AOT DTO、API key/Bearer、zh-CN→原名回退、三级官方端点验证和安全 failure taxonomy tests 已通过；cache/解析持久化待实现 |
| Bangumi archive/cache | SQLite-backed archive refresh | 替换存储 | 待实现 | archive fixture/migration tests |
| 外部 Mikan/U2/TTG 调用 | `/api/v1/ingest` + Mikan legacy adapter | 扩展 | 进行中 | 统一校验、路由、逐项结果、legacy contract与安全Torrent staging已验证；worker自动调度待实现 |

## 解析、规则与元数据编排

| 上游路径/行为 | AnimeGoNet 目标 | 类型 | 状态 | 验收证据 |
|---|---|---:|---:|---|
| `internal/animego/parser` | 标题/季度/EP/字幕组解析 | 保留 | 待实现 | parser fixtures/golden |
| `builtin_parser.py` | 编译期 C# parser | 替换 | 待实现 | Python differential fixtures |
| `Auto_Bangumi/raw_parser.py` | C# 1:1 文件 EP 候选解析 | 替换 | 待实现 | 原脚本 differential tests |
| `internal/animego/filter` | 有序规则管理器 | 保留+扩展 | 待实现 | 顺序、skip、异常 tests |
| `mikan_tool.py` `Filiter0..4` | 内置 C# MikanTool | 替换 | 待实现 | legacy differential tests |
| RSS 黑白名单→有序规则组 | `MikanRssRuleEngine` | 扩展 | 进行中 | blacklist-first、whitelist、lowercase、旁路、短路和 stable-order tests 已通过；RSS 编排/持久化待实现 |
| Mikan 人工规则 | `MikanWorkMetadataRule` | 扩展 | 待实现 | 最高优先级/共享 tests |
| `mikanid+groupid` offset 学习 | SQLite evidence/trusted cache | 扩展 | 待实现 | 3 个不同 EP/冲突撤销 tests |
| TMDB 季度失败链 | Skip=4→Backtrace=3→Title=2→First=1 | 扩展 | 待实现 | 策略 timeline tests |
| AI 季度/EP 匹配 | 独立默认关闭、600 秒超时 | 扩展 | 待实现 | fake server + TMDB 二次验证 |
| 特别篇/小数 EP | 已知季度 `Other`，不伪造整数 EP | 扩展 | 待实现 | 48.5/Specials fixture |

## 存储与业务状态

| 上游路径/行为 | AnimeGoNet 目标 | 类型 | 状态 | 验收证据 |
|---|---|---:|---:|---|
| `pkg/cache/bolt` | SQLite KV/TTL 显式 SQL | 替换 | 进行中 | schema/migration 已验证；KV/TTL API 待实现 |
| `.bolt` 二进制直接读取 | 可选 JSON 导出/导入 | 例外 | 例外 | migration report |
| `pkg/dirdb` | SQLite library tables + NFO | 替换 | 待实现 | scan/upsert/recovery tests |
| 上游下载/解析实体 | 显式领域模型与 source-generated JSON | 保留+扩展 | 进行中 | ingest command/response 和 JSON context 已验证；其余模型待实现 |
| TMDB EP 完成记录 | `(series,season,episode)` 全局去重 | 扩展 | 进行中 | SQLite 唯一约束与并发完成写入已验证；claim/逐文件待实现 |
| TMDB 完全失败记录 | `tmdbid=0` + bgmid + 待补全 | 扩展 | 进行中 | schema 强制 bgmid/待补全；非网络门禁/NFO/UI 待实现 |
| 元数据解析尝试 | failure kind/reason/timeline | 扩展 | 待实现 | persistence/retry tests |
| 四类删除 | 业务/下载器任务/源文件/媒体文件 | 扩展 | 进行中 | 四类独立 flags/schema 已建；preview/executor 待实现 |

## 下载、整理与通知

| 上游路径/行为 | AnimeGoNet 目标 | 类型 | 状态 | 验收证据 |
|---|---|---:|---:|---|
| `internal/client/qbittorrent` | 多命名 qBittorrent adapter | 保留+扩展 | 进行中 | Cookie会话、命名实例、paused add、同hash幂等、确认接收、download job、租约恢复、按实例单在途轮询和离线 stale 快照已验证；portable v5.2.3 登录/list/路径 smoke 已通过，真实容器/file-priority/reconnect 待实现 |
| `internal/client/transmission` | Unsupported diagnostic only | 例外 | 例外 | migration diagnostic test |
| `internal/animego/downloader` | 持久化任务状态机 | 保留+扩展 | 进行中 | SQLite schema v5 持久化 qB 规范状态、进度、容量、速度、ETA、Seeds/Peers、stale/revision；实例故障隔离和恢复 tests 已通过，整理阶段待实现 |
| `clientnotifier` | 下载/做种/完成事件编排 | 保留 | 待实现 | notifier parity tests |
| `renamer` 与 rename Python | C# 整理器 | 替换 | 待实现 | upstream rename fixtures |
| `link/link_delete/move/wait_move` | 跨平台文件策略 | 保留 | 待实现 | FS integration/rollback tests |
| Mikan 默认整理 | `move` | 扩展默认 | 待实现 | config + completed flow |
| 字幕整理 | EP 绑定、重命名、保留语言后缀 | 扩展 | 待实现 | ass/srt/idx/sub cases |
| Docker 路径映射 | `/data`、`/download/incomplete`、`/download/anime` | 扩展 | 进行中 | 容器配置、Compose 共享卷和 CI smoke 已建立；Docker runner 实跑待验收 |

## 调度、HTTP API 与 WebUI

| 上游路径/行为 | AnimeGoNet 目标 | 类型 | 状态 | 验收证据 |
|---|---|---:|---:|---|
| `internal/schedule` 六字段 Cron | HostedService scheduler | 保留 | 待实现 | clock/cancel/startup tests |
| `/ping`、`/sha256` | Minimal API compatibility endpoints | 保留 | 已验证 | 实际 Kestrel contract tests + win-x64 NativeAOT smoke |
| `/api/rss` | Mikan legacy → unified ingest | 保留内部替换 | 待实现 | AnimeGoHelper unchanged test |
| `/api/plugin/config` | C# built-in rule/config adapter | 保留语义 | 待实现 | legacy response contract |
| `/api/config` | typed deployment config | 保留+扩展 | 待实现 | auth/status/JSON tests |
| `/api/bolt*` | compatibility view over SQLite | 替换 | 待实现 | response contract tests |
| `/api/download/manager` | legacy Mikan → unified ingest | 保留内部替换 | 已验证 | Kestrel contract 使用同一规范化/路由/持久化路径并保留 legacy envelope |
| `/websocket/log` | AOT-safe WebSocket logs | 保留 | 待实现 | auth/stream/cancel tests |
| 新管理 API | sources/downloaders/rules/anime/delete/status | 扩展 | 进行中 | status、统一 ingest 与只读 downloads 投影已实现；CRUD/delete/OpenAPI 待实现 |
| `internal/web/static` | 静态 TypeScript/HTML/CSS WebUI | 替换+扩展 | 进行中 | HTML/CSS/JS Kestrel tests + AOT smoke 已通过；下载状态卡片使用安全 DOM API 展示进度和实例离线状态，完整管理 UI/浏览器 E2E 待实现 |

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
