# AnimeGoNet 主程序架构与数据模型

## 1. 解决方案边界

```text
AnimeGoNet.slnx
├─ src/AnimeGoNet.Core           领域模型、规则、配置与接口；零基础设施依赖
├─ src/AnimeGoNet.Data           SQLite 连接、显式 SQL、迁移和 repository
├─ src/AnimeGoNet.App            Minimal API、静态 WebUI、DI、后台任务、进程入口
├─ src/AnimeGo.Plugin.Abstractions  六类稳定 C# 契约与显式 PluginCatalog
├─ src/AnimeGoNet.Core/Plugins      编译期注册的内置 C# 实现
├─ tests/AnimeGoNet.Core.Tests
├─ tests/AnimeGoNet.Data.Tests
└─ tests/AnimeGoNet.App.Tests
```

依赖只允许从外向内：`App/Data → Core → AnimeGo.Plugin.Abstractions`；App 可以同时组合 Data 和 Core。内置插件实现六类 Abstractions 契约，由 `App` 显式注册。Core 不引用 ASP.NET、SQLite 或下载客户端实现。

HTTP JSON 边界只使用闭合 DTO 和编译期 `ApiJsonContext`。生成的 resolver 在 Minimal API resolver chain 中排第一；所有公开、非泛型 `AnimeGoNet.App.Api` contract，以及 endpoint 方法签名里出现的每个闭合 generic envelope，都由测试穷尽枚举并要求 `GetTypeInfo` 非空。新增 DTO 不能依赖 JIT 反射兜底：漏登记会在普通测试阶段直接失败，发布的 NativeAOT API smoke 再验证真实运行边界。开放式旧配置 payload 只以显式 `JsonElement` 表示，不创建 `Dictionary<string, object>` 或运行时实体映射。

内置目录当前包含 source `mikan/u2/ttg`、feed `mikan-rss`、parser `mikan-title`、filter `mikan-tool`、rename `anime-library` 和 schedule `staged-torrent-dispatch`。Legacy RSS API、Mikan 批次过滤/解析、媒体整理和 staging worker 都从同一个目录按稳定 ID 取得实现；同步静态入口仅保留为测试/兼容 facade。目录中没有 Python 条目，主程序也没有解释器、脚本执行、程序集扫描或动态 DLL 加载路径。

`PluginScheduleCoordinator` 是编译期 schedule 插件的统一 Cron 宿主。它使用无反射的纯 C# 六字段解析器，注册时固定插件 ID、参数、时区、`StartRun` 和下一次执行时间；运行中增删任务会唤醒等待，不依赖轮询配置。每个触发独立执行，失败最多三次且间隔三秒；应用停止令牌会同时取消等待、重试和插件调用，HostedService 在退出前回收仍在运行的调用。目录数据库、AnimeGoNetData 更新与 Mikan RSS 导入均以编译期内置 schedule 插件注册，不通过程序集扫描自动出现。`DataUpdateScheduleManager` 串行执行移除/重建：新 Cron 无效时恢复旧任务与旧运行快照，禁用只移除定时任务；没有启动后台 worker 时仍更新手动 API 使用的运行策略，但不创建假调度。`SourceRssScheduleManager` 在启动时按已启用 SourceProfile 注册 `source-rss-*`，来源 CRUD 后立即热替换；调度参数只有规范来源 ID 和 revision，不含 RSS URL、Cookie 或 passkey。插件执行前按 revision 重新取得 URL，旧任务不访问网络；同来源重叠触发被 SQLite `running` 门禁旁路，异常退出留下的运行状态在下次启动标为 `rss_schedule_interrupted`。

宿主使用 5 秒 `ShutdownTimeout`。所有后台循环把 HostedService 的停止令牌继续传给 SQLite、HTTP、qBittorrent 和插件调用；配置文件已经持久化后的 schedule 热应用忽略客户端断开，但改用 `ApplicationStopping`，因此不会拖住进程退出。RSS winner 失败清理也只跨越请求取消，宿主停止时允许租约按超时恢复。日志 WebSocket 同时链接 `RequestAborted` 与 `ApplicationStopping`，停止时先结束收发并发送正常关闭帧。Linux/macOS 发布 smoke 对进程发送 `SIGTERM` 并要求 7 秒内零退出；5 秒是应用业务期限，额外 2 秒只用于 CI 调度余量。

`web=false`/`ANIMEGO_WEB=false` 用显式 `HeadlessServer` 替换 Kestrel 的 `IServer`，而不是偷偷绑定随机或 loopback 端口；HostedService 生命周期和 5 秒退出期限保持不变。`debug=true` 在创建 provider 前同时设置宿主 filter 与滚动文件最低等级。CLI 只把固定上游的四个单短横线开关归一为 .NET 配置形式，其余现代部署配置参数原样交给 Configuration provider；帮助路径在构建宿主前返回，因此不会触碰数据目录。

上游 Go 源码位于独立的 `AnimeGo` 目录和 Git 仓库，仅作为差分 fixture 与业务行为索引；本仓库只保存 `DotnetProject` 的 C# 主程序、测试、文档和交付资产，两边不共享 Git 历史。

## 2. 配置、数据与目录真相源

- 部署配置文件保存监听地址、Access Key、`data_path`、`download_path`、`save_path`、命名 qBittorrent 实例、路径映射、TMDB/AI 连接与更新策略。
- 旧 `1.1.0`～`1.7.1` `setting:`/`advanced:` qBittorrent 配置在完整解析和已知字段校验后，默认以 `CreateNew` 在原目录保存原字节版本化备份，再从同目录临时文件原子替换为规范 1.7.1；同秒冲突只增序号，不覆盖证据。Transmission/其他客户端保持原文件并继续 fail closed。Python/JavaScript 插件不进入当前 C# 运行时；旧动态 tag 模板迁移到来源专用字段，不伪装成静态 tag。
- 环境变量可以覆盖部署配置；WebUI 显示最终值与来源，被环境变量覆盖的字段只读。Web/API 写入的 qB 覆盖位于 `data_path/config/downloaders.private.json`，TMDB/季度失败链/AI/offset/Torrent/AnimeGoNetData 更新覆盖位于 `data_path/config/application.private.json`，外部进程插件启停/args/vars 位于 `data_path/config/external-plugins.private.json`；均采用 source-generated JSON、revision、同目录临时文件原子替换，Unix 权限为 `0600`。这些文件属于部署 secret，必须随 data_path 一起保护且不得提交。按项目所有者的本机便利要求，经过同源 Access-Key 边界的配置编辑 API/WebUI 会直接回填当前有效凭据并在保存前显示明文差异；运行状态、任务详情、日志、异常和 AI 调用轨迹仍必须脱敏。覆盖或恢复前，当前文件按 revision 原子备份为 `data_path/backups/application.private.revision-{revision:D20}.json`，备份同样为 `0600`；同 revision 已存在不同内容时拒绝写入，不能覆盖证据。Web 不展示或改写运维人员维护的原始部署 YAML，避免注释/格式丢失和环境覆盖被伪装成写入成功；data update 七个字段保存后例外地热应用，其他应用覆盖继续在重启后整体生效。仅为上游客户端保留的 `/api/config` 通过 Access-Key 后可读写包含 secret 的部署文件；写入先完成 JSON/YAML 边界、版本和强类型值验证，再创建不可覆盖的可选原字节备份并原子替换，当前进程不热应用且响应明确要求重启。
- SQLite 保存来源 profile、规则、动画、任务、匹配尝试、下载/整理状态、完成记录和审计，不复制密码等部署配置。
- Docker 默认路径固定为 `data_path=/data`、`download_path=/download/incomplete`、`save_path=/download/anime`。Compose 必须把 AnimeGoNet 与 qBittorrent 的共同宿主父目录映射为同一容器内 `/download`。

`DataPath` 下的首版目录：

```text
/data
├─ animegonet.db
├─ staging/             临时 torrent；日志/API 永不显示 passkey URL
├─ cache/               TMDB/Bangumi/cover 缓存
├─ logs/
├─ backups/             私有配置的不可变 revision 备份
└─ plugins/             RID-specific 外部进程插件包；Docker 只读
```

路径模型在启动时完成规范化并验证：所有实例下载目录必须位于全局 `download_path` 下；所有业务写入必须落在已声明根目录内；宿主/容器路径映射必须按最长前缀、目录边界匹配，禁止字符串前缀越界。

## 3. 领域聚合

### 来源与路由

- `SourceProfile`：稳定小写 ID、adapter、下载器实例 ID、元数据字段 schema、规则 revision、文件策略、category、静态 tag、元数据动态 tag 模板、路径与做种策略。
- RSS 编排在请求起点取得一次 SourceProfile record，并通过强类型 `FilterSourceProfileSnapshot` 把相同 revision 的过滤开关传给编译期 filter；winner staging 直接接收原 record，不重新查询当前 profile。这样并发配置更新只能影响后续请求，不能产生规则与下载路由混合快照。
- `DownloaderInstance`：部署配置或 data_path 私有覆盖中的命名 qBittorrent 连接；受统一管理 API 鉴权保护的配置响应会回填用户名和密码，方便本地 WebUI 直接编辑。任务仍只保存实例 ID 和不可变路由快照，不把明文密码复制进业务 SQLite。
- `IngestBatch` / `IngestItem`：统一导入命令；保存 title、脱敏 Torrent URL 指纹、source item/work ID、`mikanid`、`groupid`、可选 bgmid/anidbid/imdbid。

### 人工规则与 Mikan offset

- `MikanWorkMetadataRule`：以 `mikanid` 唯一，保存人工 bgmid、TMDB Series/Season、EP offset、版本和审计；人工值优先级最高，非法值显式失败，自动流程不得覆盖。
- `MikanOffsetEvidence`：唯一键 `(mikanid, groupid, source_episode)`；只接受已有有效 tmdbid、普通 season 和已验证 TMDB Episode 形成的 offset。
- `MikanTrustedOffsetCache`：默认关闭；同键域三个不同来源 EP 产生相同 `(tmdbid, season, offset)` 才 Trusted，重复 EP 不计数，冲突立即重置或撤销。

### 动画与 TMDB 规范身份

- `AnimeSeries`：真实 TMDB Series，或仅在允许完全兜底时 `tmdbid=0 + bgmid` 的待补全记录。
- `AnimeSeason`：普通正季度；Season 0 不进入普通季度候选。
- `TmdbEpisode`：经 TMDB 验证的 Episode 全集，是 WebUI EP 网格和进度分母的唯一来源。
- TMDB HTTP 成功响应经 source-generated JSON 写入 SQLite `bolt/themoviedb`，默认
  TTL 默认为 144 小时（6 天）。缓存按规范 Base URL、语言、operation 与请求身份分区并使用
  SHA-256 opaque key，不保存 API key 或 Bearer token，原始搜索词只参与键摘要而不
  单独落库；读取时再次验证
  Series/Season/Episode 父子身份，损坏或不一致条目删除后回源。404 与任何失败不做
  negative cache，缓存不可用时不阻断权威 TMDB 请求。
- `MetadataResolutionRun` / `MetadataResolutionAttempt`：分别保存 Series/Season/Episode 的来源、阶段、策略优先级、结果、失败分类、脱敏原因、可重试性、次数、耗时和时间。
- 人工覆盖优先；确定性季度失败链为 `TMDBFailSkip=4`、`TMDBFailBacktrace=3`、`TMDBFailUseTitleSeason=2`、`TMDBFailUseFirstSeason=1`。P3 需要 `bgmid`，按每个 Bangumi 前作的日文名、中文名和开播日期重新联合验证完整 `tmdbid + Season`，可恢复不同的 TMDB Series。P2 只解析统一导入任务 `title`，P1 固定本地 `S01`，两者都不验证 TMDB Season，并保存实际取得策略供 UI 区分。AI 元数据匹配是一个独立、默认关闭的任务级阶段：一个开关、一个 Prompt、每任务最多一次调用，同时返回 Series/Season/Episode；HTTP 默认超时 600 秒，Backtrace、AI 和后续 Episode 候选仍须由 TMDB 验证。
- 特别篇、小数集号和无法可靠匹配的文件不转换为普通整数 EP；Series/普通 Season 已确认时保留原名进入该季度 `Other`。

作品库管理继续服从 TMDB 权威边界。创建只以 `TMDB Series ID + Season` 在线验证后写入 Series/Season/完整 Episode snapshot；刷新使用由内部行身份与更新时间计算的 SHA-256 `resource_revision` 做乐观并发，并重新验证相同 TMDB 身份；没有自由文本改名或本地伪造 Season/EP 的写入口。删除只移除无引用的本地投影：事务内检查任务文件、完成记录、Episode claim、Mikan 人工规则、fallback 完成记录和活动 NFO 重写，任何命中都拒绝并引导四类删除流程；下载器任务、源文件和媒体文件永不由作品库 CRUD 处理。

外部媒体补录是作品库的显式命令，不是目录数据库定时刷新的一部分。全库或单季度命令从当前 TMDB 投影推导唯一标准目录，只将直接目录内精确 `E###`、非空、非符号链接且唯一命中现有 `tmdb_episodes` 的视频写为 `external_import` 完成记录，并将同 TMDB 三元组的活动 Episode claim 原子标为 completed；SQLite 唯一键保证重复扫描幂等。它不从任意标题猜 Series/Season/EP，不遍历 `Other`，不为无 Torrent 身份的文件伪造 sidecar、NFO 或来源 alias，也不注册 hosted service/schedule。API 只返回媒体相对路径和稳定结果码。

### 任务、去重与生命周期

- `IngestTask`：接受一个统一导入 item 后的总任务，保存 source profile/revision、adapter、来源标题、item/work ID、Mikan/group/Bangumi/AniDB/IMDb、来源发布时间及路由/规则快照；这些均为来源证据，不得写入 TMDB 规范身份。详情 API 只返回不透明 item/work ID 的域隔离 SHA-256 指纹，不返回原值。
- `TaskFile`：Torrent 内可信文件路径、容量、来源 EP 候选、规范 TMDB 目标或 `Other` 原因。
- `EpisodeClaim`：恢复暂停下载器前事务占位；规范唯一键 `(tmdb_series_id, season_number, episode_number)`。
- `CompletionRecord`：只在下载、文件策略、整理和必要 NFO 全部成功后写入；同 TMDB+Season+EP 的后续输入直接跳过，其他 EP 不受影响。
- `FallbackClaim` / `FallbackCompletionRecord`：只在完全兜底开关开启、TMDB 成功访问后确定性无匹配且有 bgmid 时使用；WebUI 列入“待补全 TMDB”，不显示伪造 EP 进度。
- `DownloadJob`：qBittorrent hash/实例、下载状态、速度、容量、ETA 与错误；另存下载准备的 pending/preparing/completed 状态、租约、重试时间和安全失败码。qB 确认接收时同时固化该任务实际使用的 `download_root_path` 与 `save_root_path`，后续配置变化不得改写进行中任务的整理边界。
- `FileOperation`：link/link_delete/move/wait_move 的逐步、可重试状态；Mikan 默认 `move`。
- `DeletePlan` / `DeleteExecution`：业务记录、下载器任务、下载源文件、媒体库文件四类独立选择；已下载记录删除作为显式业务动作，组合执行前必须预览。

字幕只允许通过同目录同 stem（可附加多语言/default/forced/SDH/轨道 token）或唯一来源 EP 关联视频；它复用视频已经由 TMDB 验证的 Episode，不单独请求 TMDB、不单独占 claim 或写完成记录。`.idx/.sub` 分别保存关联并保留扩展。无法唯一关联但季度已确认时进入 `Other`；关联成功时目标为 `Eyyy.<原后缀>.<字幕扩展>`。

媒体目标路径只由已持久化的 TMDB 规范名称、Season、Episode 和捕获的 save root 生成；所有跨平台非法字符、控制字符、Windows 保留名及尾随点/空格都做确定性清洗。实际 `move` 必须先验证源路径和目标路径仍位于捕获根目录内并拒绝符号链接穿越；同卷优先原子 rename，跨卷使用同目录 task-owned partial、SHA-256 双向校验、原子提交目标后才删除源。多文件 operation 按 Torrent 相对路径、文件 ID 稳定执行；部分文件已完成后发生冲突只释放 job 租约，不提前写业务 completion，重试跳过 completed operation 并继续 pending operation。目标已存在仅在容量与内容一致时清理源，否则保留双方并报告冲突。

Mikan move worker 在 qB 报告完成后再次暂停任务，恢复/建立不可变逐文件计划，逐项完成安全 move，再以原子临时文件写入系列根 `tvshow.nfo` 以及与上游兼容的系列、季度、Episode 目录 JSON；侧车同步写入 schema v27 的 SQLite 目录索引。只有这些步骤全部成功，才在 SQLite 同事务写 completion record 并完成 episode claim。随后任务进入独立 `organizing_cleanup`，另一次租约只调用 qB `deleteFiles=false`，成功后才成为 `organized`。这是对上游 `clientnotifier` 完成 callback 调用 `DeleteFile:true` 的明确安全偏差：源/媒体文件只能由受根目录约束的主程序操作，不能委托 qB 递归删除。下载器离线时 cleanup 释放为持久化重试且实例 circuit 打开；健康探测成功关闭 circuit 后只重试 qB 任务清理，不重做已完成文件，也不删除媒体库目标。

下载前门禁固定为：安全暂存并解析 Torrent → qB paused add/同 hash 接管并再次显式暂停 → `download_preparing` 下完成 Series/Season/Episode 与逐集 claim → 再次暂停并精确核对 qB 文件 index/path/size → duplicate/ignored 设 priority 0、episode/other 设 priority 1 → 仅存在 wanted 文件时恢复并进入 `download_queued`。若全部文件均被逐集去重，则保持不恢复、持久化 `download_skipped_duplicate`，并仅允许 `deleteFiles=false` 清除下载器任务；核对失败按持久化租约重试，不能启动下载。

## 4. SQLite 规则

- 只使用 `Microsoft.Data.Sqlite`、参数化命令和显式 SQL，不使用 EF Core、运行时实体映射或反射 migration。
- schema migration 是按版本排序、名称唯一的编译期常量。启动先验证数据库历史是当前常量的精确前缀；每个待执行版本以 SQLite immediate 写事务取得跨连接锁，在锁内重新检查后把 DDL 与 `schema_migrations(version, name, applied_at_utc)` 一起提交。并发首次启动因此不会重复执行，单版本失败整体回滚并可在修复后续跑；历史缺口、名称变化或数据库版本高于应用均以稳定错误码 fail closed，不猜测降级。schema v10 起 media organization 使用 job 级租约，逐文件 `file_operations` 每个 task file 唯一，并把文件完成与下载器 cleanup 作为两个可独立恢复阶段。schema v12 起删除确认不依赖不透明 JSON：预览把完成记录 ID、下载器实例+hash、源/媒体绝对路径及其捕获根目录规范排序后计算 SHA-256 指纹；确认必须提交该指纹，事务内重新计算一致后才冻结为逐项 `delete_execution_items`，同一任务只能存在一个活动计划。执行器按 qB 任务、源文件、媒体文件、业务记录的顺序消费不可变目标并持久化逐项结果；qB 永远使用 `deleteFiles=false`，精确文件删除拒绝根目录本身、目录、越界路径和符号链接穿越且不清理父目录，缺失文件作为幂等 skipped。只有前置外部动作成功后才事务删除 completion（alias 级联）及同 TMDB Episode 的 completed claim。schema v13 将 Mikan RSS whitelist、blacklist、priority groups、具名数组和值拆为带显式 position/FK/唯一约束的规范化表；所有匹配值保存前按 invariant lowercase 归一，整套规则用 revision 乐观并发替换，启动只在不存在时写默认规则。schema v20 为 fallback completion 增加 `pending/resolved/duplicate_after_resolution` 恢复状态、规范 completion 外键与显式 fallback alias；规范 completion 删除时通过级联同时失效恢复记录和 alias。schema v21 把恢复后的 `tvshow.nfo` 更新建模为带租约、失败码和重试时间的持久作业；目标只来自原下载任务捕获的 `save_root_path`，不从媒体路径字符串猜测根目录。schema v27 保存目录数据库索引、每次扫描和逐文件稳定拒绝码；扫描不跟随 reparse point/symlink，单侧车上限 64 KiB，刷新与整理侧写通过进程内门禁串行，避免全量刷新覆盖刚完成的增量索引。schema v28 保存 AnimeGoNetData 版本、active/previous 指针、导入/回滚运行审计、独立 staging subjects/episodes 与版本化归档表；压缩包完整验证后才在单事务内复制 staging、切换 active 并裁剪旧版本，失败不修改旧 active。schema v29 增加带自增顺序的远程检查/下载/导入审计及已验证下载包目录；下载只写应用创建的 `.partial-*`，长度与哈希全部通过后才同卷移动到 `data-update/packages/<version>`，数据库不保存 manifest/asset URL。schema v30 为季度详情审计增加 task file 规范身份、解析 Run 规范身份、Run 尝试时间和 Mikan 人工规则作用域索引；不复制或反规范化审计数据。schema v33 将任务创建时的做种目标复制到 download job，以 qB 累计秒数产生单调状态并保存首次完成时间；`wait_move` 和 `link_delete` 的整理/删除门禁只读取该持久化状态，不依赖可能丢失或回退的瞬时 qB state。
- schema v34 保存来源动态 tag 模板及 job 的 `pending/applied/skipped/not_configured` 状态、实际 tag 和稳定失败码；后置 qB 赋值可重试且历史任务不受 profile 修改影响。schema v35 为来源完成 alias 建立查询索引，并在 RSS 批次条目保存早期命中的 completion/alias 与检查时间；正常媒体整理将 completion、来源 alias 和 completed claim 放在同一事务。RSS winner 在 Bangumi 页面/Torrent 网络访问前使用 IMMEDIATE 写事务复查同 `source+mikanid+来源EP`，并在 staging 前再次事务复查以关闭其间完成写入的并发窗口；完成记录删除通过 FK 级联清除 alias/批次命中证据，因此允许同一来源集显式重新进入。schema v36 在 `source_profiles` 保存只写 RSS URL、启用开关、六字段 Cron、`never/running/succeeded/failed`、起止时间、稳定失败码及最近 batch 外键；配置 revision 更新会原子清空旧执行审计，防止旧执行污染新配置。schema v37 在 download job 保存媒体整理的权威阶段、已完成单位和总单位，并用约束/触发器禁止与 `not_required/cleanup/completed` 状态矛盾的组合；租约恢复以持久阶段识别下载器清理，避免 `link/link_delete` 在崩溃后重复文件整理。schema v38 在 `source_profiles` 保存默认开启且只控制日志通知的 `duplicate_notification_enabled`；任务创建时把值固化进 `route_snapshot_json`，RSS 批次使用取得的 profile 快照，后续 profile 修改不会改变历史任务的通知策略。schema v39 只保存旧 Go cache JSON 导入包的内容指纹、非敏感计数与重复次数；bucket/entry 与审计同行在一个 IMMEDIATE 事务提交。schema v40 在实际承载统一 AI 调用的 metadata attempt 保存 provider 模型、累计 token、HTTP 请求与工具调用计数；Prompt、工具正文和凭据不持久化。schema v41 扩展逐文件 Episode 证据触发器，允许 `tmdb_episode_bangumi_date`，用于保存 Mikan 的 Bangumi 单集首播日期到已确认 TMDB Season Episode 的确定性映射；Torrent 发布日期不作为确定性 EP 证据。schema v43 增加 `tmdb_episode_bangumi_nearest_date`，使单文件、最多 7 日且文件名 EP 一致的补判拥有独立可验证证据；这两次迁移只重建触发器，不改写历史任务。schema v44 恢复旧版本中准备已经完成、文件已全部解析、qB 已完成且整理仍 pending，却被重复元数据 Run 留在 `metadata_season_resolved` 的任务；仍有 pending 文件或活动 Run 的任务不会被推进。schema v45 按 AnimeGoNetData 版本原子累计本地 Subject、完整 Episode 集及前传关系命中次数和最近命中时间；版本裁剪不删除使用记录，WebUI 汇总全部历史版本，在线回源不计数。schema v46 在同一事务中写入汇总和逐条命中事件，事件保存类型、subject id、返回条数、数据版本与时间；按时间和自增序号稳定倒序分页，升级前历史不补造。
- 启用 `PRAGMA foreign_keys=ON`、WAL、busy timeout；每个写入工作流使用短事务。
- 枚举按稳定小写字符串或显式整数存储，禁止依赖 .NET 类型名。
- 所有时间使用 UTC ISO-8601；所有比较 ID（source/profile/downloader）在入口转小写 invariant。
- 单次 Torrent/RSS passkey URL、密码、Access Key 和 AI key 不进入业务表、异常正文或 Web 响应。唯一持久例外是用户明确配置的自动 Mikan RSS URL：只保存在 `source_profiles.rss_feed_url`，API/WebUI 仅返回是否配置，调度参数、审计、异常和日志仍不含原值；`data_path` 数据库及其备份必须按敏感数据保护。

首个 schema 至少包含：`source_profiles`、`mikan_work_rules`、`mikan_offset_evidence`、`mikan_trusted_offsets`、`anime_series`、`anime_seasons`、`tmdb_episodes`、`ingest_tasks`、`task_files`、`metadata_resolution_runs`、`metadata_resolution_attempts`、`episode_claims`、`completion_records`、`completion_aliases`、`download_jobs`、`file_operations`、`delete_executions`。

## 5. NativeAOT 边界

允许：

- `System.Text.Json` source generation；固定 DTO 和 polymorphism 显式表。
- YamlDotNet 仅使用 `YamlStream`/`YamlNode` AST；部署 YAML 由显式节点遍历扁平化为
  `IConfiguration` 键，不调用反射式 serializer/deserializer。输入固定为严格 UTF-8、
  单文档 mapping，限制 1 MiB、32 层和 4096 节点，重复键及非标量键直接拒绝。
- ASP.NET Core Request Delegate Generator 能静态分析的 typed Minimal API handlers。
- 编译期 DI 注册；内置插件使用普通泛型/接口注册。
- 显式 SQL reader ordinal → 构造函数映射。
- Torrent v1 使用自有严格 Bencode reader，并对原始 `info` 字节计算协议规定的 SHA-1；不把 announce 或原始 metainfo 投影到业务 DTO。
- passkey URL 抓取关闭自动 redirect，每跳重新校验 SourceProfile host 和全部 DNS 地址；未命中全局代理的直连分支由 `SocketsHttpHandler.ConnectCallback` 只连接本跳已校验 IP，避免校验后再次解析造成 DNS rebinding。显式命中 `outbound_proxy.hosts` 时仍保留逐跳 host/DNS/redirect 门禁，但 forward proxy 自行解析并连接目标，因此不声称具备直连分支的 DNS 连接钉死。
- 静态 WebUI 唯一源码位于 `src/AnimeGoNet.App/WebUI/src`，使用固定 TypeScript 版本、strict 模式和 DOM 类型编译到 `wwwroot`，不引入浏览器运行时框架。编译产物随主程序作为 static content 发布；CI 同时执行 `--noEmit` 类型检查并验证重新编译后 Git 无差异。
- `/websocket/log` 使用原生 ASP.NET Core WebSocket 和编译期 `ILoggerProvider` fan-out；控制命令只由有界 `JsonDocument` 读取，日志帧显式构造，不使用反射 DTO。每个连接独立暂停，最多缓存最新 1000 条；发送队列最多 256 帧并丢弃最旧帧，避免慢浏览器无界占用内存。进入 WebSocket 前统一验证 access-key，输出先用 `GeneratedRegex` 脱敏 URL、Bearer、Cookie、Authorization、密码及常见 token/key。
- 文件日志使用自有 `ILoggerProvider`，固定写入 `data_path/logs/animego.log`，不依赖 Serilog/NLog 或运行时模板。与上游一致只保存 Information 以上，每 2 MiB 轮转，最多 14 份并删除超过 14 天的受管数字后缀备份；写入与轮转在进程内串行，单行即时 flush 以支持运维 tail。两个 provider 均由 DI 工厂创建并以 `ILoggerProvider` 映射到同一实例，provider 的释放幂等，宿主停止后不遗留文件句柄；Unix 新文件权限固定 `0640`。

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
