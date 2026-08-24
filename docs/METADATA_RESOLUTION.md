# 元数据 ID 与 TMDB/Bangumi 兜底

## 1. ID 定义

内部模型禁止使用含糊的 `AnimeEntity.ID`：

- `Id`：AnimeGoNet 自增/内部主键。
- `SourceId`：输入源 profile ID，例如 `mikan`、`u2`；不是站点条目 ID。
- `SourceItemId`、`SourceWorkId`：可空的站点条目/作品标识，用于审计和通用作品规则。
- `MikanId`：Mikan RSS 和页面 URL 中的作品 ID，统一简称 `mikanid`。例如 `https://mikanani.me/Home/Bangumi/3951` 的 `mikanid` 为 `3951`；旧实现或上游数据中的 `bangumiId` 仅作为兼容输入名称，不在新 UI 中使用，避免与 Bangumi.tv Subject ID 混淆。
- `BangumiSubjectId`：Bangumi.tv Subject ID，对外兼容字段仍为 `bangumi_id`/`bgmid`。
- `AniDbId`、`ImdbTitleId`：可空的作品级辅助标识，分别对应 AniDB 正整数 ID 和 IMDb `tt...` Title ID；不代表具体 TMDB Episode。
- `TmdbSeriesId`：TMDB TV Series ID，对外兼容字段仍为 `themoviedb_id`/`tmdbid`。
- `SourceEpisodeNumber`：从种子/媒体文件名解析的来源集号，只用于匹配和审计。
- `TmdbSeriesName`：TMDB `zh-CN` 剧集名，缺失时使用 TMDB `original_name`。
- `TmdbSeasonNumber`、`TmdbEpisodeNumber`、`TmdbEpisodeId`：经 TMDB API 验证的媒体库规范定位。
- `TmdbSeriesResolutionSource`、`TmdbSeasonResolutionSource`、`TmdbEpisodeResolutionSource`：分别记录 TMDB 剧集、季度和集号在哪个阶段取得，供审计与 Web UI 展示。

处理顺序固定为：解析 `mikanid` → 应用作品级人工规则 → 解析或覆盖 Bgm ID → 读取 Bangumi 元数据 → 解析 TMDB → 季度/AI fallback → TMDB Series/Season/Episode 验证 → 下载 → 按 TMDB 规范重命名/刮削。

### 1.1 Mikan 作品级人工规则

人工规则是元数据解析的最高优先级，优先于已有自动缓存、TMDB 自动搜索、Backtrace、AI、标题季度和第一季 fallback。来自 Mikan RSS 或 Mikan 页面链接的任务必须解析出正整数 `mikanid`。

相同 `mikanid` 的 RSS 条目、不同字幕组、Torrent 和下载任务视为同一部作品，共用一条规则：

- 对应的 `BangumiSubjectId`。
- 统一的 `TmdbSeriesId`。
- 统一的 `TmdbSeasonNumber`，只允许大于 0 的普通季度。
- 统一的有符号整数 `EpisodeOffset`。

普通正片按 `TmdbEpisodeNumber = SourceEpisodeNumber + EpisodeOffset` 计算。例如来源 EP 1 对应 TMDB EP 13 时，偏移为 `+12`。结果必须大于 0 且真实存在于指定 TMDB Season；偏移不应用于非整数来源集号，也不应用于 Menu、OVA、PV、NCOP、NCED 等进入 `Other` 的文件。

规则以 `mikanid` 为唯一作品键，而不是标题、字幕组、Torrent hash 或 `bgmid`。`bgmid` 是该作品规则的关联字段和查询上下文，不替代规则主键。人工规则保存时验证 TMDB TV Series、Season 和可用的样例 Episode，运行时仍验证最终 Episode。

`PUT /api/v1/mikan/work-rules/{mikanid}` 可选接收正整数 `sample_source_episode`。提供时必须同时提供有效 `tmdb_series_id`、普通 `tmdb_season_number` 和 `episode_offset`，服务端以 `sample_source_episode + episode_offset` 计算目标并依次调用 TMDB TV Series、Season、Episode 官方端点验证；目标非正数、身份不一致或 Episode 不存在均在写规则前拒绝。网络/远端失败返回可重试的安全错误码，任何失败都不增加 revision、不留下部分人工覆盖。未提供样例字段时保持原兼容契约，最终任务运行时仍逐集验证。

规则无效时记录 `ManualOverrideInvalid` 并停止当前项，不能由自动策略静默覆盖。只有用户修正规则、禁用规则或明确清除人工覆盖后才恢复自动解析。规则修改影响新任务和用户显式重新匹配的任务，不静默移动已整理完成的媒体文件。

### 1.2 TMDB 获取阶段

作品详情不能只显示最终 `tmdbid`，必须分别保存和显示三个解析来源：

- Series：`ManualMikanRule`、`AutomaticSearch`、`AI`、`None`。
- Season：`ManualMikanRule`、`DirectDateMatch`、`Backtrace`、`AI`、`TitleSeason`、`FirstSeason`、`None`。
- Episode：`ManualOffset`、`DirectNumber`、`BangumiDeterministicMatch`、`AI`、`Other`、`None`。

每个来源同时关联解析运行 ID、策略尝试 ID和取得时间。Bangumi 完全兜底时 TMDB 三层来源均为 `None`，另显示 `BangumiFallback` 状态，不能伪装成某种 TMDB 获取方式。Web UI 的作品详情页固定展示“TMDB 获取方式”卡片，包含 Series/Season/Episode 来源、人工规则 `mikanid`、偏移值、验证状态和最后解析时间。

确定性 Series/Season 搜索不是“每个名字只取首个 Series”。顺序固定为：

1. 先用 Bangumi `name`（通常是日文原名），穷尽原始标题与四步上游后缀清理产生的不同搜索词；再对 `name_cn` 执行同样流程。重复搜索词不重复请求。
2. 每个 TMDB 搜索响应内，先按原名/本地化名精确匹配、UTF-8 byte 相似度和响应顺序稳定排序，再逐个检查所有达到阈值的 TV Series。
3. 每个候选都必须取得身份一致的 Series details，以 Bangumi 季度首播日期选出首播日期相差不超过 `±1` 个日历日的普通季度，再从官方 Season endpoint 验证 `tmdbid + Season Number`。候选详情、日期季度或 Season endpoint 任一不成立，只拒绝该候选。
4. 当前响应内还有候选时继续候选；响应穷尽后继续当前名字的下一条清理搜索词；该名字穷尽后才切到另一个名字。只有完整 Series+Season 验证成功才立即停止。认证、配置、网络、远端服务或协议失败不会伪装成“无匹配”并继续低优先级语义搜索。

例如 `name="Re:ゼロから始める異世界生活 4th season 喪失編"`、`name_cn="Re：从零开始的异世界生活 第四季 丧失篇"` 时，日文原始词选出的 Series 如果季度验证失败，仍会尝试清理后的 `Re:ゼロから始める異世界生活`；日文轮次全部穷尽后再尝试中文名。中文搜索返回多个同名 Series 时也逐个做季度验证，不会因第一个失败而提前进入 P4/P3/P2/P1。

### 1.3 已下载记录与重复集

重复集/多版本采用“第一个成功记录生效”，不实现自动多版本管理。规范去重键固定为 `(TmdbSeriesId, TmdbSeasonNumber, TmdbEpisodeNumber)`，作用域是整个媒体库，不区分 Mikan、U2、字幕组、Torrent 或下载器实例。同一 TMDB 剧集不整体阻断：只跳过已经完成的具体 Episode，其他 Episode 正常处理。

只有下载、文件策略、重命名和必要的 NFO/目录数据库写入全部成功后，才为每个 Episode 原子写入 `Downloaded=true` 的完成记录。失败、取消、只完成下载但整理失败的任务不能占用“第一个”资格。

为支持 RSS 早停，每次成功解析还保存 `(source_id, source_work_key, 来源Episode类型, 来源Episode)` 到规范 TMDB Episode 的 alias。再次遇到已知 alias 时可在 RSS 解析阶段直接停止该文件；alias 未命中时先完成 TMDB 映射，再在提交下载器前检查全局规范键。`mikanid`、bgmid/anidbid/imdbid、Torrent hash 和来源集号保留用于审计，但都不能替代最终全局去重键。

多文件 Torrent 按文件级处理：已下载 Episode 对应的视频及其已绑定字幕标记为 unwanted，未下载 Episode 和对应字幕继续。qBittorrent 使用“暂停添加 → 等待/校验 metadata → 设置文件 priority → 恢复”；必须按相对路径和容量核对索引，不能仅按数组位置。所有正片 Episode 已完成且没有其他明确需要处理的文件时，才跳过整个 Torrent。

当前实现已在 TMDB Episode 官方验证完成的同一 SQLite 事务中获取 `EpisodeClaim`：同一任务内映射到同一 Episode 的视频和字幕共享一个 claim；已有规范完成记录时标记 `episode_already_completed`，其他任务持有活动 claim 时标记 `episode_claimed_by_another_task`，并且只把对应 `task_files` 置为 `duplicate`，同任务其他 Episode 不受影响。整理全部成功写入 `CompletionRecord` 时 claim 原子转为 `completed`，整理失败可按 claim 所属 `task_file_id` 显式释放。claim 不按超时自动接管，崩溃恢复仍须核对下载器与文件状态。

这一增量关闭了“验证完成到整理完成”之间的 SQLite 并发窗口，但当前元数据 worker 仍位于下载完成之后。把相同门禁前移到 qBittorrent 恢复下载之前、按路径和容量设置 unwanted/priority，仍属于下载编排模块，不能把当前 `duplicate` 标记误报成已节省网络下载。

TMDB 完全失败、`tmdbid=0` 的 Bangumi 兜底没有规范 TMDB Episode 键，不能参与 TMDB 级全局去重，但不等于完全不记录下载历史。每个完整成功的兜底文件都必须原子写入 `FallbackCompletionRecord`，并按当前可证明的最强身份建立唯一键：

1. 已可靠解析到 Bangumi Episode ID 时，使用 `(bgmid, bangumi_episode_id)`；这可以在持有同一可靠 Bangumi Episode 身份的来源之间去重，但不能把来源集号直接当 Bangumi Episode ID。
2. 否则 Mikan 使用 `(source=mikan, mikanid, 来源Episode类型, 规范化来源Episode)`，使相同 `mikanid` 下的不同字幕组、RSS 条目和 Torrent 不会重复下载同一来源集。
3. 其他来源使用 `(source_id, source_work_key, 来源Episode类型, 规范化来源Episode)`；若 Episode 身份也无法可靠解析，只能按 source item ID、Torrent info-hash 和文件指纹阻止同一输入重复处理。

fallback 唯一键必须包含身份类型，不能把不同编号体系拼进同一命名空间。命中记录时必须在 qBittorrent 恢复下载前停止对应文件；多文件 Torrent 仍逐文件处理。上述第二、三档不能保证跨来源、跨编号体系识别同一真实 Episode，因此 Web 必须显示当前去重范围和“可能跨来源重复”的风险，不能宣称全局去重。为了避免误伤，系统也不能仅凭相同标题、容量或来源集号跨来源阻断。

当前自动 fallback 会在确认 bgmid 后读取 Bangumi Episode 列表。只有来源集号是正普通整数，且在 `type=0` Episode 中唯一命中一个正 Bangumi Episode ID 时，才使用 `bangumi_episode` 作为最高优先级 scope；Mikan/U2 因而可共享该可靠身份。小数、特别篇、文本集号、重复编号、无结果或 Bangumi Episode API 错误都不升级身份，继续使用 mikan/source/torrent 的保守边界。API 错误会写元数据 attempt 审计但不阻止已获准的 Bangumi 完全兜底。

为关闭并发窗口，在 qBittorrent 恢复下载前必须用 SQLite 唯一约束和事务为每个 fallback 键创建 `FallbackEpisodeClaim`。同键只有一个活动 claim；随后到达的任务等待首项结果或以 `DuplicateInProgress` 早停，不能同时进入不同下载器。下载/整理失败时 claim 进入可重试失败态并按重试策略释放或接管；完整成功时在同一事务中转为 `FallbackCompletionRecord`。进程崩溃后的过期 claim 只能在核对下载器任务和文件状态后恢复，不能仅按超时直接再次下载。

当前实现已在 Bangumi 兜底元数据事务中按 `mikan_episode`、`source_work_episode` 或 `torrent_file` 最强可用 scope 获取 `fallback_claims`。来源 Episode 的大小写、空白和十进制格式先规范化；无可靠 Episode 时使用来源项、info-hash、任务内路径与容量生成 SHA-256 文件指纹。已有 completion 标记 `fallback_already_completed`，其他任务持有活动 claim 标记 `fallback_claimed_by_another_task`，两者都会由 download preparation 设为 qB priority 0；同任务同 scope 的视频/字幕共享 claim。整理成功写 completion 与 claim=`completed` 位于同一事务，瞬时整理失败保留活动 claim 并重试，明确放弃时可按 owner file 释放；不得按时间自动抢占。

后续补全真实 TMDB 映射时，在一个事务中把 `FallbackCompletionRecord` 合并为规范 `(TmdbSeriesId, TmdbSeasonNumber, TmdbEpisodeNumber)` 完成记录并保存原 fallback alias。若多个 fallback 记录或已有规范记录收敛到同一 TMDB Episode，保留最早的规范完成记录，其他记录标记 `DuplicateAfterResolution` 并进入人工处理；不得再次触发下载，也不得静默删除已经存在的文件。

schema v20 与 `PendingTmdbRecoveryStore` 已实现上述数据库事务边界。调用方必须提交已验证且内部一致的 TMDB Series/Season/Episode；事务按原兜底完成时间排序，首条创建规范 completion，其余或命中既有 completion 的记录写为 `duplicate_after_resolution`。每条记录都保存 `manual/automatic` 恢复来源、规范 completion 外键和 fallback scope alias，同时更新关联 `task_file` 的正式 TMDB 身份；事务不会创建下载任务、移动或删除任何文件。允许分批恢复，只有最后一条待补全记录完成后才移除 `tmdbid=0` 投影。

人工恢复端点已在提交事务前通过 `ITmdbClient` 逐项读取并验证 Series、Season、Episode 的 ID 与父级身份；任何不存在、不一致或 TMDB 网络/服务错误都不会进入数据库事务。详情只向客户端公开随机 fallback record ID、来源、来源集号和去重边界，不公开 scope key、媒体路径或下载凭据。

schema v21 在同一恢复事务中按原下载任务的不可变 `save_root_path` 入队 NFO 重写；缺失可信根目录时拒绝整个恢复，不能从 `media_path` 反推边界。后台 worker 使用五分钟租约和三十秒失败重试，在原兜底作品目录内以临时文件加覆盖 rename 原子写入真实 TMDB ID、对应 bgmid 和 TMDB 正式标题。进程崩溃后过期租约恢复，已完成作业不重复执行；写入不移动、重命名或删除现有媒体。

旧 YAML 的 `allow_duplicate_download` 字段仍可读取和迁移，但新程序不允许它绕过规范 TMDB Episode 完成记录；Web 标记为已弃用并解释需要先删除对应完成记录才能重新下载。这样不会因为旧配置中的 `true` 破坏跨来源全局去重。

Web UI 的“删除业务记录”必须细分出“删除已下载完成记录”。删除一个规范 TMDB Episode 完成记录时同时删除/失效它的所有来源 alias；不会隐式删除下载器任务、下载源文件或媒体库文件，但会解除所有来源的去重门禁。删除操作保存操作者、原键、alias、原状态、时间和关联文件快照，便于解释为何发生重新下载。

### 1.4 字幕随集整理

附属文件首版只处理字幕；字体、图片、校验文件、歌词、章节和其他附件不跟随移动。字幕扩展名至少识别 `.ass`、`.ssa`、`.srt`、`.vtt`、`.sub`、`.idx` 和 `.sup`，比较时不区分大小写；`.idx/.sub` 作为一组处理。

字幕按以下证据顺序绑定 Episode：

1. 与视频相同的相对目录，字幕 stem 等于视频 stem。
2. 去掉字幕 stem 尾部一个或多个语言/轨道标记后等于视频 stem，例如 `.zh-CN`、`.zh-Hans`、`.chs`、`.cht`、`.sc`、`.tc`、`.jpn`、`.eng`、`.default`、`.forced`、`.sdh`、`.cc` 及其组合。
3. 文件名可解析出来源 Episode，并且在当前任务内只对应一个已验证的视频 Episode。

只有得到唯一候选才绑定，存在多个候选时不得按文件顺序或容量猜测。字幕绑定成功后直接继承对应视频已验证的 TMDB Series/Season/Episode，不独立调用 AI，也不再次应用 Episode Offset。视频目标为 `E013.mkv` 时，`原视频名.zh-Hans.forced.ass` 重命名为 `E013.zh-Hans.forced.ass`；无法识别的原后缀也应原样保留在 `E013` 与字幕扩展名之间，避免多语言轨道互相覆盖。

未能唯一绑定的字幕在 Series 和普通 Season 已确认时，归类为 `Other`，保留原文件名放入 `<TmdbSeriesName>/Sxx/Extras/` 并记录 `SubtitleUnmatched` 或 `SubtitleAmbiguous`；季度未知时留在下载目录。其他非字幕附件首版始终留在下载目录，不放入 `Extras`。

## 2. 业务兜底配置

```yaml
advanced:
  default:
    # TMDB 数据完全获取失败时，是否允许使用 Bangumi 数据继续处理。
    # 默认关闭：失败即停止，不下载、不刮削、不生成 NFO。
    tmdb_fail_use_bangumi: false
```

这是业务流程开关，不是单纯的 NFO 字段开关。

默认 `false` 表示完全不兜底：TMDB 数据完全获取失败后沿用上游失败流程，当前项不能进入下载、重命名和刮削阶段，因此不会生成对应的 `tvshow.nfo`，也不存在“只写 `tmdbid=0`”的情况。

设置为 `true` 后，只有以下条件全部满足才允许继续：

- TMDB 搜索请求已经成功到达并取得可解析的权威响应，但在既定策略后仍确定性地无法得到有效 TMDB TV Series ID；最终失败类别必须为 `SemanticNoMatch`。
- 已取得有效 Bangumi Subject ID。

任一前置条件不满足，仍按失败、待确认或跳过处理，不生成失败 NFO。

完全兜底的季度不读取任务 `title`、Bangumi 名称或 P2/P1 开关，固定使用本地 `S01`，且不请求、不验证 TMDB Season。这个 S01 只是待补全目录范围，不得标记为 TMDB Season 来源。

“非网络原因”在此处使用白名单语义，不是简单判断 `retryable=false`。允许进入 `tmdbid=0` 的最终原因仅包括成功 TMDB 查询后的 `SeriesNotFound`、`NoAcceptableTvCandidate` 或等价的确定性无匹配。以下情况即使重试耗尽或表面上属于非网络错误，也一律禁止兜底：

- DNS、连接、代理、TLS、超时、取消或断路器开启。
- HTTP 408/429/5xx 及其他服务暂时不可用。
- API Key 缺失、401/403、endpoint/代理配置错误。
- 响应截断、格式/Schema错误、无法验证响应来源或解析失败。
- 输入缺失/非法、人工覆盖无效、候选仍有歧义等需要配置或人工修复的情况。

先前尝试发生过瞬时网络错误、但后续重试成功取得有效 TMDB 响应并最终确定无匹配时，可以按最终 `SemanticNoMatch` 判断。反之，仅 AI/MCP 返回“未找到”而权威 TMDB 请求从未成功，不能视为非网络无匹配。

## 3. 状态与输出语义

| TMDB 结果 | Bgm ID | 兜底季度 | 开关 | 结果 |
|---|---:|---:|---:|---|
| 成功 | 有效 | 有效 | 任意 | 使用 TMDB 名称/Season/Episode 继续；`tvshow.nfo` 默认只写真实 `tmdbid`，仅在 `write_bangumi_id_when_tmdb_matched=true` 时同时写 `bangumiid` |
| 确定性完全无匹配（`SemanticNoMatch`） | 有效 | 固定 `S01` | `true` | 使用 Bangumi 兜底继续；`tvshow.nfo` 写 `tmdbid=0` + `bangumiid` |
| 网络/服务/认证/配置/协议失败 | 有效 | 不适用 | `true` | 禁止兜底；进入重试或配置/人工修复，不下载、不生成 NFO |
| 完全失败 | 有效 | 不适用 | `false` | 原失败流程；不下载、不刮削、不生成 NFO |
| 完全失败 | 缺失 | 任意 | `true` | 无法兜底；失败/待确认，不生成 NFO |
| TMDB ID 有效、仅季度失败 | 有效 | fallback 后有效 | 任意 | 不属于完全失败；正常继续并写真实 `tmdbid`；是否附加 `bangumiid` 仍由显式开关决定 |

本节的 `tmdbid`、`bangumiid` 都指 NFO XML 标签内容。内部 SQLite 分别保存来源值、TMDB 规范值、失败原因和待修复状态，不用 NFO 的 `0` 代替内部身份信息。

当前主程序在权威 Series `SemanticNoMatch` 后先尝试已启用的统一 AI 元数据恢复；仍失败时才评估 Bangumi 兜底。兜底任务在 `anime_series` 保存 `tmdb_series_id=0 + bangumi_subject_id + needs_tmdb_completion=1`，但 `task_files.tmdb_series_id` 和 `metadata_resolution_runs.tmdb_series_id` 保持 `NULL`，避免把例外值混入规范 TMDB 身份。文件只携带固定的本地 `S01` 范围并以 `Other/tmdb_fallback_pending_completion` 整理，不生成 TMDB Episode 进度或规范 completion record。

## 4. NFO 文件和内容

ID 写入动画剧集根目录的 `tvshow.nfo`；不写入季度目录的 `season.nfo`。AnimeGoNet 默认只在 Bangumi 完全兜底时写 `bangumiid`，避免多个 bgmid 映射到同一 TMDB Series 根目录时被后续任务覆盖。该默认值是按项目当前业务语义对 AnimeGo `develop` 无条件写入行为的明确调整。

TMDB 成功：

```xml
<tvshow>
  <tmdbid>72517</tmdbid>
</tvshow>
```

显式开启 `metadata.write_bangumi_id_when_tmdb_matched` 后，TMDB 成功的 NFO 才额外写 `<bangumiid>`。WebUI 会提示同一 TMDB Series 目录被不同 bgmid 共用时的覆盖风险。

TMDB 完全失败且业务兜底成功：

```xml
<tvshow>
  <tmdbid>0</tmdbid>
  <bangumiid>371546</bangumiid>
</tvshow>
```

`0` 明确告诉 Jellyfin 不使用 TMDB provider ID，同时保留对应 Bangumi Subject ID，供支持 `bangumiid` 的元数据提供方处理。兜底关闭时没有失败 NFO 示例，因为该流程不会进入 NFO 写入阶段。

## 5. “TMDB 完全失败”的边界

- 搜索确定无结果：判定完全失败。
- 搜索得到候选，但无法取得有效详情或 Series ID：在重试耗尽后判定完全失败。
- 429、超时、连接失败、5xx：先按请求配置重试，耗尽后才判定完全失败。
- 401/403、API Key 缺失：属于系统配置错误，记录明确告警；不得绕过默认关闭的兜底设置。
- 已取得有效 TMDB Series ID、只因首播日期或季度匹配失败：不是完全失败，执行原季度 fallback。

开启兜底后生成的 NFO 可标记为待修复。后台任务后续取得真实 TMDB ID 时，原子地用真实值替换 `0`；更新失败时保留原文件。

## 6. 季度 fallback

配置模型和 YAML 字段为：

```yaml
advanced:
  default:
    # TMDBFailSkip，优先级4
    tmdb_fail_skip: false
    # TMDBFailBacktrace，优先级3
    tmdb_fail_backtrace: false
    # 统一 AI 元数据匹配；独立阶段，不占确定性优先级
    ai_use_metadata_match: false
    # TMDBFailUseTitleSeason，优先级2
    tmdb_fail_use_title_season: false
    # TMDBFailUseFirstSeason，优先级1
    tmdb_fail_use_first_season: false
```

季度策略按数值从高到低执行：

1. `tmdb_fail_skip=true`：优先级 4，立即跳过当前项，不执行其他策略。
2. `tmdb_fail_backtrace=true`：优先级 3，AnimeGoNet 沿 Bangumi“前传”关系逐项回溯；每个前作重新联合匹配完整 `tmdbid + Season`，成功即采用，耗尽则继续下一策略。
3. `ai_use_metadata_match=true`：独立可选阶段，使用下载任务总标题、候选视频的相对文件名/字节容量及可空作品级 `bgmid`/`anidbid`/`imdbid` 请求大模型，一次返回整个任务的 TMDB Series/Season/Episode 候选。非空 ID 已绑定当前任务，但跨站标题、季度和 EP 编号可能不同，只能作参考；结果必须通过官方 TMDB API 二次验证，详细协议见 [`AI_METADATA_MATCHING.md`](AI_METADATA_MATCHING.md)。
4. `tmdb_fail_use_title_season=true`（`TMDBFailUseTitleSeason`）：优先级 2。前面策略全部失败后，只把统一导入任务的 `title` 交给本地标题解析器；解析出正季度后直接使用该本地季度，不验证 TMDB Season，解析不到时继续 P1。
5. `tmdb_fail_use_first_season=true`（`TMDBFailUseFirstSeason`）：优先级 1。前序策略全部失败后直接使用本地 `S01`，不验证 TMDB Season。
6. 所有启用策略都没有结果：进入待确认/解析失败。

除 `Skip` 外，某个已启用策略返回“未匹配”或在自身重试耗尽后失败时，必须记录本次尝试并继续较低优先级；成功后立即停止。`Skip` 是显式终止策略，命中后不得执行较低优先级。未启用、因前置条件不满足而不适用、执行后未匹配、执行出错和成功必须是可区分结果，不能都记成“失败”。

### 6.1 回溯算法边界

- 正常确定性搜索按 Bangumi 日文原名、中文名顺序执行。每个名字先搜索原文，再依次执行四级后缀清理；同一名字中未发生变化的重复搜索词不再次请求。
- 每个搜索词返回的合格 Series 按“名称精确匹配、相似度降序、TMDB 返回顺序”稳定排列，并逐个读取官方 TMDB 详情，以当前 Bangumi Subject 的首播日期匹配普通 Season。只有 `tmdbid` 与 Season 同时通过验证才停止；本轮全部候选均失败后继续同一名字的后续清理轮次，再继续中文名。
- P3 需要有效 `bgmid`，但不要求当前搜索已经取得 `TmdbSeriesId`。当前作品的 `tmdbid + Season` 联合匹配失败后，从当前 `BangumiSubjectId` 查询“前传”关系。
- 每个前作作为新的完整匹配节点，按其日文原名、中文名和首播日期重新执行 Series 搜索、官方详情读取和普通 Season 日期验证；不得锁定当前候选的 `tmdbid`，允许前作命中不同的 TMDB Series。
- 任一前作的 `tmdbid + Season` 同时验证成功即结束回溯；没有前传即视为回溯到首部，转入较低优先级策略。
- 前传缺少首播日期时不参与日期匹配，但仍可继续查询它的前传。
- 多个前传按关系距离由近到远遍历；同层按首播日期降序、Subject ID 升序稳定排序，保证相同输入结果确定。
- 使用 visited Subject ID 防止关系环和重复请求；所有请求支持取消。关系读取失败先按数据源策略重试，耗尽后记录独立 `BacktraceError` 并继续较低优先级策略，不能伪装成正常的“回溯耗尽”。

无论当前搜索是“已有有效 ID 但季度失败”还是“两个名字均未找到可验证 Series”，只要已有 `bgmid`，P3 都可以尝试恢复完整 `tmdbid + Season`。只有后者最终仍没有有效 TMDB Series、失败为权威 `SemanticNoMatch` 且 `tmdb_fail_use_bangumi=true` 时，才进入 `tmdbid=0` 例外路径；该路径使用 Bangumi 名称和固定本地 `S01`，并必须在状态/UI 中明确标记为非 TMDB 规范命名。P2/P1 仍要求已有有效 TMDB Series，因为两者只提供本地 Season Number。

### 6.2 普通 EP 校验与统一 AI 补全

确定性流程已确认 Series/Season 后，先执行普通 Episode 校验：

1. 在已确认的 TMDB Season 中读取完整 Episode 列表。P4/P3 的日期候选只用于选定季度，成功前必须再请求官方 `/tv/{series}/season/{season}` endpoint。Series/Season 确认阶段只提交季度身份和 `episode_count`；完整响应可以进入 TMDB HTTP 成功缓存，但不得提前写入作品库的正式 `tmdb_episodes` 投影。Episode Worker 完成确定性判断、必要的统一 AI 匹配及最终 TMDB 验证后，才在同一事务替换正式 Episode snapshot 并提交逐文件结果。待补全 TMDB 的人工恢复同样只使用已验证 Season 响应保存 snapshot，不能用 `episode_count` 自行生成不存在的 Episode。
2. Mikan 任务存在 `bgmid`、且没有命中人工 offset、可信 offset 或更高优先级预解析结果时，先用单集首播日期确定对应关系。主程序从文件名重新解析普通正整数 EP，用它匹配 Bangumi 普通 Episode 的局部 `ep` 或全局 `sort`；当前 Subject 无该集号时，只读取其直接 `续集` 关系并继续查找。若活动 AnimeGoNetData 已包含目标 Episode 身份、但该目标的 `airdate` 为空，主程序会绕过 Archive 在线刷新该 Subject 的完整 Episode 列表后再判断；其它无关 Episode 日期缺失不会触发刷新，已有目标日期也不会重复联网。Bangumi `airdate` 与已确认 TMDB Season 的普通 Episode `air_date` 允许 `±1` 个日历日，取距离最近且可唯一消歧的候选。
3. 上一步失败且 Torrent 实际文件总数恰好为 1 时，才启用文件名补判：以文件名 EP 定位 Bangumi 普通 Episode，在已确认 TMDB Season 中寻找首播日期最近的普通 Episode。最近日期差必须不超过 7 日，并且最近候选的 TMDB Episode Number 必须与文件名 EP 一致；否则不得确定性接受。该补判成功记录为 `tmdb_episode_bangumi_nearest_date`。
4. 超过 7 日、最近候选编号不一致、日期/身份缺失、多个最近候选无法消歧，或实际多文件任务的 `±1` 日主匹配失败，都进入同一个任务级 AI 匹配。AI 开关关闭或 AI 无法验证时，文件进入已确认季度的 `Other`。候选成功后仍须通过 TMDB Episode endpoint 验证 Series/Season/Episode 身份。
5. Torrent `published_at` 不属于季度或 EP 的确定性日期校验，也不能替代 Bangumi 单集首播日期；它仅在 Mikan 的统一 AI 输入中保留为辅助参数。
6. 主日期映射成功时使用 `tmdb_episode_bangumi_date` 记录策略。它可以把来源 EP 6 映射到 TMDB EP 56，也可以用 Bangumi `sort=45` 映射 TMDB S02E21，因此不能先因 TMDB 中存在同号来源 EP 就直接接受。单文件最近日期补判则要求 TMDB EP 与文件名 EP 同号，并记录独立策略。
7. 非 Mikan 或没有 `bgmid` 时，才检查与文件名普通整数候选同号的 TMDB Episode；不存在已知日期冲突时可采用并记录 `tmdb_episode_number`。
8. AI 必须返回与已确认值相同的 `tmdb_id` 和 `season_number`，且目标 Episode 必须存在；AI 试图更换 Series/Season 时拒绝。开关关闭、该任务已尝试 AI、AI 失败或 TMDB 二次验证失败时进入 `EpisodeUnmatched`，不得退回来源 EP；普通季度已确认时按 `Other` 规则整理，季度未知时不移动。

“一次”指一个下载任务在季度和 Episode 全流程合计最多一次语义匹配，不对无效答案继续改写 Prompt 追问。季度阶段已经成功或失败尝试 AI 后，Episode 阶段均不得再次请求。没有收到响应时可按网络策略幂等重试同一请求；重试不得改变任务标题、文件列表、Prompt 或已确认的 Series/Season。

AI 输入经过显式白名单数据边界：仅投影任务 `title`、候选视频相对文件名/字节容量、
可空 `bgmid`/`anidbid`/`imdbid`、实际 Torrent 文件数，以及通过 Mikan 单文件门禁后
取得的发布日期/候选 Bgm EP。元数据 claim 的 run/task/lease、来源 adapter/raw 日期等
编排字段不会进入输入；SQL claim 本身也不读取 Torrent URL 指纹。完整 Torrent URL、
passkey、announce、info-hash、暂存字节、route snapshot、Cookie、Authorization 和
下载器凭据在该类型中没有字段，Prompt renderer 只逐项读取上述白名单。结构契约测试
会在输入 record 新增字段时失败，统一导入 E2E 另从 SQLite 读取实际 URL fingerprint，
验证 URL、passkey 和 fingerprint 均不出现在 matcher 输入或最终 Prompt。

Mikan RSS 可在统一 AI Prompt 中增加单文件发布日期提示。配置开关开启后，主程序仍只在 Torrent 实际文件条目数恰好为1、`bgmid` 和合法内部 `pubDate` 同时存在且没有命中更高优先级人工 Episode Offset 时尝试计算；Torrent单文件模式和根目录下仅一个文件均满足，目录节点不计数，但字幕、图片及已标记为 ignored/duplicate 的实际文件条目都计数。`pubDate` 无时区时按 Mikan SourceProfile 默认 `Asia/Shanghai` 解析。主程序可从在该发布时间之前已经播出的 Bangumi 普通正整数 Episode 中选取最近项，写入 `bgm_episode_candidate` 供 AI 参考，但不设置 Torrent 发布延迟的通过/拒绝窗口，也不以该候选直接确认 TMDB 集号。查询失败或候选为空时最终门禁保持 false，继续原通用 AI 流程；门禁为 true 时，Prompt 也只能把该候选和文件名集号当作辅助证据，最终 Series、Season、Episode 仍须 TMDB 验证。

AI 和确定性匹配均不接受 Season 0。Series 和大于0的普通季度已经确认、但 Episode 无法确认时，不用来源集号冒充 TMDB Episode；该文件以业务分类 `Other` 保留原名进入 `<TmdbSeriesName>/Sxx/Extras/`，并保存未匹配原因。季度也无法确认时保留在下载目录，等待重试或人工处理。多文件任务中的其他已验证文件可以正常落盘。

### 6.3 失败分类、持久化与 Web 展示

SQLite 必须同时保存一次解析运行的最终状态和每个策略的尝试记录。日志不是唯一数据源，进程重启后 Web UI 仍应能解释为什么没有创建下载任务或为什么进入兜底。

每次解析运行至少保存：

- `status`：`Succeeded`、`FallbackSucceeded`、`RetryPending`、`ManualActionRequired`、`Skipped`。
- `failure_stage`：`input`、`bangumi`、`tmdb_series`、`tmdb_season`、`tmdb_episode`、`ai`、`validation`、`download_gate`。
- `failure_code`、不含密钥/请求头/完整 Prompt 的 `reason`、`retryable`、`attempt_count`、`started_at`、`finished_at`。
- `failure_kind`：`SemanticNoMatch`、`Network`、`RemoteService`、`Authentication`、`Configuration`、`Protocol`、`InvalidInput`、`Ambiguous`、`Cancelled`；以及 `tmdb_access_confirmed` 和计算后的 `bangumi_fallback_eligible/denial_reason`。
- 已确认的上级标识：例如 Series 已确认但 Season 失败时仍保留 `tmdb_id`，便于重试和人工诊断。
- `mikanid`、命中的人工规则 ID/版本、`EpisodeOffset`，以及 Series/Season/Episode 各自的 `resolution_source`、验证状态和取得时间。

每个策略尝试至少保存 `strategy`、`priority`、`outcome`、错误码、脱敏原因、耗时和时间戳。`outcome` 固定区分 `Disabled`、`NotApplicable`、`NoMatch`、`Error`、`Succeeded`、`Terminated`。

错误码必须区分以下业务含义，不能只显示“TMDB失败”：

- 输入/标题：文件名无法解析标题、Bangumi Subject ID 缺失或无效。
- 访问：DNS/连接错误、超时、429、5xx、401/403、API Key 缺失、无效响应。
- Series：没有找到标题、多个候选无法消歧、详情或 TV Series ID 无效。
- Season：日期不匹配、Backtrace 耗尽或出错、AI 未匹配/不可用、标题没有季度、Season 1 不存在。
- Episode：目标集不存在、标题/日期冲突、重复映射、统一 AI 匹配失败、TMDB 二次验证失败。

网络错误、超时、429 和 5xx 在重试耗尽后标记 `retryable=true`；缺少/错误密钥标记为配置修复；确定性无结果、歧义或字段冲突标记为需人工处理。Web UI 显示最终原因和按优先级排列的尝试时间线，并支持按阶段、错误码、可重试性和状态筛选及手动重新匹配。

Bangumi 完全兜底资格只允许 `failure_kind=SemanticNoMatch && tmdb_access_confirmed=true`。Web 必须显示“允许/拒绝兜底”及拒绝原因；网络恢复后的手动/自动重试重新运行 TMDB，不把旧网络错误转换成 `tmdbid=0`。

当前 SQLite `metadata_resolution_runs` 已把上述最终决定固化为
`tmdb_access_confirmed`、`fallback_eligible` 和 `fallback_denial_reason`。任务查询只读取
该任务最高 `attempt_number` 的 Run，不从最近一条策略 Attempt 猜测资格。列表和详情
API 使用 `latest_run_status`、`tmdb_access_confirmed`、
`bangumi_fallback_eligible`、`bangumi_fallback_denial_reason` 返回同一决定。WebUI 只在
Run 为 `failed` 或 `fallback_resolved` 时显示它：前者明确显示允许/拒绝、权威访问
是否确认及稳定拒绝原因；后者明确显示已经使用固定本地 S01 且不提供有效 tmdbid。
没有 Run、仍在运行或已取得正常 TMDB 身份时，不虚构兜底决定。

### 6.4 最终取得证据

策略尝试时间线用于解释过程，最终字段不能在查询时从“最近一次尝试”重新推断。
SQLite schema v32 在完成事务中保存以下权威证据：

- Series：`series_resolution_source + run_id + series_resolution_attempt_id`；
- Season：`season_resolution_source + run_id + season_resolution_attempt_id`；
- Episode：每个 `task_files` 独立保存
  `episode_resolution_source + episode_resolution_run_id + episode_resolution_attempt_id`。

每个引用的 Attempt 必须属于同一任务和 Run，stage 与层级一致、strategy 与 source
一致，并且结果为 `matched`。SQLite 触发器拒绝不完整引用、跨任务引用、错误 stage
和伪造 strategy。Episode worker 在写每个文件时使用本次匹配直接返回的 Attempt ID；
多个文件即使策略相同，也不能一律引用最后一次 Attempt。能够关联的字幕保存自己的
`subtitle_association` 证据；无法关联并进入 `Other` 的文件不伪造 Episode 证据。

任务摘要只有在全部已取得 Episode 证据的 source/run/attempt 三元组相同时才返回单一
Episode 证据，否则返回 `episode_resolution_mixed=true`，由文件详情逐项展示。作品库
Series/Season 也读取固化证据，不再聚合尝试时间线猜测来源。P2 `title_season` 和 P1
`first_season` 仍明确标注为本地未验证季度；证据表示“如何取得”，不把它包装成 TMDB
Season 验证成功。

## 7. 验证场景

1. TMDB 全成功：SQLite 保存来源 ID，动画根目录 `tvshow.nfo` 包含真实 TMDB/Bgm ID。
2. TMDB 搜索无结果、开关默认关闭：状态进入原失败路径，不创建下载任务，不生成 `tvshow.nfo`。
3. 同一输入开启开关且 Bgm ID 有效：固定进入本地 `S01` 继续处理，NFO 精确包含 `tmdbid=0` 和对应 `bangumiid`；标题中的第二季等信息不得改变该季度。
4. 开关开启但 Bgm ID 缺失：不得继续，不生成 NFO。
5. P2/P1 全部关闭或任务标题没有季度：不影响完全兜底固定使用本地 `S01`。
6. TMDB 网络错误在重试内恢复：不得提前进入 Bangumi 兜底。
7. 重试耗尽并成功兜底，后台恢复后用真实 TMDB ID 原子更新 NFO。
8. 已有 TMDB ID、仅季度失败：不得误判成“TMDB 完全失败”。
9. 断言只创建 `tvshow.nfo`，不为本功能创建 `season.nfo`。
10. `tmdb_fail_skip=true` 与 Backtrace 同时开启：Skip 优先，不发起前传请求。
11. Backtrace 命中第二个前传：使用命中的 TMDB 季度，不执行 TitleSeason/FirstSeason。
12. Backtrace 回溯到首部仍无匹配：按顺序继续 TitleSeason，再继续 FirstSeason。
13. 前传缺日期、多前传和循环关系：遍历结果确定、无死循环、同一 Subject 不重复请求。
14. 前传请求瞬时失败后恢复：继续回溯；重试耗尽：记录 `BacktraceError` 后执行较低优先级策略。
15. 当前作品的日文名、中文名均未取得可验证 Series，但存在 `bgmid` 且 Backtrace 开启：必须逐项查询前传，并允许前作恢复一个不同的有效 `tmdbid + Season`；P3 耗尽且仍无有效 Series 时，TitleSeason/FirstSeason 标记不适用。
16. Backtrace 或 AI 返回的 Series/Season/Episode 均验证成功时，目录名、季度和集号使用 TMDB 值，来源值只保留审计；P2/P1 是明确的本地 Season 回退例外，分别使用任务 `title` 解析季度或固定 `S01`，不验证 TMDB Season，且必须保存取得策略。
17. AI 返回有效 Series/Season 但 Episode 不存在：不得下载/重命名，不允许退回来源 EP 冒充 TMDB EP。
18. `tmdb_fail_use_bangumi=true` 的 `tmdbid=0` 路径明确标记为例外，不得把其名称/季度/集号记录为 TMDB 来源。
19. 非 AI 季度成功且同号 EP 标题/日期一致：直接采用 TMDB EP，AI 请求数为 0。
20. 同号 EP 存在但日期冲突：不得误判成功；统一 AI 开启且此前未尝试时恰好调用一次。
21. AI 返回其他 TMDB ID/Season：拒绝并进入 `EpisodeUnmatched`，已确认季度不得被改写；季度阶段已尝试 AI 时 Episode 阶段调用数必须为 0。
22. 从 `/Home/Bangumi/3951`、带尾斜杠或 query 的同类 URL 均取得 `mikanid=3951`；缺失、非正整数和不属于 Mikan Bangumi 页面的路径不得误解析。
23. 同一 `mikanid` 的不同字幕组、RSS 条目和 Torrent 统一使用同一个 `bgmid`、TMDB Series/Season 和 Episode Offset；标题差异不得新建另一条作品规则。
24. 人工规则存在时自动搜索、Backtrace、AI、TitleSeason 和 FirstSeason 请求数均为 0；规则无效时记录 `ManualOverrideInvalid`，不得静默回退覆盖。
25. 偏移 `+12` 将来源 EP 1 映射为 TMDB EP 13；偏移 `0` 和负偏移按同一公式处理。结果小于 1、不存在、溢出或来源非整数时拒绝映射。
26. Menu、OVA、PV、NCOP、NCED 等文件不应用 EP 偏移；季度已由人工规则确认时按既定 `Other` 规则处理。
27. 修改人工规则只影响新任务和显式重新匹配；已完成媒体不会被静默移动。清除或禁用规则后，新解析恢复自动策略链。
28. Web 作品详情分别显示 Series/Season/Episode 的获取阶段；覆盖 Manual、直接匹配、Backtrace、AI、TitleSeason、FirstSeason、EP 偏移、确定性 EP 匹配、Other 和 Bangumi 完全兜底场景。

## 8. 当前已接通的暂停下载门禁

qBittorrent 确认接收 Torrent 后，任务进入 `download_preparing`，而不是直接开始传输。dispatcher 对新添加和已存在的同 hash 任务都显式调用暂停；Series/Season/Episode worker 在该阶段完成 TMDB 验证、`Other` 分类和逐集 claim。元数据全部完成后，download preparation worker 再次暂停任务，并要求 qB 返回的文件数量、唯一 index、规范化相对路径和容量与暂存时解析的清单逐项一致。

`duplicate` 与 `ignored` 文件设置为 priority 0，`episode` 与 `other` 文件设置为 priority 1；只有至少一个 wanted 文件时才恢复任务并进入 `download_queued`。全部文件都被去重时不调用恢复，持久化 `download_skipped_duplicate`，并以 `deleteFiles=false` 尝试移除 qB 任务。文件元数据尚未就绪、清单不一致、下载器离线或请求失败均保留 paused 语义，通过 SQLite preparation lease、attempt 和 next-attempt 安全重试；进程崩溃后的过期租约可恢复。

默认单元/集成测试只使用 fake client 和临时 SQLite，不接触 portable qBittorrent。真实 `filePrio`、恢复、全重复清理和跨容器路径 E2E 仍必须使用明确的可丢弃 Torrent fixture、可识别 category/tag 和书面清理步骤后显式运行。
