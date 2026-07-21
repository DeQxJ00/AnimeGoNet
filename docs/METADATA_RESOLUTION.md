# 元数据 ID 与 TMDB/Bangumi 兜底

## 1. ID 定义

内部模型禁止使用含糊的 `AnimeEntity.ID`：

- `Id`：AnimeGoNet 自增/内部主键。
- `SourceId`：输入源 profile ID，例如 `mikan`、`u2`、`ttg`；不是站点条目 ID。
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

规则无效时记录 `ManualOverrideInvalid` 并停止当前项，不能由自动策略静默覆盖。只有用户修正规则、禁用规则或明确清除人工覆盖后才恢复自动解析。规则修改影响新任务和用户显式重新匹配的任务，不静默移动已整理完成的媒体文件。

### 1.2 TMDB 获取阶段

作品详情不能只显示最终 `tmdbid`，必须分别保存和显示三个解析来源：

- Series：`ManualMikanRule`、`AutomaticSearch`、`AI`、`None`。
- Season：`ManualMikanRule`、`DirectDateMatch`、`Backtrace`、`AI`、`TitleSeason`、`FirstSeason`、`None`。
- Episode：`ManualOffset`、`DirectNumber`、`BangumiDeterministicMatch`、`AI`、`Other`、`None`。

每个来源同时关联解析运行 ID、策略尝试 ID和取得时间。Bangumi 完全兜底时 TMDB 三层来源均为 `None`，另显示 `BangumiFallback` 状态，不能伪装成某种 TMDB 获取方式。Web UI 的作品详情页固定展示“TMDB 获取方式”卡片，包含 Series/Season/Episode 来源、人工规则 `mikanid`、偏移值、验证状态和最后解析时间。

### 1.3 已下载记录与重复集

重复集/多版本采用“第一个成功记录生效”，不实现自动多版本管理。规范去重键固定为 `(TmdbSeriesId, TmdbSeasonNumber, TmdbEpisodeNumber)`，作用域是整个媒体库，不区分 Mikan、U2、TTG、字幕组、Torrent 或下载器实例。同一 TMDB 剧集不整体阻断：只跳过已经完成的具体 Episode，其他 Episode 正常处理。

只有下载、文件策略、重命名和必要的 NFO/目录数据库写入全部成功后，才为每个 Episode 原子写入 `Downloaded=true` 的完成记录。失败、取消、只完成下载但整理失败的任务不能占用“第一个”资格。

为支持 RSS 早停，每次成功解析还保存 `(source_id, source_work_key, 来源Episode类型, 来源Episode)` 到规范 TMDB Episode 的 alias。再次遇到已知 alias 时可在 RSS 解析阶段直接停止该文件；alias 未命中时先完成 TMDB 映射，再在提交下载器前检查全局规范键。`mikanid`、bgmid/anidbid/imdbid、Torrent hash 和来源集号保留用于审计，但都不能替代最终全局去重键。

多文件 Torrent 按文件级处理：已下载 Episode 对应的视频及其已绑定字幕标记为 unwanted，未下载 Episode 和对应字幕继续。qBittorrent 使用“暂停添加 → 等待/校验 metadata → 设置文件 priority → 恢复”；必须按相对路径和容量核对索引，不能仅按数组位置。所有正片 Episode 已完成且没有其他明确需要处理的文件时，才跳过整个 Torrent。

当前实现已在 TMDB Episode 官方验证完成的同一 SQLite 事务中获取 `EpisodeClaim`：同一任务内映射到同一 Episode 的视频和字幕共享一个 claim；已有规范完成记录时标记 `episode_already_completed`，其他任务持有活动 claim 时标记 `episode_claimed_by_another_task`，并且只把对应 `task_files` 置为 `duplicate`，同任务其他 Episode 不受影响。整理全部成功写入 `CompletionRecord` 时 claim 原子转为 `completed`，整理失败可按 claim 所属 `task_file_id` 显式释放。claim 不按超时自动接管，崩溃恢复仍须核对下载器与文件状态。

这一增量关闭了“验证完成到整理完成”之间的 SQLite 并发窗口，但当前元数据 worker 仍位于下载完成之后。把相同门禁前移到 qBittorrent 恢复下载之前、按路径和容量设置 unwanted/priority，仍属于下载编排模块，不能把当前 `duplicate` 标记误报成已节省网络下载。

TMDB 完全失败、`tmdbid=0` 的 Bangumi 兜底没有规范 TMDB Episode 键，不能参与 TMDB 级全局去重，但不等于完全不记录下载历史。每个完整成功的兜底文件都必须原子写入 `FallbackCompletionRecord`，并按当前可证明的最强身份建立唯一键：

1. 已可靠解析到 Bangumi Episode ID 时，使用 `(bgmid, bangumi_episode_id)`；这可以在持有同一可靠 Bangumi Episode 身份的来源之间去重，但不能把来源集号直接当 Bangumi Episode ID。
2. 否则 Mikan 使用 `(source=mikan, mikanid, 来源Episode类型, 规范化来源Episode)`，使相同 `mikanid` 下的不同字幕组、RSS 条目和 Torrent 不会重复下载同一来源集。
3. 其他来源使用 `(source_id, source_work_key, 来源Episode类型, 规范化来源Episode)`；若 Episode 身份也无法可靠解析，只能按 source item ID、Torrent info-hash 和文件指纹阻止同一输入重复处理。

fallback 唯一键必须包含身份类型，不能把不同编号体系拼进同一命名空间。命中记录时在创建下载器任务前停止对应文件；多文件 Torrent 仍逐文件处理。上述第二、三档不能保证跨来源、跨编号体系识别同一真实 Episode，因此 Web 必须显示当前去重范围和“可能跨来源重复”的风险，不能宣称全局去重。为了避免误伤，系统也不能仅凭相同标题、容量或来源集号跨来源阻断。

为关闭并发窗口，在提交下载器前必须用 SQLite 唯一约束和事务为每个 fallback 键创建 `FallbackEpisodeClaim`。同键只有一个活动 claim；随后到达的任务等待首项结果或以 `DuplicateInProgress` 早停，不能同时进入不同下载器。下载/整理失败时 claim 进入可重试失败态并按重试策略释放或接管；完整成功时在同一事务中转为 `FallbackCompletionRecord`。进程崩溃后的过期 claim 只能在核对下载器任务和文件状态后恢复，不能仅按超时直接再次下载。

后续补全真实 TMDB 映射时，在一个事务中把 `FallbackCompletionRecord` 合并为规范 `(TmdbSeriesId, TmdbSeasonNumber, TmdbEpisodeNumber)` 完成记录并保存原 fallback alias。若多个 fallback 记录或已有规范记录收敛到同一 TMDB Episode，保留最早的规范完成记录，其他记录标记 `DuplicateAfterResolution` 并进入人工处理；不得再次触发下载，也不得静默删除已经存在的文件。

旧 YAML 的 `allow_duplicate_download` 字段仍可读取和迁移，但新程序不允许它绕过规范 TMDB Episode 完成记录；Web 标记为已弃用并解释需要先删除对应完成记录才能重新下载。这样不会因为旧配置中的 `true` 破坏跨来源全局去重。

Web UI 的“删除业务记录”必须细分出“删除已下载完成记录”。删除一个规范 TMDB Episode 完成记录时同时删除/失效它的所有来源 alias；不会隐式删除下载器任务、下载源文件或媒体库文件，但会解除所有来源的去重门禁。删除操作保存操作者、原键、alias、原状态、时间和关联文件快照，便于解释为何发生重新下载。

### 1.4 字幕随集整理

附属文件首版只处理字幕；字体、图片、校验文件、歌词、章节和其他附件不跟随移动。字幕扩展名至少识别 `.ass`、`.ssa`、`.srt`、`.vtt`、`.sub`、`.idx` 和 `.sup`，比较时不区分大小写；`.idx/.sub` 作为一组处理。

字幕按以下证据顺序绑定 Episode：

1. 与视频相同的相对目录，字幕 stem 等于视频 stem。
2. 去掉字幕 stem 尾部一个或多个语言/轨道标记后等于视频 stem，例如 `.zh-CN`、`.zh-Hans`、`.chs`、`.cht`、`.sc`、`.tc`、`.jpn`、`.eng`、`.default`、`.forced`、`.sdh`、`.cc` 及其组合。
3. 文件名可解析出来源 Episode，并且在当前任务内只对应一个已验证的视频 Episode。

只有得到唯一候选才绑定，存在多个候选时不得按文件顺序或容量猜测。字幕绑定成功后直接继承对应视频已验证的 TMDB Series/Season/Episode，不独立调用 AI，也不再次应用 Episode Offset。视频目标为 `E013.mkv` 时，`原视频名.zh-Hans.forced.ass` 重命名为 `E013.zh-Hans.forced.ass`；无法识别的原后缀也应原样保留在 `E013` 与字幕扩展名之间，避免多语言轨道互相覆盖。

未能唯一绑定的字幕在 Series 和普通 Season 已确认时，保留原文件名放入 `<TmdbSeriesName>/Sxx/Other/` 并记录 `SubtitleUnmatched` 或 `SubtitleAmbiguous`；季度未知时留在下载目录。其他非字幕附件首版始终留在下载目录，不放入 `Other`。

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
- 人工覆盖或既有季度 fallback 最终得到有效 Season Number。

任一前置条件不满足，仍按失败、待确认或跳过处理，不生成失败 NFO。

“非网络原因”在此处使用白名单语义，不是简单判断 `retryable=false`。允许进入 `tmdbid=0` 的最终原因仅包括成功 TMDB 查询后的 `SeriesNotFound`、`NoAcceptableTvCandidate` 或等价的确定性无匹配。以下情况即使重试耗尽或表面上属于非网络错误，也一律禁止兜底：

- DNS、连接、代理、TLS、超时、取消或断路器开启。
- HTTP 408/429/5xx 及其他服务暂时不可用。
- API Key 缺失、401/403、endpoint/代理配置错误。
- 响应截断、格式/Schema错误、无法验证响应来源或解析失败。
- 输入缺失/非法、人工覆盖无效、候选仍有歧义等需要配置或人工修复的情况。

先前尝试发生过瞬时网络错误、但后续重试成功取得有效 TMDB 响应并最终确定无匹配时，可以按最终 `SemanticNoMatch` 判断。反之，仅 AI/MCP 返回“未找到”而权威 TMDB 请求从未成功，不能视为非网络无匹配。

## 3. 状态与输出语义

| TMDB 结果 | Bgm ID | 季度 | 开关 | 结果 |
|---|---:|---:|---:|---|
| 成功 | 有效 | 有效 | 任意 | 使用 TMDB 名称/Season/Episode 继续；`tvshow.nfo` 写真实 `tmdbid` + `bangumiid` |
| 确定性完全无匹配（`SemanticNoMatch`） | 有效 | 有效 | `true` | 使用 Bangumi 兜底继续；`tvshow.nfo` 写 `tmdbid=0` + `bangumiid` |
| 网络/服务/认证/配置/协议失败 | 有效 | 有效 | `true` | 禁止兜底；进入重试或配置/人工修复，不下载、不生成 NFO |
| 完全失败 | 有效 | 有效 | `false` | 原失败流程；不下载、不刮削、不生成 NFO |
| 完全失败 | 缺失 | 任意 | `true` | 无法兜底；失败/待确认，不生成 NFO |
| 完全失败 | 有效 | 未确定 | `true` | 无法安排媒体目录；待确认或跳过，不生成 NFO |
| TMDB ID 有效、仅季度失败 | 有效 | fallback 后有效 | 任意 | 不属于完全失败；正常继续并写真实 `tmdbid` + `bangumiid` |

本节的 `tmdbid`、`bangumiid` 都指 NFO XML 标签内容。内部 SQLite 分别保存来源值、TMDB 规范值、失败原因和待修复状态，不用 NFO 的 `0` 代替内部身份信息。

## 4. NFO 文件和内容

ID 写入动画剧集根目录的 `tvshow.nfo`，与 AnimeGo `develop` 保持一致；不写入季度目录的 `season.nfo`。

TMDB 成功：

```xml
<tvshow>
  <tmdbid>72517</tmdbid>
  <bangumiid>371546</bangumiid>
</tvshow>
```

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
    # TMDBFailUseAIMatchSeason；独立阶段，不占确定性优先级
    tmdb_fail_use_ai_match_season: false
    # TMDBFailUseTitleSeason，优先级2
    tmdb_fail_use_title_season: false
    # TMDBFailUseFirstSeason，优先级1
    tmdb_fail_use_first_season: false
    # TMDBFailEpUseAIMatchSeason；不属于季度优先级链
    # 非AI季度匹配成功，但EP无法对应时使用AI匹配一次EP
    tmdb_failep_use_ai_match_season: false
```

季度策略按数值从高到低执行：

1. `tmdb_fail_skip=true`：优先级 4，立即跳过当前项，不执行其他策略。
2. `tmdb_fail_backtrace=true`：优先级 3，按 [AnimeGo issue #15](https://github.com/wetor/AnimeGo/issues/15) 沿 Bangumi“前传”关系逐项回溯；成功即采用，耗尽则继续下一策略。
3. `tmdb_fail_use_ai_match_season=true`：独立可选阶段，使用下载任务总标题、候选视频的相对文件名/字节容量及可空作品级 `bgmid`/`anidbid`/`imdbid` 请求大模型，一次返回整个任务的 TMDB Series/Season/Episode 候选。非空 ID 已绑定当前任务，但跨站标题、季度和 EP 编号可能不同，只能作参考；结果必须通过官方 TMDB API 二次验证，详细协议见 [`AI_METADATA_MATCHING.md`](AI_METADATA_MATCHING.md)。
4. `tmdb_fail_use_title_season=true`：优先级 2，从标题明确季度中取值。
5. `tmdb_fail_use_first_season=true`：优先级 1，使用第一季。
6. 所有启用策略都没有结果：进入待确认/解析失败。

除 `Skip` 外，某个已启用策略返回“未匹配”或在自身重试耗尽后失败时，必须记录本次尝试并继续较低优先级；成功后立即停止。`Skip` 是显式终止策略，命中后不得执行较低优先级。未启用、因前置条件不满足而不适用、执行后未匹配、执行出错和成功必须是可区分结果，不能都记成“失败”。

### 6.1 回溯算法边界

- 只在已经取得有效 `TmdbSeriesId`、但当前 Bgm 首播日期与所有有效 TMDB 季度的最小差值仍超过 `ThemoviedbMatchSeasonDays` 时运行。
- 复用同一 TMDB 剧集的季度列表；从当前 `BangumiSubjectId` 查询“前传”关系，并以每个前传的首播日期重新执行相同的日期差匹配。
- 命中阈值内季度立即结束回溯；没有前传即视为回溯到首部，转入较低优先级策略。
- 前传缺少首播日期时不参与日期匹配，但仍可继续查询它的前传。
- 多个前传按关系距离由近到远遍历；同层按首播日期降序、Subject ID 升序稳定排序，保证相同输入结果确定。
- 使用 visited Subject ID 防止关系环和重复请求；所有请求支持取消。关系读取失败先按数据源策略重试，耗尽后记录独立 `BacktraceError` 并继续较低优先级策略，不能伪装成正常的“回溯耗尽”。

TMDB 已有有效 ID但季度失败时执行完整策略。TMDB 完全失败时 Backtrace 不适用，但 AI 可以尝试恢复 ID/Season/Episode。AI 仍失败且 `tmdb_fail_use_bangumi=true` 时进入先前确认的 `tmdbid=0` 例外路径；由于没有 TMDB 规范值，该路径只能使用 Bangumi 名称与来源季度/集号，并必须在状态/UI 中明确标记为非 TMDB 规范命名。

### 6.2 非 AI 季度成功后的 EP 校验

`tmdb_failep_use_ai_match_season` 是后置 EP 开关，不参与上述优先级排序，默认 `false`。仅当季度结果来源不是 AI 时执行：

1. 在已确认的 TMDB Season 中读取完整 Episode 列表。
2. 若与 `SourceEpisodeNumber` 同号的 TMDB Episode 存在，且 Bgm/文件名标题、首播日期没有冲突，则直接采用该 TMDB Episode。
3. 同号不存在或存在冲突时，使用内部取得的 Bgm Episode 标题/日期在同一 TMDB Season 内做确定性匹配；这些详情不发送给 AI。
4. 仍有文件无法对应且开关为 `true` 时，使用任务总标题和完整候选视频列表发起一次任务级 AI 请求。
5. AI 必须返回与已确认值相同的 `tmdb_id` 和 `season_number`，且目标 Episode 必须存在；AI 试图更换 Series/Season 时拒绝。
6. 开关关闭、AI 失败或 TMDB 二次验证失败时进入 `EpisodeUnmatched`，不得退回来源 EP；普通季度已确认时按 `Other` 规则整理，季度未知时不移动。

“一次”指一个下载任务的一次语义匹配，不对无效答案继续改写 Prompt 追问。没有收到响应时可按网络策略幂等重试同一请求；重试不得改变任务标题、文件列表、Prompt 或已确认的 Series/Season。

Mikan RSS 可在季度 AI 和后置 EP-AI Prompt 中增加单文件发布日期优先分支。配置开关开启后，主程序仍只在 Torrent实际文件条目数恰好为1、`bgmid` 和合法 `pubDate` 同时存在且没有命中更高优先级人工规则时尝试计算；Torrent单文件模式和根目录下仅一个文件均满足。`pubDate` 无时区时按 Mikan SourceProfile 默认 `Asia/Shanghai` 解析。主程序从该Subject的普通Episode中找到播出日期最接近者并写入 `bgm_episode_candidate`；查询失败或候选为空时最终门禁保持 false。门禁为 true 时，Prompt 直接把该候选与文件名解析EP用于定向查询TMDB。两个来源集号都不能复制为TMDB Episode Number，最终结果仍须TMDB验证；优先分支失败时继续原通用AI流程。

AI 和确定性匹配均不接受 Season 0。Series 和大于0的普通季度已经确认、但 Episode 无法确认时，不用来源集号冒充 TMDB Episode；该文件保留原名进入 `<TmdbSeriesName>/Sxx/Other/`，并保存未匹配原因。季度也无法确认时保留在下载目录，等待重试或人工处理。多文件任务中的其他已验证文件可以正常落盘。

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
- Episode：目标集不存在、标题/日期冲突、重复映射、EP-AI 失败、TMDB 二次验证失败。

网络错误、超时、429 和 5xx 在重试耗尽后标记 `retryable=true`；缺少/错误密钥标记为配置修复；确定性无结果、歧义或字段冲突标记为需人工处理。Web UI 显示最终原因和按优先级排列的尝试时间线，并支持按阶段、错误码、可重试性和状态筛选及手动重新匹配。

Bangumi 完全兜底资格只允许 `failure_kind=SemanticNoMatch && tmdb_access_confirmed=true`。Web 必须显示“允许/拒绝兜底”及拒绝原因；网络恢复后的手动/自动重试重新运行 TMDB，不把旧网络错误转换成 `tmdbid=0`。

## 7. 验证场景

1. TMDB 全成功：SQLite 保存来源 ID，动画根目录 `tvshow.nfo` 包含真实 TMDB/Bgm ID。
2. TMDB 搜索无结果、开关默认关闭：状态进入原失败路径，不创建下载任务，不生成 `tvshow.nfo`。
3. 同一输入开启开关、Bgm ID 和季度有效：继续下载/刮削，NFO 精确包含 `tmdbid=0` 和对应 `bangumiid`。
4. 开关开启但 Bgm ID 缺失：不得继续，不生成 NFO。
5. 开关开启但季度无法确定：进入待确认或跳过，不生成 NFO。
6. TMDB 网络错误在重试内恢复：不得提前进入 Bangumi 兜底。
7. 重试耗尽并成功兜底，后台恢复后用真实 TMDB ID 原子更新 NFO。
8. 已有 TMDB ID、仅季度失败：不得误判成“TMDB 完全失败”。
9. 断言只创建 `tvshow.nfo`，不为本功能创建 `season.nfo`。
10. `tmdb_fail_skip=true` 与 Backtrace 同时开启：Skip 优先，不发起前传请求。
11. Backtrace 命中第二个前传：使用命中的 TMDB 季度，不执行 TitleSeason/FirstSeason。
12. Backtrace 回溯到首部仍无匹配：按顺序继续 TitleSeason，再继续 FirstSeason。
13. 前传缺日期、多前传和循环关系：遍历结果确定、无死循环、同一 Subject 不重复请求。
14. 前传请求瞬时失败后恢复：继续回溯；重试耗尽：记录 `BacktraceError` 后执行较低优先级策略。
15. TMDB 完全失败且仅 Backtrace 开启：不发起无意义的前传请求，最终仍解析失败；若 TitleSeason/FirstSeason 同时开启则可由它们确定季度。
16. 任一季度策略返回 Series/Season/Episode 且均验证成功：目录名、季度和集号全部使用 TMDB 值，来源值只保留审计。
17. AI 返回有效 Series/Season 但 Episode 不存在：不得下载/重命名，不允许退回来源 EP 冒充 TMDB EP。
18. `tmdb_fail_use_bangumi=true` 的 `tmdbid=0` 路径明确标记为例外，不得把其名称/季度/集号记录为 TMDB 来源。
19. 非 AI 季度成功且同号 EP 标题/日期一致：直接采用 TMDB EP，AI 请求数为 0。
20. 同号 EP 存在但日期冲突：不得误判成功；开启后置开关时 AI 恰好调用一次。
21. EP-AI 返回其他 TMDB ID/Season：拒绝并进入 `EpisodeUnmatched`，已确认季度不得被改写。
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
