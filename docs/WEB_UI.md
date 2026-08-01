# Web UI 作品库设计

本文固定首版本地 Web UI 的动画作品列表、季度详情和排序语义。页面展示的是 AnimeGoNet 已纳管的作品，不把下载器任务列表当成媒体库。

## 1. 列表单位与展示字段

动画作品列表的基本单位是一个 `TMDB TV Series + 普通 Season`。同一 Series 的多个季度分别形成列表项，并允许在前端按 Series 折叠；Season 0 不进入作品列表。

每个列表项至少展示：

- 动画名称：使用当前已验证 TMDB TV Series 的显示名称；首选 TMDB `zh-CN` 名称，缺失时使用 TMDB 原名。
- Cover：首选该 TMDB Season 的 `poster_path`，缺失时回退到 TMDB Series poster，再缺失时显示本地占位图。图片由后端代理并缓存在 `data_path`，浏览器不直接持有 TMDB API key。
- Season：同时显示 TMDB Season Number（如 `S02`）和 TMDB Season Name；二者都来自已验证的 TMDB Season。
- EP 状态：按 TMDB Season 返回的普通 Episode 列表展示集号，并区分“已下载完成”和“未下载完成”。
- 日期：最后更新日期、TMDB 开播日期和本地加入日期。

列表 API 返回稳定 ID 和结构化字段，不让前端从名称、`Sxx` 文本或文件路径反推 TMDB 标识。

## 2. EP 状态只以 TMDB 为准

EP 网格的全集合来自已验证 TMDB Season 的 Episode 列表。来源站集号、文件名集号、Bangumi/AniDB Episode、RSS 项目数、Torrent 文件数量和 `Other` 文件都不能生成或扩大 EP 网格。

状态判定固定为：

- `Downloaded`：规范键 `(TmdbSeriesId, TmdbSeasonNumber, TmdbEpisodeNumber)` 存在有效的下载完成记录，并且下载、整理、重命名及必要的 NFO/目录数据库写入已全部成功。
- `NotDownloaded`：TMDB 中存在该 Episode，但没有有效完成记录。尚未开始、下载中、等待整理、整理失败、已取消和删除完成记录后的 Episode 都属于“未下载完成”；页面可以附加显示其当前任务状态，但不能把它标成已完成。

首页运行状态同时展示目录数据库的当前索引数、最近扫描/写入/拒绝数、稳定失败码和六字段 Cron；“立即刷新”调用 `POST /api/v1/library/directory-database/refresh`。只读状态使用 `GET /api/v1/library/directory-database`。默认 Cron 与上游一致为 `0 0 6 * * *`，配置键为 `refresh_database_cron`。

删除某集的下载完成记录后，该 EP 立即恢复为 `NotDownloaded`。只删除下载器任务、源文件或媒体库文件而保留完成记录时，页面必须显示数据/文件不一致警告，不能静默改变完成状态；用户可通过删除业务完成记录或修复操作纠正。

同一 TMDB Episode 即使有多个来源 alias、字幕组或 Torrent，也只显示一个 EP 标记。字幕和 `Other` 文件不独立计数，不影响季度完成数。季度进度使用 `已完成 TMDB EP 数 / TMDB 普通 EP 总数`。

## 3. TMDB 未完成解析与兜底条目

尚未确认 TMDB Series/Season 的任务不进入标准季度 EP 网格，统一显示在“待补全 TMDB”视图中，并显示解析失败原因、重试状态和取得阶段。

Bangumi 完全兜底产生的 NFO `tmdbid=0` 也属于“待补全 TMDB”。它可以展示明确标注来源的兜底标题、已处理文件数量和 `FallbackCompletionRecord`，但这些状态必须命名为“兜底处理记录”，不能显示为 `Downloaded/NotDownloaded` 的 TMDB EP 标记、TMDB Season cover 或 TMDB 完成比例。

待补全详情还要显示兜底去重身份和作用域，例如“Bangumi Episode”“仅同一 mikanid”“仅当前来源作品”或“仅相同 Torrent/文件”，并在不能跨来源去重时显示风险提示。恢复出真实 TMDB ID/Season/Episode 并通过验证、合并完成记录后，才进入标准动画作品列表；合并冲突显示 `DuplicateAfterResolution`，不自动重新下载或静默删文件。

当前 `GET /api/v1/metadata/pending-tmdb` 和 `/{bgmid}` 已提供待补全作品 summary/detail。静态 WebUI 显示 Bangumi 兜底名、已确认季度、关联任务、已处理文件、兜底 completion/claim、重复数、最近失败分类，以及不含内部 scope key 的去重边界。`mikan_episode`、`source_work_episode`、`torrent_file` 明确提示可能跨来源重复；页面不返回或推导 `tmdb_series_id`、TMDB Episode 进度、季度封面和完成比例。详情 API 另返回不含 scope key、媒体路径的安全恢复候选 ID；页面可为每个候选填写 Season/Episode，并统一提交 TMDB Series ID。`POST /api/v1/metadata/pending-tmdb/{bgmid}/recover` 逐项在线验证后执行事务合并，冲突显示 `DuplicateAfterResolution`。恢复表单打开时暂停十秒自动刷新，防止用户输入丢失；手动刷新和提交成功可强制更新。成功恢复会在同一事务排入可恢复 NFO 重写作业；作业状态的 Web 展示仍待接入统一任务面板。

## 4. 排序

作品列表支持升序/降序切换，至少提供以下四种排序：

1. 最后更新日期 `LastUpdatedAt`：该季度的元数据验证、人工规则变更、任务状态、下载完成记录或文件一致性状态最后一次发生业务变化的时间；仅查看页面不更新时间。默认按此字段降序。
2. 名字 `SortName`：使用 TMDB 显示名称的规范化排序键，不使用来源标题；相同名称按 Season Number、TmdbSeriesId 稳定排序。
3. 开播日期 `AirDate`：使用 TMDB Season 的 `air_date`；缺失值始终排在有日期项目之后，相同日期按名字和 Season Number 稳定排序。
4. 加入日期 `AddedAt`：AnimeGoNet 首次确认并纳管该 TMDB Series + Season 的时间，创建后保持不变；重新匹配回同一规范季度不会重置。

服务端 API 接受明确的 `sort` 和 `direction` 枚举并执行分页前排序。所有排序都必须追加稳定的 TMDB ID/Season Number tie-breaker，保证翻页时不重复或漏项。

## 5. 详情与交互

- 点击列表项进入季度详情，顶部继续显示名称、Cover、Season、三种日期、完成比例及 TMDB Series/Season 的取得方式。
- EP 网格可按已下载/未下载筛选；每格显示 TMDB Episode Number，并可展开 TMDB Episode Name、air date、完成时间和关联任务。
- 对未完成 EP 可以跳转到导入/任务信息；对已完成 EP 可以进入四类删除的影响预览，但列表本身不执行隐式删除。
- Cover 加载失败显示占位图和可访问文本；EP 状态除颜色外必须同时使用文字或图标语义，支持键盘操作和窄屏布局。
- 页面刷新或元数据同步后保持当前排序、方向、筛选和折叠状态；这些纯 UI 偏好可存浏览器本地，不写入业务数据库。

当前静态 TypeScript 页面已实现上述季度列表、四种排序/方向/分页、同源 Cover、详情头部与 EP 网格。EP 项使用可键盘操作的原生详情控件并显式维护 `aria-expanded`；状态同时显示文字与符号，不只依赖颜色。排序、方向、页大小、EP 筛选和当前季度详情使用 `animegonet.library.v1` 保存在浏览器本地，自动刷新不会在用户展开详情时重绘网格。

## 6. 最小查询投影

服务端季度列表投影至少包含：

- `tmdbSeriesId`、`tmdbSeasonNumber`、`displayName`、`sortName`、`seasonName`；
- `posterUrl`、`posterSource`；
- `airDate`、`addedAt`、`lastUpdatedAt`；
- `episodeTotal`、`episodeDownloaded` 和逐 EP 状态（详情接口可延迟加载）；
- Series/Season 的 `resolutionSource`、各自的解析 Run/Attempt ID、验证状态和最近解析运行 ID；
- 元数据/文件一致性警告摘要。

数据库查询必须批量聚合完成记录，禁止列表按条目或按 EP 产生 N+1 查询。

## 7. 当前任务状态投影

在完整作品库落地前，首页提供只读的“匹配与整理状态”任务投影，用于观察统一导入到元数据解析的实际进度。`GET /api/v1/metadata/tasks` 当前返回标题、来源、任务状态、`mikanid`/Bangumi/TMDB Series/Season、完成时固化的 Series/Season/Episode 策略及各自 Run/Attempt 引用、Episode/Duplicate/Other/Pending 文件计数、脱敏失败分类与原因，以及最后更新时间。多个文件的 Episode 来源或 Attempt 不一致时，摘要明确返回 `episode_resolution_mixed=true`，不伪造单一证据。展开“来源 / TMDB 对照”时，`GET /api/v1/metadata/tasks/{taskId}` 返回每个 Torrent 相对文件名、容量、来源 EP、仅供本地审计的文件名 EP 候选、逐文件 Episode Run/Attempt，以及经验证后的 TMDB Series 名、Season 名/号和 Episode 名/号；字幕关联保留自己的 `subtitle_association` Attempt。接口不返回 Torrent URL、passkey、下载绝对路径或媒体绝对路径。详情同时给出统一 AI 是否调用、所在阶段、结果、耗时和安全失败原因。模型自报数字置信度不进入协议或数据库；只有主程序再次验证 TMDB Series/Season/Episode 后，页面才显示“可信依据：TMDB 已验证”，否则明确显示“未建立”。

该投影不返回 Torrent URL、passkey、下载器凭据或文件绝对路径。查询通过单条聚合 SQL 批量产生，避免逐任务读取策略或文件计数。只有已进入 `metadata_failed` 且没有活动租约的任务显示“显式重新匹配”，调用既有重试 API 后刷新状态；它不是自动重试开关，也不会覆盖人工规则。

`GET /api/v1/metadata/tasks` 现支持 `page/page_size`、标题/任务/来源/错误码搜索、任务状态、最新失败阶段、错误码、可重试性、处理分类，以及最后更新/标题/状态/失败分类排序。每个任务返回最新 `result=failed` 尝试的阶段、稳定错误码和 `retryable`，并由服务端给出处理分类：`explicit_retry`、`configuration`、`manual`、`skipped`、`fallback`、`active`、`resolved` 或 `other`。`Authentication`、`Configuration`、`InvalidInput` 归入配置修复；`metadata_failed + retryable` 只标为“可安全重试（需显式）”，不能在尚无自动重试调度器时表示成“待自动重试”。Bangumi 兜底 `tmdb_completion_pending` 独立标为待补全 TMDB，重复跳过也不显示成失败。列表和任务详情还返回最高 attempt Run 的 `latest_run_status`、`tmdb_access_confirmed`、`bangumi_fallback_eligible` 与稳定拒绝原因；失败卡片据此显示 Bangumi 完全兜底允许/拒绝决定，不能根据 `retryable` 或某一条 Attempt 自行推断。

静态 TypeScript 页面持久化纯 UI 筛选/排序/分页偏好，自动刷新继续沿用当前查询；失败卡片同时展示任务失败分类、最新失败阶段、稳定错误码、可重试性和脱敏原因。筛选 API 最多扫描最近 500 个运维任务，这是首版本地控制面的有界投影，不改变 SQLite 中的完整任务与尝试审计。

当前面板属于运维任务视图，不等同于第 1～6 节定义的 TMDB 作品库：文件归类计数不能用作季度完成比例。任务卡片已可按需读取完整策略尝试时间线；标准作品库页面已经独立读取季度列表/详情 API，并提供 TMDB 权威季度的创建、刷新和安全删除。

SQLite schema v23 已为正式 TMDB 作品保存 Series 首播日期与 poster 路径，并为普通 Season 保存首播日期、TMDB Episode 总数与 poster 路径。正常自动/人工解析和“待补全 TMDB”恢复共用同一投影；这些字段是作品库查询和 Cover 代理的权威输入，浏览器不直连 TMDB 图片 URL。

`GET /api/v1/library/seasons` 已提供第 6 节的季度列表基础投影和服务端分页。`sort` 接受 `last_updated`、`name`、`air_date`、`added_at`，`direction` 接受 `asc`/`desc`；空开播日期在两个方向都置后。列表用单次批量查询聚合完整 Episode snapshot 与规范完成记录，返回 snapshot 缺口、snapshot 外完成记录、完成记录缺媒体路径和本地未验证季度警告。`tmdbid=0` 条目始终排除。响应中的 `poster_url` 固定指向同源 Cover API；`poster_path` 只是诊断用的经校验 TMDB 相对路径，页面不得自行拼接外部 URL。

`GET /api/v1/library/seasons/{tmdbSeriesId}/{seasonNumber}` 返回季度头部和完整 EP 网格。网格只枚举该季度实际保存的 `tmdb_episodes`，不会按 `episode_count` 猜造缺失项；状态只由同一规范 TMDB 三元组的 `completion_records` 决定。响应可显示来源、完成时间和媒体路径是否已记录，但不返回媒体绝对路径、内部 SQLite 行 ID、Torrent URL 或凭据。删除完成记录后下一次读取立即显示 `not_downloaded`。

`POST /api/v1/library/seasons` 只接受正整数 `tmdb_series_id` 与普通 `tmdb_season_number`。服务端必须分别读取并验证 TMDB Series、Season 的身份，再以 TMDB 名称、开播日期、封面和完整 Episode snapshot 创建本地投影；页面不允许手工填写动画名、季度名或 Episode。已存在资源返回冲突，不能用创建请求静默覆盖。

`PUT /api/v1/library/seasons/{tmdbSeriesId}/{seasonNumber}` 是权威刷新，不是自由编辑。请求必须提交详情响应中的 64 位 `expected_revision`；服务端在远端请求前和写事务内都检查 revision，再重新验证 Series 与 Season，替换 TMDB 当前 Episode snapshot，并保留规范完成记录。`DELETE` 使用相同 revision；只删除无业务引用的本地 Season/EP 投影，最后一个 Season 删除后才删除 Series。任务文件、完成记录、Episode claim、Mikan 人工规则、fallback 完成记录或活动 NFO 重写仍引用该身份时返回冲突，并要求使用四类删除流程。该操作始终不删除 qBittorrent 任务、源文件或媒体文件。

季度详情同时返回元数据审计投影。`manual_offsets` 只显示当前仍与该规范季度或其关联 Mikan 任务有关、且确实含 EP offset 的人工规则，并同时标明启停、作用域和 revision；它是“当前配置”，不冒充某次历史 Run 实际使用的值。关联任务用规范 `task_files` 或任一解析 Run 的 `tmdb_series_id + tmdb_season_number` 建立，按最近更新时间返回最多 50 条。季度时间线再读取这些任务的全部历史 Run，按尝试时间倒序返回最多 200 条，保留阶段、策略、确定性优先级、结果、稳定错误码、脱敏原因、可重试性和耗时。响应提供总数与 `*_truncated`，页面把三类信息放在独立可展开区块，不用单个“最后成功策略”覆盖历史失败。

`GET /api/v1/library/covers/{tmdbSeriesId}/{seasonNumber}` 依次读取 Season poster、Series poster、本地 SVG 占位图。远端图片使用 TMDB 的无密钥图片地址并复用 TMDB 代理设置，限制 5 MiB、校验 JPEG/PNG/WebP 魔数、合并同一 poster 的并发下载，并缓存到 `data_path/cache/covers`。上游超时、不可用或返回非图片时均返回短缓存占位图；响应头标明来源、缓存命中和安全警告码。浏览器请求、URL 和响应中都没有 TMDB API key。

可信 Mikan offset 面板读取 `/api/v1/mikan/trusted-offsets`，显示 `(mikanid, groupid)`、TMDB Series/Season、带符号 offset、Learning/Trusted/ConflictReset 和不同 EP 进度。清理操作只调用目标键 DELETE，并在确认文本中明确排除人工规则、完成记录与媒体文件。

## 8. 当前生效配置投影

首页通过 `GET /api/v1/config` 展示当前进程实际使用的三目录、容器/后台 worker/Access-Key 状态、TMDB API 地址/代理/语言、Bangumi API 地址/代理、季度失败链、一个任务级 AI 元数据开关和 600 秒默认超时、Bangumi 完全兜底、Mikan 可信 offset 缓存以及 Torrent HTTP/暂存限制。季度失败链按 P4 `TMDBFailSkip` → P3 `TMDBFailBacktrace` → P2 `TMDBFailUseTitleSeason` → P1 `TMDBFailUseFirstSeason` 纵向显示；P3 明确标注需要 `bgmid`，并说明当前联合匹配失败后会逐层回溯前作，以每个前作的日文名、中文名和开播日期重新验证完整 `tmdbid + Season`，而不是锁定当前 tmdbid 只换季度。P2 在前面策略全部失败后，只把统一导入任务的 `title` 交给本地标题解析器，解析成功即使用该本地季度，不验证 TMDB Season；解析不到时继续 P1。P1 直接使用本地 `S01`，同样不验证 TMDB Season。P4、P3 和统一 AI 使用的远端结果仍执行 TMDB 验证。AI 在实际执行位置以“独立 AI”标记，明确不占确定性优先级且默认关闭；其一个开关和一个 Prompt 同时处理 Series/Season/全部 Episode，每任务最多调用一次。Bangumi 完全兜底明确标为“一般不启用”：仅在 TMDB 完全失败且已有 `bgmid` 时使用 Bangumi，季度固定为 `S01`，页面不提供有效 TMDB ID；内部既有 `tmdbid=0` 写入与待补全逻辑保持不变。编辑器允许 TMDB 与 Bangumi 分别选择自定义 API 地址或无凭据 HTTP(S)/SOCKS5 代理；Base URL 必须以 `/` 结尾，代理留空表示直连。

该接口只返回 `api_key_configured`、`read_access_token_configured` 和 `access_key_configured` 布尔值，绝不返回凭据内容；仍受统一 API 鉴权保护。目录标明修改需要重启。页面提供带 revision 的私密覆盖编辑和恢复部署默认操作，密钥输入为空表示保留，另有明确清除选项；保存后持续显示 saved/applied revision 差异。

配置保存采用两个明确步骤。表单提交先调用 `POST /api/v1/config/preview`，服务端使用与实际 PUT 相同的字段锁、规范化和强类型校验，返回字段级 `before/after/effect/sensitive` 投影但不写文件。页面把 `hot_reload` 标为“即时生效”、`restart` 标为“重启生效”；敏感字段无论服务端返回什么都只按 `继承部署配置/已配置（值已隐藏）/已明确清除` 三态渲染。只有预览存在差异时才启用“确认保存并备份”，表单任一输入变化都会使旧预览和待提交对象失效。

确认保存仍使用预览时的 `expected_configuration_revision`；并发变化返回冲突并要求重新预览。覆盖现有私有配置或恢复部署默认前，服务端先把旧 revision 保存到 `data_path/backups/application.private.revision-{20位revision}.json`，再原子替换当前文件。响应只返回被备份的 revision，不返回备份内容或路径；首个私有 revision 没有旧文件可备份。原始部署 YAML 继续只由运维维护，Web 不展示含 secret 的原文，也不改写其注释和格式。

编辑器使用单独的 `editable` 投影。服务端以未应用私密覆盖前的部署基线加当前持久化覆盖计算期望值，因此保存后未重启、或移除覆盖后再次打开编辑器，都不会把旧进程内存中的值误当成部署默认。

`editable.locked_fields` 为每个环境变量控制的字段返回规范字段名、`source=environment` 和实际命中的环境变量名，但不返回环境变量值。当前覆盖 TMDB 地址/代理/语言/超时/API Key/Read Token、Bangumi 地址/代理/超时，以及统一 AI 开关和超时；旧 `ai_use_season_match`、`ai_use_episode_match` 也会锁定规范的 `ai_use_metadata_match`。页面显示最终有效值和锁来源，禁用对应输入、凭据清除控件与提交语义。服务端不信任前端禁用状态：不同值或显式凭据写入统一返回 `configuration_field_locked`，错误只列字段名，不包含凭据。保存其他未锁字段时，锁字段保留其保存前的底层私有覆盖，首次保存则记录为“继承部署”；因此移除环境变量后不会把当时的环境值误当成新的私有覆盖。

## 9. 手动 Torrent 与 RSS 提交

首页“手动提交”区不建立第二套下载逻辑。单个 Torrent 调用 `POST /api/v1/ingest`，选择值是已启用的 SourceProfile ID，因此自定义 Mikan/U2/TTG 来源继续使用各自不可变的下载器、目录、文件策略、category、tags 和 revision 快照。Mikan 手动导入要求 `mikanid` 与 `bgmid`；U2/TTG 可附带作品级 `anidbid`/`imdbid` 参考。结果显示接受/拒绝数量、任务 ID、实际来源 revision、下载器、文件数、info hash 和不可逆 URL 指纹，不显示原 Torrent URL。

Mikan RSS 调用现代 `POST /api/v1/rss/ingest`，请求包含明确的 `source_profile_id` 与 RSS URL。服务端必须先确认该 profile 已启用且 adapter 为 Mikan，再发起任何网络请求；随后复用旧过滤、同集有序优选、winner lease 和统一 Torrent staging。响应显示 batch、mikanid、规则 revision、候选决策和实际任务 ID。旧 `/api/rss` 的 AnimeGoHelper 契约保持不变。

Torrent URL 与 RSS URL 都按敏感值处理：页面使用密码输入，不写入 `localStorage`，构造请求后立即清空输入框，并在请求建立后主动丢弃临时 JSON 字符串引用。结果节点只用 `textContent` 创建；后端稳定错误响应不得包含请求 URL、passkey、Cookie 或下载器凭据。

## 10. Mikan 人工作品规则

首页“Mikan 人工作品规则”区按 `mikanid` 管理最高优先级的作品级元数据覆盖。读取不存在的规则时，页面明确显示将从 revision 0 创建；读取现有规则后，保存、禁用和清除均携带 expected revision，服务端在并发修改时返回冲突而不是覆盖其他管理操作。字段包含 `bgmid`、TMDB Series ID、TMDB Season、带符号 EP Offset，以及可选的样例来源 EP。填写样例 EP 时，保存前必须在线验证 Series、Season 和 `来源 EP + Offset` 对应的目标 Episode；未填写时仍执行规则自身的结构和组合约束。

`GET /api/v1/mikan/work-rules/{mikanid}/impact` 返回权威任务总数和有限明细，并将任务分为未来自动应用、可显式重试的失败任务、活动中保护、已解析保护、已整理保护和其他状态。页面不会把截断后的明细数量误当作总数。保存、禁用或清除规则本身只影响之后的匹配，不会回溯修改任务或移动文件。

只有存在可重试失败任务时，页面才启用“显式重新匹配失败任务”。`POST /api/v1/mikan/work-rules/{mikanid}/rematch` 再次校验当前规则 revision，只把没有运行租约的 `metadata_failed` 任务恢复到其安全的元数据入口；已解析、正在处理、已整理任务、Episode claim、完成记录和媒体文件均保持不变。规则已被其他操作修改时，本次重匹配整体冲突，不进行部分重置。

## 11. AnimeGoNetData 版本与更新

首页“数据版本与更新”区通过 `GET /api/v1/data-update` 读取当前调度策略、manifest 是否已配置、active/previous 指针、已安装版本、已验证下载包、最近本地导入和最近检查/下载运行。响应不返回 manifest/asset URL、磁盘路径或任何凭据。传输进度使用实际已下载字节与 manifest 总字节；未知总量时只展示字节数，不能伪造百分比。

手动动作与定时调度开关相互独立：

- `POST /api/v1/data-update/check` 只检查并验证 manifest；
- `POST /api/v1/data-update/download` 下载并完整校验资产，但不切换 active；
- `POST /api/v1/data-update/update` 下载、校验并事务导入；
- `POST /api/v1/data-update/downloads/{dataVersion}/import` 导入已经验证的本地下载包，不再次联网；
- `POST /api/v1/data-update/offline/import` 以原始 `application/zip` 请求体上传离线包，并在相同校验通过后事务导入；
- `POST /api/v1/data-update/rollback` 原子切换到 previous。

未配置 manifest 时检查、下载和在线更新按钮禁用；没有 previous 时回滚按钮禁用。执行期间同一区域所有写动作禁用，完成后重新读取服务端状态。回滚必须二次确认。只有完整校验并成功提交 SQLite 事务的数据版本才显示为 active；失败继续显示旧 active 及稳定失败码。关闭 `data_update.enabled` 只关闭 Cron 注册，页面仍允许手动操作。

离线导入不要求 manifest URL。页面只接受一个 ZIP，并在请求建立后立即清空文件选择；不保存上传文件名、本机路径或 ZIP 本体。ZIP 根目录必须严格等于 `manifest.json + assets[].file_name`，服务端拒绝额外条目、目录、路径穿越、重复名称、长度或 SHA-256 不符。上传先进入 `data_path/data-update/.partial-*`，成功后只保留已验证包目录；失败清理 partial 且不改变 active。

“编辑应用配置”对话框同时提供 data update 开关、六字段 Cron、Manifest URL、自动下载、自动导入、保留版本数和 HTTP 超时。保存前先显示脱敏字段 diff 与“即时生效/重启生效”，明确确认后才写 `data_path/config/application.private.json` 的 revision 私有覆盖并备份旧 revision，不直接改写部署 YAML；被环境变量覆盖的输入禁用并显示变量名。服务端校验通过后立即替换共享运行策略和 `animegonet-data-update` 调度：启用时 Manifest URL 必填，修改 Cron 立即重新计算下一次执行，禁用立即移除任务，恢复部署默认值也立即生效。若同次还修改 TMDB 等非热加载字段，响应保持 `restart_required=true`，但 data update 部分仍已即时生效，页面会明确区分两者。

## 12. 实时日志

首页通过同源 `/websocket/log` 接收服务端已脱敏日志。协议保留上游
`{"type":"log","count":N}\n\n<line>...` 帧，因此旧客户端仍可消费；新增
`control` 确认帧只用于静态 WebUI，旧客户端可按既有逻辑忽略。

- access-key 配置开启时，WebSocket 与 `/api` 使用同一鉴权；页面只把当前 URL
  中已有的旧 hash 放入 upgrade query，不回显、不记录、不写入本地存储。
- pause/resume 是逐浏览器连接状态，不会暂停其他管理员。暂停后服务端保存最新
  1000 条，溢出丢弃最旧；恢复时按一个兼容 log 帧补发。
- 浏览器只保存最新 500 条并可选择 Trace、Debug、Information、Warning、
  Error 或 Critical 最低级别。过滤只改变显示，不改变服务端采集。
- 所有日志行通过 `textContent` 创建 DOM，绝不解释成 HTML。服务端先规范化
  换行，并脱敏 URL path/query、Bearer、Cookie、Authorization、password、
  passkey、api key、access key 和 token；异常只输出类型与脱敏后的 message。
- 非预期断开使用 1～30 秒指数退避自动重连；“重新连接”按钮立即重建连接。
  页面重连时会恢复原暂停意图；关闭页面会取消重连并关闭 socket。

## 13. 外部 C# 插件运行状态

首页“外部插件”区直接消费 `GET /api/v1/status.external_plugins`。每个有效包显示
安全的 ID、名称、类型、版本、当前 RID 与声明能力，并将运行状态明确区分为未启动、
正在启动、运行中、故障退避和已自动禁用。发现失败的包单独显示目录 basename、稳定
错误码和安全诊断；页面不显示包绝对路径、入口路径、插件数据路径、stderr、环境变量
或配置内容。

存在连续故障时，页面显示计数、稳定失败码和可重试时间，并提供“清除故障状态”。该
按钮调用 Access-Key 保护的 `POST /api/v1/plugins/{id}/reset`，关闭旧会话并清除退避/
自动禁用；它不会立即执行插件、改写或删除包，也不会伪造“已启用”。刷新按钮重新读取
运行状态和 `GET /api/v1/plugins` 配置 revision。

“启停与参数”按每个包的 `config.schema.json` 生成 string/password、enum、boolean、
integer/number 与 JSON 容器控件。args 是非凭据 JSON 对象且只作为任务缺省值；vars
通过 schema 校验后传入协议 config。`writeOnly` 值永不回显，页面只显示“已配置”；
密码框留空时保留旧值，勾选“清除已保存值”才删除。保存使用全局 revision 防止多页面
覆盖，并停止旧会话；“恢复默认禁用”明确确认后删除该插件私有配置，不删除插件包或
plugin-data。

类型为 `source` 的有效外部包也进入“来源”页面的 adapter 下拉。只有已启用包可用于
新建 profile；已存在 profile 对应包后来被禁用或移除时仍显示原 ID 和明确状态，不会
静默改成 Mikan/U2/TTG。服务端创建时从实际 `PluginCatalog` 验证 adapter，路由预览走
同一个强类型 adapter；默认禁用的外部包会安全返回不可用，不启动进程或产生任务。

## 14. TypeScript 工程与 API client

WebUI 使用 TypeScript 7 strict 编译为浏览器原生 ES module，不引入 React、Vue、
Angular 或客户端运行时框架。`api-client.ts` 是现代 JSON API 的共享边界：调用点声明
响应与请求体类型，client 统一序列化 JSON、传播 `AbortSignal`、携带页面已有的旧
`Access-Key`，并把结构化失败投影为稳定的 `ApiHttpError`。

客户端只接受以单个 `/` 开头且不含反斜杠的同源路径，在进入 `fetch` 前拒绝绝对 URL、
协议相对 URL和浏览器可重解释的反斜杠 host，防止 Access-Key 被发送到外部来源。失败
响应只读取类型正确的 `code/message/errors` 字段；HTML、畸形 JSON 或错误字段类型只
显示 HTTP 状态，不把代理页或任意响应正文放进 DOM。成功但不是 JSON 时返回稳定协议
错误，`204` 可作为类型化 `void` 操作。

运行状态、外部插件配置和目录数据库状态已经使用该 client。其余旧页面请求可按功能
模块逐步迁移，不改变服务端契约。`npm run web:test` 先确定性编译，再使用 Node 内置
runner 验证同源门禁、凭据/header、请求体、取消传播、结构化失败、不可信正文和 204；
CI 同时检查 `app.js` 与 `api-client.js` 必须和 TypeScript 源码一致。

## 15. 缓存浏览与精确删除

“缓存浏览”只展示 SQLite `cache_buckets/cache_entries` 的安全管理投影。用户可在
`bolt` 与只读 `bolt_sub` 之间切换、选择 opaque bucket、分页查看 opaque key、JSON
字节数、更新时间和过期时间。原始 bucket/key/value、数据库文件路径、业务表字段和
凭据都不进入 API 响应，因此页面也无法显示或复制这些值。

`bolt` 条目提供“删除此缓存项”，点击后还需浏览器明确二次确认。请求只携带 opaque
bucket/key ID 和当前删除 token；服务端事务内确认 token 仍对应同一个 key/value/TTL/
更新时间后才删除一条 cache entry。期间发生刷新或覆盖会返回稳定冲突并强制页面重新
读取；`bolt_sub` 只显示“只读”，从 API 和页面两层都不提供删除。此功能不开放 SQL、
整 bucket 删除、任意业务表修改或文件操作；业务记录和媒体删除继续只走四类删除中心。
