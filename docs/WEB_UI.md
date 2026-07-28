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

## 6. 最小查询投影

服务端季度列表投影至少包含：

- `tmdbSeriesId`、`tmdbSeasonNumber`、`displayName`、`sortName`、`seasonName`；
- `posterUrl`、`posterSource`；
- `airDate`、`addedAt`、`lastUpdatedAt`；
- `episodeTotal`、`episodeDownloaded` 和逐 EP 状态（详情接口可延迟加载）；
- Series/Season 的 `resolutionSource`、验证状态和最近解析运行 ID；
- 元数据/文件一致性警告摘要。

数据库查询必须批量聚合完成记录，禁止列表按条目或按 EP 产生 N+1 查询。

## 7. 当前任务状态投影

在完整作品库落地前，首页提供只读的“匹配与整理状态”任务投影，用于观察统一导入到元数据解析的实际进度。`GET /api/v1/metadata/tasks` 当前返回标题、来源、任务状态、`mikanid`/Bangumi/TMDB Series/Season、最近成功的 Series/Season/Episode 策略、Episode/Duplicate/Other/Pending 文件计数、脱敏失败分类与原因，以及最后更新时间。

该投影不返回 Torrent URL、passkey、下载器凭据或文件绝对路径。查询通过单条聚合 SQL 批量产生，避免逐任务读取策略或文件计数。只有已进入 `metadata_failed` 且没有活动租约的任务显示“显式重新匹配”，调用既有重试 API 后刷新状态；它不是自动重试开关，也不会覆盖人工规则。

当前面板属于运维任务视图，不等同于第 1～6 节定义的 TMDB 作品库：文件归类计数不能用作季度完成比例，尚未实现的筛选、策略尝试时间线、作品 CRUD、Cover 和 TMDB EP 网格仍按 TODO 独立验收。

可信 Mikan offset 面板读取 `/api/v1/mikan/trusted-offsets`，显示 `(mikanid, groupid)`、TMDB Series/Season、带符号 offset、Learning/Trusted/ConflictReset 和不同 EP 进度。清理操作只调用目标键 DELETE，并在确认文本中明确排除人工规则、完成记录与媒体文件。

## 8. 当前生效配置投影

首页通过 `GET /api/v1/config` 展示当前进程实际使用的三目录、容器/后台 worker/Access-Key 状态、TMDB 端点与语言、季度失败链、AI 两个独立开关和 600 秒默认超时、Bangumi 完全兜底、Mikan 可信 offset 缓存以及 Torrent HTTP/暂存限制。

该接口只返回 `api_key_configured`、`read_access_token_configured` 和 `access_key_configured` 布尔值，绝不返回凭据内容；仍受统一 API 鉴权保护。目录标明修改需要重启。页面提供带 revision 的私密覆盖编辑和恢复部署默认操作，密钥输入为空表示保留，另有明确清除选项；保存后持续显示 saved/applied revision 差异。

编辑器使用单独的 `editable` 投影。服务端以未应用私密覆盖前的部署基线加当前持久化覆盖计算期望值，因此保存后未重启、或移除覆盖后再次打开编辑器，都不会把旧进程内存中的值误当成部署默认。配置来源和环境变量覆盖提示仍按 TODO 继续实现。
