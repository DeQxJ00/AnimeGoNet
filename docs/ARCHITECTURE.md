# AnimeGoNet 主程序架构与数据模型

## 1. 解决方案边界

```text
AnimeGoNet.slnx
├─ src/AnimeGoNet.Core           领域模型、规则、配置与接口；零基础设施依赖
├─ src/AnimeGoNet.Data           SQLite 连接、显式 SQL、迁移和 repository
├─ src/AnimeGoNet.App            Minimal API、静态 WebUI、DI、后台任务、进程入口
├─ src/AnimeGoNet.Plugins        编译期注册的内置 C# 实现（后续阶段）
├─ tests/AnimeGoNet.Core.Tests
├─ tests/AnimeGoNet.Data.Tests
└─ tests/AnimeGoNet.App.Tests
```

依赖只允许从外向内：`App → Data → Core`；内置插件依赖 `Core` 契约，由 `App` 显式注册。Core 不引用 ASP.NET、SQLite、文件系统或下载器实现。

上游 Go 源码位于独立的 `AnimeGo` 目录和 Git 仓库，仅作为差分 fixture 与业务行为索引；本仓库只保存 `DotnetProject` 的 C# 主程序、测试、文档和交付资产，两边不共享 Git 历史。

## 2. 配置、数据与目录真相源

- 部署配置文件保存监听地址、Access Key、`data_path`、`download_path`、`save_path`、命名 qBittorrent 实例、路径映射、TMDB/AI 连接与更新策略。
- 环境变量可以覆盖部署配置；WebUI 显示最终值与来源，被环境变量覆盖的字段只读。
- SQLite 保存来源 profile、规则、动画、任务、匹配尝试、下载/整理状态、完成记录和审计，不复制密码等部署配置。
- Docker 默认路径固定为 `data_path=/data`、`download_path=/download/incomplete`、`save_path=/download/anime`。Compose 必须把 AnimeGoNet 与 qBittorrent 的共同宿主父目录映射为同一容器内 `/download`。

`DataPath` 下的首版目录：

```text
/data
├─ animegonet.db
├─ staging/             临时 torrent；日志/API 永不显示 passkey URL
├─ cache/               TMDB/Bangumi/cover 缓存
├─ logs/
├─ backups/             配置备份
└─ plugins/             未来外部进程插件 manifest；Docker 只读
```

路径模型在启动时完成规范化并验证：所有实例下载目录必须位于全局 `download_path` 下；所有业务写入必须落在已声明根目录内；宿主/容器路径映射必须按最长前缀、目录边界匹配，禁止字符串前缀越界。

## 3. 领域聚合

### 来源与路由

- `SourceProfile`：稳定小写 ID、adapter、下载器实例 ID、元数据字段 schema、规则 revision、文件策略、category/tag、路径与做种策略。
- `DownloaderInstance`：部署配置中的命名 qBittorrent 连接；任务只保存实例 ID 和不可变路由快照，不保存明文密码。
- `IngestBatch` / `IngestItem`：统一导入命令；保存 title、脱敏 Torrent URL 指纹、source item/work ID、`mikanid`、`groupid`、可选 bgmid/anidbid/imdbid。

### 人工规则与 Mikan offset

- `MikanWorkMetadataRule`：以 `mikanid` 唯一，保存人工 bgmid、TMDB Series/Season、EP offset、版本和审计；人工值优先级最高，非法值显式失败，自动流程不得覆盖。
- `MikanOffsetEvidence`：唯一键 `(mikanid, groupid, source_episode)`；只接受已有有效 tmdbid、普通 season 和已验证 TMDB Episode 形成的 offset。
- `MikanTrustedOffsetCache`：默认关闭；同键域三个不同来源 EP 产生相同 `(tmdbid, season, offset)` 才 Trusted，重复 EP 不计数，冲突立即重置或撤销。

### 动画与 TMDB 规范身份

- `AnimeSeries`：真实 TMDB Series，或仅在允许完全兜底时 `tmdbid=0 + bgmid` 的待补全记录。
- `AnimeSeason`：普通正季度；Season 0 不进入普通季度候选。
- `TmdbEpisode`：经 TMDB 验证的 Episode 全集，是 WebUI EP 网格和进度分母的唯一来源。
- `MetadataResolutionRun` / `MetadataResolutionAttempt`：分别保存 Series/Season/Episode 的来源、阶段、策略优先级、结果、失败分类、脱敏原因、可重试性、次数、耗时和时间。
- 人工覆盖优先；确定性季度失败链为 Skip=4、Backtrace=3、标题季度=2、第一季=1。AI 季度/EP 匹配是独立默认关闭阶段，HTTP 默认超时 600 秒，最终 Series/Season/Episode 必须逐级由 TMDB 验证。
- 特别篇、小数集号和无法可靠匹配的文件不转换为普通整数 EP；Series/普通 Season 已确认时保留原名进入该季度 `Other`。

### 任务、去重与生命周期

- `IngestTask`：接受一个统一导入 item 后的总任务，保存来源/路由/规则快照。
- `TaskFile`：Torrent 内可信文件路径、容量、来源 EP 候选、规范 TMDB 目标或 `Other` 原因。
- `EpisodeClaim`：恢复暂停下载器前事务占位；规范唯一键 `(tmdb_series_id, season_number, episode_number)`。
- `CompletionRecord`：只在下载、文件策略、整理和必要 NFO 全部成功后写入；同 TMDB+Season+EP 的后续输入直接跳过，其他 EP 不受影响。
- `FallbackClaim` / `FallbackCompletionRecord`：只在完全兜底开关开启、TMDB 成功访问后确定性无匹配且有 bgmid 时使用；WebUI 列入“待补全 TMDB”，不显示伪造 EP 进度。
- `DownloadJob`：qBittorrent hash/实例、下载状态、速度、容量、ETA 与错误；另存下载准备的 pending/preparing/completed 状态、租约、重试时间和安全失败码。qB 确认接收时同时固化该任务实际使用的 `download_root_path` 与 `save_root_path`，后续配置变化不得改写进行中任务的整理边界。
- `FileOperation`：link/link_delete/move/wait_move 的逐步、可重试状态；Mikan 默认 `move`。
- `DeletePlan` / `DeleteExecution`：业务记录、下载器任务、下载源文件、媒体库文件四类独立选择；已下载记录删除作为显式业务动作，组合执行前必须预览。

字幕只允许通过同目录同 stem（可附加多语言/default/forced/SDH/轨道 token）或唯一来源 EP 关联视频；它复用视频已经由 TMDB 验证的 Episode，不单独请求 TMDB、不单独占 claim 或写完成记录。`.idx/.sub` 分别保存关联并保留扩展。无法唯一关联但季度已确认时进入 `Other`；关联成功时目标为 `Eyyy.<原后缀>.<字幕扩展>`。

媒体目标路径只由已持久化的 TMDB 规范名称、Season、Episode 和捕获的 save root 生成；所有跨平台非法字符、控制字符、Windows 保留名及尾随点/空格都做确定性清洗。实际 `move` 必须先验证源路径和目标路径仍位于捕获根目录内并拒绝符号链接穿越；同卷优先原子 rename，跨卷使用同目录 task-owned partial、SHA-256 双向校验、原子提交目标后才删除源。重试时目标已存在仅在容量与内容一致时清理源，否则保留双方并报告冲突。

Mikan move worker 在 qB 报告完成后再次暂停任务，恢复/建立不可变逐文件计划，逐项完成安全 move，再以原子临时文件写入系列根 `tvshow.nfo`；只有这些步骤全部成功，才在 SQLite 同事务写 completion record 并完成 episode claim。随后任务进入独立 `organizing_cleanup`，另一次租约只调用 qB `deleteFiles=false`，成功后才成为 `organized`。下载器离线或进程崩溃不会重做已完成文件，也不会删除媒体库目标。

下载前门禁固定为：安全暂存并解析 Torrent → qB paused add/同 hash 接管并再次显式暂停 → `download_preparing` 下完成 Series/Season/Episode 与逐集 claim → 再次暂停并精确核对 qB 文件 index/path/size → duplicate/ignored 设 priority 0、episode/other 设 priority 1 → 仅存在 wanted 文件时恢复并进入 `download_queued`。若全部文件均被逐集去重，则保持不恢复、持久化 `download_skipped_duplicate`，并仅允许 `deleteFiles=false` 清除下载器任务；核对失败按持久化租约重试，不能启动下载。

## 4. SQLite 规则

- 只使用 `Microsoft.Data.Sqlite`、参数化命令和显式 SQL，不使用 EF Core、运行时实体映射或反射 migration。
- schema migration 是按版本排序的编译期常量；启动时在单事务执行并记录 `schema_migrations(version, name, applied_at_utc)`。schema v10 起 media organization 使用 job 级租约，逐文件 `file_operations` 每个 task file 唯一，并把文件完成与下载器 cleanup 作为两个可独立恢复阶段。schema v12 起删除确认不依赖不透明 JSON：预览把完成记录 ID、下载器实例+hash、源/媒体绝对路径及其捕获根目录规范排序后计算 SHA-256 指纹；确认必须提交该指纹，事务内重新计算一致后才冻结为逐项 `delete_execution_items`，同一任务只能存在一个活动计划。执行器按 qB 任务、源文件、媒体文件、业务记录的顺序消费不可变目标并持久化逐项结果；qB 永远使用 `deleteFiles=false`，精确文件删除拒绝根目录本身、目录、越界路径和符号链接穿越且不清理父目录，缺失文件作为幂等 skipped。只有前置外部动作成功后才事务删除 completion（alias 级联）及同 TMDB Episode 的 completed claim。
- 启用 `PRAGMA foreign_keys=ON`、WAL、busy timeout；每个写入工作流使用短事务。
- 枚举按稳定小写字符串或显式整数存储，禁止依赖 .NET 类型名。
- 所有时间使用 UTC ISO-8601；所有比较 ID（source/profile/downloader）在入口转小写 invariant。
- passkey URL、密码、Access Key 和 AI key 不进入业务表、异常正文或 Web 响应。

首个 schema 至少包含：`source_profiles`、`mikan_work_rules`、`mikan_offset_evidence`、`mikan_trusted_offsets`、`anime_series`、`anime_seasons`、`tmdb_episodes`、`ingest_tasks`、`task_files`、`metadata_resolution_runs`、`metadata_resolution_attempts`、`episode_claims`、`completion_records`、`completion_aliases`、`download_jobs`、`file_operations`、`delete_executions`。

## 5. NativeAOT 边界

允许：

- `System.Text.Json` source generation；固定 DTO 和 polymorphism 显式表。
- ASP.NET Core Request Delegate Generator 能静态分析的 typed Minimal API handlers。
- 编译期 DI 注册；内置插件使用普通泛型/接口注册。
- 显式 SQL reader ordinal → 构造函数映射。
- Torrent v1 使用自有严格 Bencode reader，并对原始 `info` 字节计算协议规定的 SHA-1；不把 announce 或原始 metainfo 投影到业务 DTO。
- passkey URL 抓取关闭自动 redirect，每跳重新校验 SourceProfile host 和全部 DNS 地址；`SocketsHttpHandler.ConnectCallback` 只连接本跳已校验 IP，避免校验后再次解析造成 DNS rebinding。
- 静态 TypeScript 构建产物作为 content/embedded resource。

禁止进入生产路径：

- `Assembly.Load*`、程序集扫描、MEF、`Reflection.Emit`、运行时代码生成和动态代理。
- EF Core、运行时 ORM materializer、反射式 JSON/YAML DTO 序列化。
- Python/gpython 运行时和 `.py` 执行。
- 未固定 DTO 的 `object`/`dynamic` 协议载荷。
- 依赖当前工作目录或宿主平台分隔符的路径逻辑。

所有生产项目设置 `IsAotCompatible=true`、trim/AOT analyzers；入口发布设置 `PublishAot=true`。每个阶段至少在 `win-x64` 本地 publish smoke，并由 CI 覆盖五个 RID。无法在当前宿主执行的目标产物由 GitHub 对应 OS runner 生成，不能用“能 restore”代替 publish 成功。

## 6. 第一阶段垂直切片

1. Core：配置/目录模型、业务枚举和校验。
2. Data：schema v1、migration runner、显式 repository 和 SQLite 健康检查。
3. App：`/ping`、`/api/v1/status`、静态 WebUI shell；启动时创建目录并迁移数据库。
4. Delivery：JIT build/test、win-x64 NativeAOT smoke、五 RID CI matrix、NativeAOT Dockerfile/Compose。

后续按相同边界扩展统一导入 → RSS 规则 → claim/去重 → qBittorrent → TMDB → 整理 → WebUI 状态；每个模块测试通过后独立提交。
