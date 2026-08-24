# AI 辅助 TMDB 匹配

## 1. 产品语义

AI 元数据匹配是一个独立、默认 `false` 的任务级开关。确定性失败策略顺序为 Skip=4、Backtrace=3、TitleSeason=2、FirstSeason=1；AI 不占该编号链。一个下载任务最多执行一次 AI 业务匹配，以同一份请求和同一份 Prompt 同时返回 Series、各文件 Season 与 Episode 候选，输出必须经过 TMDB Series/Season/Episode 验证。不存在独立的“季度 AI 流程”和“EP-AI 流程”。

AI 匹配以一个下载任务为单位，一次处理单文件或多文件。总标题和候选视频文件的名称/字节容量始终足够发起请求，不要求来源是 Mikan；`bgmid`、`anidbid` 和 `imdbid` 是可空的作品级辅助标识。非空 ID 已由调用方与当前下载任务的标题和 Torrent 文件组绑定，但不保证 Bangumi/AniDB/IMDb 与 TMDB 使用相同标题、季度拆分或 Episode 编号，也不表示具体文件已经完成 EP 对应。AI 返回 TMDB TV Series ID，以及每个文件对应的 Season/Episode 候选；正式名称和其他元数据由主程序重新请求 TMDB 获取。

Mikan RSS 的 `pubDate` 可触发发布日期优先查找，但配置开关和运行时门禁分离。即使开关开启，也只有 Torrent 实际文件条目数恰好为 1、存在 `bgmid`、`pubDate` 合法、Bangumi 可用且主程序成功算出最近普通 Episode 时才生效。Torrent 可以是单文件模式，也可以有根目录但目录下只有一个文件；目录节点不计数。日期候选只是辅助证据，不直接决定 TMDB Episode。

## 2. 配置

```yaml
advanced:
  default:
    tmdb_fail_skip: false
    tmdb_fail_backtrace: false
    ai_use_metadata_match: false
    tmdb_fail_use_title_season: false
    tmdb_fail_use_first_season: false

ai:
  provider: openai_compatible
  base_url: ""
  api_key: ""
  model: ""
  timeout_second: 600
  retry_count: 2
  use_bangumi_pubdate_first: true
```

首版直接用 `HttpClient` 调用 OpenAI-compatible API，不依赖厂商 SDK。配置缺失时记录明确错误并继续较低优先级；密钥允许环境变量覆盖，本机配置页和 AI 匹配测试工具直接回填当前值，日志、请求轨迹和错误内容继续脱敏。一次 Chat Completions 响应必须恰好包含一个 `choice`；零个候选属于无效协议响应，多个候选属于协议歧义，不能静默取第一项。外层响应和 `message.content` 中的业务 JSON 分别解析并使用稳定错误码分类。

当前主程序部署层使用扁平键
`ai_provider`、`ai_base_url`、`ai_api_key`、`ai_model`、
`ai_use_metadata_match`、`ai_timeout_second`、
`ai_retry_count`、`ai_use_bangumi_pubdate_first`、
`ai_tmdb_mcp_url`、`ai_bangumi_mcp_url` 和
`ai_anidb_mapping_url_template`；环境变量由 ASP.NET Core 配置系统按同名键覆盖。
硬性默认超时为 600 秒。AI API key 只保存在服务端配置中，配置 API/WebUI
仅返回 `api_key_configured`。

`ai_anidb_mapping_url_template` 只保留部署兼容读取，值必须逐字等于程序内置模板；任意其他值会以安全配置错误拒绝启动。实际请求始终使用编译期固定模板、禁止代理与重定向，并在连接前解析 DNS、只连接公网地址，模型不能提供 URL 或替换 `anidbid`。

旧部署键 `ai_use_season_match` 和 `ai_use_episode_match` 仅用于升级读取：未设置规范键时，任一旧键为 `true` 都会启用统一流程；显式 `ai_use_metadata_match` 的值优先。配置 API 仍回显两个旧字段且值与规范字段相同，供旧客户端平滑迁移；WebUI 和新写入只使用 `ai_use_metadata_match`。

## 主程序内置 AI 测试

WebUI 的“AI 匹配测试工具 / AI 元数据测试”以已验证独立 Tester 为行为基准，内置相同请求/响应 DTO、Prompt 条件区块、OpenAI Responses/Chat Completions 请求格式、工具注册/缓存、最多 8 轮工具调用、Responses stateful→stateless 续轮、用量累计、结果结构校验、Mikan pubDate 门禁和本地 EP offset 计算。页面样式接入主程序侧栏，但协议和交互不再由主程序简化。“发送给 AI API 的请求”和“工具调用顺序与完整 Content”保留完整审计内容；每轮请求/工具调用是第一层折叠项，请求 Content、工具请求 Content 与返回 Content 是可分别展开的第二层折叠项，长 JSON 默认不铺满页面。

测试页分为公共区域、Mikan/BGM、U2/AniDB、高级 Prompt 和结果审计区。三个来源区域的状态徽标会随表单即时更新：TMDB MCP、Mikan/BGM MCP 与 AniDB 已启用时显示绿色，关闭时显示中性灰色；状态颜色不改变实际工具注册条件。后台 Worker 与测试页默认读取同一份有效生产 Prompt；该 Prompt 使用 `{{#TMDB_MCP}}…{{/TMDB_MCP}}`、`BGM_MCP`、`ANIDB_LOOKUP`、`IMDB_LOOKUP`、`BANGUMI_PUBDATE_FIRST` 条件区块。被关闭或不适用的字段与说明不会发送给模型，对应工具也不会注册。Tester 原结构校验结论保持不变，随后额外显示主程序 `AiMetadataResultValidator` 的 TMDB Series/普通 Season/真实 Episode 二次验证，二者不能混为一个状态。

模型连接字段与原 Tester 一致：Base URL、API Key、Model、Responses/Chat Completions、reasoning effort、Responses Web Search、600 秒默认超时、单次测试 HTTP 代理、TMDB/BGM MCP 地址和 AniDB URL 模板。测试工具默认使用 Responses 并开启 Web Search；这两个测试默认值不改写后台 Worker 的正式 AI 模式与开关，其余连接字段继续从主配置回填。API Key 只存在于当前请求和内存，不进入浏览器持久化、Prompt、执行日志或响应。Mikan URL 导入继续使用主程序全局“域名匹配代理”，不使用该测试代理。

主程序“设置与备份 / AI 与 MCP”提供正式模型的推理程度：`none`、`low`、`medium`、
`high`。`none` 表示不向 OpenAI-compatible 请求写入 `reasoning`；其余值以
`reasoning: { effort }` 发送，保存到私有配置并在编辑器中回填，重启后用于后台 AI 匹配。
部署 YAML/命令行也可使用 `metadata.ai.reasoning_effort` / `ai_reasoning_effort`，部署键
存在时 WebUI 只读。该设置不改变 Prompt。

该端点是只读诊断边界：不创建统一导入、下载或元数据任务，不访问 qBittorrent，也不写 SQLite 动画库。`run-stream` 逐行返回 `progress/result/stopped/error`；结果包含原始 Provider 响应、提取后的模型 JSON、结构校验、`request_identity`、累计 Token、每轮脱敏 AI 请求、工具顺序与脱敏 Request/Response Content、本地 offset 及主程序 TMDB 二次验证。密钥、Authorization、Cookie、passkey 和宿主机路径不得出现在审计内容。

`GET /api/v1/ai-test/prompt` 返回当前进程实际使用的 `tmdb-ai-match-v16` 生产模板、程序内置默认模板、是否自定义及长度上限。WebUI“设置与备份 / AI 与 MCP”可持久化正式 Prompt 私有覆盖，保存前验证全部必需占位符和条件区块，预览只显示版本、字符数和短 SHA-256，不回显完整差异；保存后重启生效。测试页的高级 Prompt 仍可临时修改并按版本保存浏览器草稿，但只影响当次测试；“恢复默认”恢复当前进程的有效生产模板，不写配置。

`POST /api/v1/ai-test/torrent-import` 接收 Torrent base64，后端解析实际文件数并签发 4 小时有效、最多 256 条的 `import_id`；运行请求不能直接声明可信 `torrent_file_count`。`POST /api/v1/ai-test/mikan-import` 使用现有 Mikan DNS/重定向/Host/Cookie/Torrent staging 安全链，并签发同类 `import_id`。响应不返回 Torrent URL、Cookie 或 passkey；WebUI 只填表，不自动运行 AI。

## 3. 最小请求契约

```json
{
  "title": "下载任务总标题",
  "files": [
    {
      "name": "Season 1/01.mkv",
      "size_bytes": 1234567890
    }
  ],
  "bgmid": null,
  "anidbid": null,
  "imdbid": null,
  "torrent_file_count": 1,
  "published_at": null,
  "bgm_episode_candidate": null,
  "use_bangumi_pubdate_first": false
}
```

- `title` 来自 RSS、torrent 或手工下载任务的总标题，不要求来自 Mikan。
- `files` 只包含主程序筛选后的候选视频文件；单文件也使用数组。
- `name` 使用下载任务内部的相对文件名或 basename，不发送宿主机绝对路径；允许保留任务内部目录以区分重名和季度。
- `size_bytes` 是非负整数，只作为辅助线索。
- `bgmid`、`anidbid` 只接受正整数或 `null`；`imdbid` 只接受规范 IMDb Title ID 字符串或 `null`；三者都为空时仍须正常匹配。
- `torrent_file_count` 是解析 `.torrent` 后得到的实际文件条目数，必须大于0；单文件模式和“根目录下只有一个文件”都记为1。
- `published_at` 只在 Mikan 来源中出现，接受带偏移的 ISO 8601 时间或 `null`。Mikan 原始 `pubDate` 没有偏移时按 Mikan SourceProfile 的时区解析，默认 `Asia/Shanghai`，再规范为带偏移值；原始字符串另存审计，不原样交给模型。该值是辅助参数，不是 Bangumi/TMDB 单集日期校验条件。
- `bgm_episode_candidate` 是主程序可选提供的普通 Bangumi Episode 提示；特别篇和附加条目不参与，查询失败时为 `null`。它不能作为程序或模型接受/拒绝 TMDB Episode 的硬门禁。
- 配置项 `ai.use_bangumi_pubdate_first` 是用户开关；请求中的 `use_bangumi_pubdate_first` 是程序计算的最终门禁：仅 `开关开启 && is_mikan && torrent_file_count == 1 && bgmid != null && published_at != null && bgm_episode_candidate != null && BGM查询成功` 时为 `true`。API调用方不能伪造最终门禁。
- 非空 ID 表示调用方已经确认其与当前任务标题及文件组存在作品级关联；不得因为跨站标题字面不同而丢弃该上下文。
- 该关联不证明具体 TMDB Series/Season/Episode，也不证明来源 Episode 与 TMDB Episode 同号。Bangumi/AniDB 的标题、别名、日期、集数和映射结果全部只是候选证据，最终结果仍须由 TMDB 数据验证。
- 禁止内嵌发送 Bangumi详情、密钥、Cookie、Access Key、完整种子内容、日志和无关配置；Bangumi详情只通过本地 MCP 按需获取。

## 4. 本地工具与调用顺序

测试程序和主程序均不能把 `http://*.mcp.local` 作为远程 MCP URL 交给云端模型。程序在本地实现 Streamable HTTP MCP 客户端，将发现到的 MCP tools 转换为带命名空间的 function tools；模型发起 function call 后由本机执行，再把结果回传模型。工具清单和 endpoint schema 可按 MCP 地址/版本缓存，避免每次请求重复发现。

- TMDB MCP：`http://tmdb.mcp.local/mcp`，始终启用，用于搜索和验证候选；最终结果还要由模型外的主程序通过 TMDB API 二次验证。
- Bangumi MCP：`http://bgm.mcp.local/mcp`，仅 `bgmid != null` 时连接并注册工具。
- AniDB映射：仅 `anidbid != null` 时注册本地查询工具；固定读取 `api/anidb/{anidbid}.json` 的 `tmdbtv` 字段，作为候选 TMDB TV ID。
- IMDb：不注册任意 URL 工具；仅 `imdbid != null` 时注册参数为空的 `lookup_imdb_tmdb_tv`。主程序把已规范化的固定 IMDb ID 交给 TMDB MCP external ID/find，程序侧删除 Movie 结果，仅把正整数 TV Series ID 候选返回模型；通用 `tmdb__invoke-api-endpoint` 若调用 `/3/find`，也必须使用同一任务绑定 ID 和 `external_source=imdb_id`，否则在发往 MCP 前拒绝。
- Web Search：只有适用 MCP 无结果、报错或信息不足后才可调用；不得作为第一数据源。

两个 MCP 当前均实现 Streamable HTTP MCP `2025-03-26`，暴露 `list-api-endpoints`、`get-api-endpoint-schema`、`invoke-api-endpoint`。转换为模型函数时分别增加 `bgm__`、`tmdb__` 前缀，避免同名冲突。实现必须限制工具轮数、超时、参数/响应大小，并支持取消、同步 JSON 响应，以及 `tools/call` 返回 202 后用同一 session GET SSE 取得对应 JSON-RPC request id 的响应。MCP DNS、连接、一般网络、HTTP鉴权/限流/服务端/拒绝、SSE、协议和工具 `isError` 分别记录稳定错误码；模型未调用必需 TMDB MCP 使用 `ai_tmdb_mcp_not_used`，上述故障不得降格成普通未匹配。

AniDB映射 URL 固定为：

```text
https://raw.githubusercontent.com/DeQxJ00/Anime-Lists-Json/refs/heads/main/api/anidb/{anidbid}.json
```

映射数据约65%经过人工检验，未检验部分也不等于错误，但全量只能作为参考。`tmdbtv` 为空、无效、404或请求失败不能直接导致任务失败；非空候选必须由 TMDB MCP结合任务标题、完整文件组及作品结构验证后才能写入最终响应。不能把 AniDB/Bangumi Episode 编号直接当作 TMDB Episode Number。

### 4.1 Mikan 单文件发布日期优先分支

该分支由主程序预计算 Bangumi 日期候选、固定 Prompt 执行 TMDB 定向验证。主程序不预计算 TMDB 候选：

1. 开关开启且基础条件满足后，主程序通过 Bangumi 客户端读取 `bgmid` 对应 Subject 的 Episode 列表。
2. 只考虑有合法播出日期且集号为正整数的普通正片，排除小数集、特别篇、OP/ED、PV 和其他附加条目；未来 Episode 不参与。以 `published_at` 的来源本地日历日期寻找最近的已播条目并按集号和 Episode ID 稳定排序，不设置 Torrent 发布延迟窗口。结果仅作为 `bgm_episode_candidate` 提示。
3. 任一步失败或候选为空时，最终门禁为 false，但 Mikan 的 `published_at` 参数仍可进入统一 AI 请求；继续通用 AI 匹配，不因 Torrent 日期失败。
4. 门禁为 true 时，模型使用原始 `files[].name` 和 `bgm_episode_candidate` 定向查询 TMDB TV Series、普通 Season 和 Episode；任何来源集号都不得直接复制成 TMDB Episode Number。
5. TMDB 定向验证失败时继续原通用 AI 匹配流程，不把它当成整个任务失败；最终仍必须通过 TMDB MCP和主程序二次验证。

生产流程中 `use_bangumi_pubdate_first=false` 时不发送 `bgm_episode_candidate` 的日期优先指令，但 Mikan 的 `published_at` 仍作为非约束参数发送；非 Mikan 来源始终为 `null`。内置 Tester 使用独立字段名 `mikan_pub_date`：Tester 开关关闭或运行时门禁未通过时，该字段以及对应日期优先条件字段从 Prompt JSON 整段省略，不使用 `null` 占位；WebUI 关闭开关时，发往 Tester API 的请求也不包含 `mikan_pub_date` 属性。即使 `bgmid` 非空，Bangumi MCP仍可按原通用流程提供作品标题、别名等上下文。人工规则和 Episode Offset 始终优先，命中时不调用 AI。

主程序可按 `bgmid` 和数据版本缓存 Bangumi 普通 Episode 列表，但缓存必须遵守更新策略，不能导致新播 Episode 永久不可见。模型侧不再为日期候选重复调用 Bangumi 工具。

当前实现按 Bangumi 官方 `GET /v0/episodes` 合约请求 `type=0`，每页 200 条并设置 10,000 条硬上限；分页字段不一致、超限、无效 JSON、网络或服务错误都只关闭可选日期门禁并记录 `ai_pubdate` 安全错误码，不阻断随后通用 AI。任务 claim 从 SourceProfile 读取真实 adapter，从全部 `task_files` 计数，已被标记 `ignored/duplicate/other` 的条目也计入 Torrent 实际文件数。启用的同 `mikanid` 人工规则中的 `bgmid` 高于任务来源值；完整 Series/Season 人工覆盖和 EP offset 均优先于 AI。

## 5. 最小响应契约

```json
{
  "matched": true,
  "tmdb_id": 12345,
  "files": [
    {
      "name": "Season 1/01.mkv",
      "matched": true,
      "season": 1,
      "episode": 1,
      "reason": null
    }
  ],
  "reason": null
}
```

输入文件必须在输出中按原顺序恰好出现一次，`name` 原样返回。无法确认 Episode 的文件使用 `matched=false`、`episode=null` 并给出具体原因；普通季度可靠时保留大于0的 `season`，季度也不确定时才使用 `season=null`。

模型偶尔会在已经 `matched=true` 的顶层或文件级 `reason` 中附带解释文字。主程序允许并忽略这类冗余说明，原始值仍保留在 AI Debug 审计中；这不会替代或放宽 `tmdb_id`、Season、Episode、文件身份/数量以及后续 TMDB Series/Season/Episode 二次验证。`matched=false` 时仍必须提供非空的具体原因。

顶层 `matched` 表示整个任务是否已有明确落盘方案，不表示每个文件都是 TMDB Episode。Series 已确认，并且每个文件要么有经过验证的 Season/Episode，要么有大于0的已确认 Season 可进入 `Other` 时，顶层可以为 `true`。存在 Series 未确认、`season=null`、重复映射或目标冲突时才为 `false`。

不要求模型返回动画名称、首播日期、Episode标题、置信度或复杂错误枚举。这些内容要么由主程序从 TMDB 获取，要么由主程序根据 HTTP/验证结果分类。

唯一正式 Prompt 见 [`TMDB_AI_MATCH_PROMPT.md`](TMDB_AI_MATCH_PROMPT.md)。本次契约版本为 `tmdb-ai-match-v16`；实现不得维护第二份运行时 Prompt。部署配置或私有配置可以覆盖模板正文，但必须保留契约要求的全部占位符和条件区块；变更内置模板必须先取得项目所有者明确确认，再更新 `prompt_version` 并通过 snapshot review。

## 6. 未匹配文件与 Other

- 不匹配 TMDB Season 0 或 Specials；AI 和主程序都拒绝 `season=0`。
- Menu、特别篇、OVA、Summary、PV、CM、NCOP、NCED、Logo 或其他非正片文件返回 `matched=false`、`episode=null` 和具体原因。
- 如果已经可靠确认该文件随任务所属的普通季度，可以保留大于0的 `season`；主程序将原文件名放入 `<TmdbSeriesName>/Sxx/Other/`。
- 如果 Season 也无法确认，则文件留在下载目录等待重试或人工处理，不能放进猜测的季度。
- `Other` 文件不使用 `Eyyy` 重命名，不伪装为已完成 TMDB Episode 匹配。

## 7. 主程序二次验证

模型结果只是候选。AnimeGoNet 必须：

1. 验证 `tmdb_id` 是真实 TMDB TV Series ID。
2. 用 `language=zh-CN` 获取正式名称，缺失时使用 TMDB `original_name`。
3. 验证每个 Season 存在且大于0；Season 0 一律拒绝。
4. 验证每个 Episode 在对应 Season 中存在。
5. 检查输入/输出文件数量、顺序和名称完全一致。
6. 拒绝重复主视频目标、缺失映射、伪造 ID 或字段越界。

已确认 Episode 的视频正常进入 `Sxx/Eyyy.ext`；Series/Season 已确认但 Episode 未匹配的文件保留原名进入 `Sxx/Other/`。Series 或 Season 未确认、重复目标、目标冲突的文件不移动。网络错误、认证错误、AI 未匹配和 TMDB 验证失败由主程序写入元数据失败审计。

跨季度 Torrent 包仍只发起一次任务级 AI 请求。验证成功后，任务级 `metadata_resolution_runs.tmdb_season_number` 保持 `NULL`，每个 `task_files.tmdb_season_number` 保存自己的普通季度；Episode worker 按逐文件季度请求和校验 TMDB，不能用任务摘要中的最小季度覆盖其他文件。能够唯一关联到视频的字幕继承该视频的季度和 Episode/Other 种子；存在无法归属季度的字幕或其他待处理文件时，整个跨季度候选以 `ai_cross_season_file_unassigned` 安全拒绝，事务不得产生部分季度写入。跨季度任务不应用作品级单季度人工 EP offset，也不会产生第二次 AI 调用。

所有请求/响应 DTO 使用 `System.Text.Json` source generation，确保 NativeAOT 可分析。

## 8. Mikan 文件名 EP 与可信偏移

`file_episode_candidate` 完全属于 Mikan RSS 本地状态。程序用上游 `Auto_Bangumi/raw_parser.py` 的 C# 移植规则从每个 Torrent 视频 basename 重新计算候选，调用方不能覆盖；AI Prompt、请求 DTO 和响应 DTO 都不包含该字段或 `episode_offset`。

AI 返回逐文件 `season/episode` 后，主程序先完成 TMDB Series/Season/Episode 二次验证，再对每个“已匹配正片且存在文件名候选”的文件计算 `episode_offset = TmdbEpisodeNumber - file_episode_candidate`。同一任务的偏移和普通季度均统一时才产生缓存学习证据；没有候选、偏移不一致或跨多个 TMDB 季度时不学习，但不因此否定已经验证成功的逐文件映射。

可选的 `(mikanid,groupid)` 可信偏移缓存默认关闭，并且只由主程序管理，不属于 AI 测试程序。来源 EP 专指从 Torrent 视频文件名解析出的 `file_episode_candidate`；不同文件名 EP 得到完全相同且已由 TMDB 验证的 `tmdb_id+season+episode_offset`，达到可配置门槛后才可信（1～100，默认 3）。重复 EP 不计数，冲突重置或撤销可信。主程序在 AI 调用前命中有效可信记录时，直接用 `file_episode_candidate + episode_offset` 本地构造目标 Episode 映射并把本次 AI 请求数降为零，不再为该次命中逐集请求 TMDB。完整状态机、WebUI 和验收规则见 [`MIKAN_EPISODE_OFFSET_CACHE.md`](MIKAN_EPISODE_OFFSET_CACHE.md)。

## 9. 单次任务级调用与阶段复用

统一 AI 流程可以在两个位置首次触发，但它们共用同一开关、输入契约、Prompt、解析器和验证器：

- 正常 Series/Season 联合匹配及 P4/P3 都未取得结果时，调用一次 AI，同时验证 Series、Season 和全部视频 Episode；成功结果一次性写入逐文件种子，Episode worker 只消费结果。
- 已由确定性流程确认 Series/Season、但普通 Episode 匹配仍有缺口时，如果该任务从未尝试过 AI，则调用同一解析器一次，并在模型外锁定已确认的 Series/Season。

任务审计使用统一策略名 `ai_metadata`。历史数据库中的 `ai_season`、`ai_episode` 仍被识别为“该任务已经尝试过 AI”，因此升级后不会重复调用。一次 AI 尝试失败后，即使 P2/P1 又取得本地季度，也不得在 Episode 阶段再次调用；HTTP `retry_count` 只允许同一语义请求内部重试，不是第二条业务流程。

已确认 Series/Season 的调用不把它们加入模型业务输入；响应返回后在模型外检查 `tmdb_id` 与季度均未越界，并逐 Episode 请求 TMDB 验证。

## 10. 验证门禁

- 请求 snapshot 严格只有 `title`、`files[].name/size_bytes`、可空 `bgmid`/`anidbid`/`imdbid`、`torrent_file_count`、可空 `published_at/bgm_episode_candidate` 和程序计算的 `use_bangumi_pubdate_first`。
- AI 响应中的 `files[]` 按输入数组顺序解释，数量必须一致。单文件任务只有唯一对应关系，`name` 回显即使有误也不影响结果；多文件任务必须逐项原样回显 `name`，用来防止模型乱序导致 EP 串文件。回显值本身不参与整理或改名，主程序始终保留相同下标的原始 Torrent 文件名。
- 开关关闭时门禁必须为假；开关开启也只有 Mikan Torrent实际文件条目数恰好为1、`bgmid/pubDate` 有效、BGM查询成功且普通Episode候选非空时才为真。
- 门禁为真时 Prompt 直接以文件名EP和主程序计算的Bangumi日期EP定向查TMDB；失败后继续通用流程。任何Bangumi集号都不能直接成为最终TMDB集号。
- 断言MCP endpoint/schema跨请求缓存；记录优先分支的Bangumi/TMDB工具次数及转入通用流程的原因。
- `bgmid=null` 时不得连接或注册 Bangumi MCP；`anidbid=null` 时不得注册 AniDB映射工具；TMDB MCP始终可用。
- `bgmid`/`anidbid` 非空时确认其作品级任务绑定被保留；跨站标题不一致不能单独判定失败，来源 Episode 同号也不能单独判定成功。
- `imdbid` 非空时必须先经 TMDB external ID/find 查询并验证 TV 类型；不能采用 Movie ID，也不能直接推导 Season/Episode。
- AniDB `tmdbtv` 候选未经 TMDB MCP验证不得成为最终 `tmdb_id`。
- 断言适用 MCP 先于 Web Search；MCP充分时 Web Search调用数为0。
- 单文件、多文件和跨季度包均最多使用一次任务级请求，特别篇不映射 Season 0。
- 断言季度阶段 AI 失败后即使 P2/P1 成功，Episode 阶段也不会发出第二次 AI 请求。
- 正片全部映射、其余视频均有普通季度可进入 `Other` 时，顶层 `matched=true`；只有存在无法确定季度的文件或冲突时才为 `false`。
- 无 Bangumi/Mikan 信息时仍可请求和匹配。
- 输入/输出数量、顺序或名称不一致时拒绝整个响应。
- fake AI 覆盖超时、429、5xx、取消、非 JSON、超长响应和部分文件失败。
- fake TMDB 覆盖真实/伪造 TV ID、普通季度、Season 0 拒绝、Episode 缺失、季度 `Other` 和重复目标。
- NativeAOT 发布二进制以正式后台 worker 完成 fake AI 两轮 → MCP 工具 → fake TMDB Series/Season/Episode 二次验证 → SQLite/API 权威状态落库；fixture 不连接真实 AI、TMDB、qBittorrent 或用户 TestSpace。

AI 调用审计另外保存调用前的 `ai_trigger_reason`，与调用后的 result/error/reason 分开。季度统一为
`season_unresolved:<最后一次确定性失败码>`；Episode 汇总本批未决视频的原因并使用
`episode_unresolved:<原因>`。Mikan 文件在兼容候选被拒绝后即使未保留候选值，也会重新执行同一
纯函数解析器取得精确拒绝码，例如 `ambiguous_episode_markers`。该字段从 schema v55 开始生成，
不对历史调用作猜测性回填。

## 11. AI Debug 完整链路

`metadata.ai.debug_mode` / `ai_debug_mode` 默认 `false`。开启后，正式后台 AI 调用按 `run_id` 在 `data_path/ai-debug` 写入一份独立 JSON 调试文件；它不进入 SQLite，也不改变匹配、重试或验证结果。每份记录固定包含：

1. 进入 AI 前的任务输入快照，包括标题、视频文件名/容量、本地来源 EP/文件名 EP 候选/已预解析结果、mikanid/groupid/bgmid/anidbid/imdbid、来源标识和 Torrent 实际文件数。
2. 同一 metadata run 在 AI 调用前已经落库的确定性尝试，按时间顺序展示 stage、strategy、P4/P3 等 priority、结果、安全错误码、耗时和重试性；同时保存已经尝试过的 TMDB 搜索词、模型外锁定的 tmdbid/Season、Mikan 发布时间辅助证据及进入 Series/Season 或 Episode AI 的触发阶段。
3. 当次有效 Prompt 模板原文和代入实际输入后的最终渲染 Prompt。全局模板日后修改不会覆盖历史快照。
4. 每轮 OpenAI-compatible HTTP 请求/响应，以及 TMDB/Bangumi MCP initialize、工具列表、工具调用和响应；记录 endpoint、Body、HTTP 状态、耗时与安全错误类型。
5. 模型原始输出、解析后的候选、累计 Token/请求/工具用量，以及主程序对 TMDB Series/普通 Season/真实 Episode 的最终本地验证。

该文件属于本机敏感调试数据。捕获器从不保存 HTTP Authorization Header、API Key、Cookie、passkey、Torrent URL、announce、种子字节或下载器凭据；正式 AI 输入边界本身也没有这些字段。WebUI“日志 / AI 调用日志”仅在对应文件存在时显示“查看完整链路”，按“AI 前置链路 → Prompt → AI/MCP 请求链 → 模型结果与本地验证”分段和折叠展示，并允许只删除该 run 的 Debug 文件。普通 AI 调用日志和配置归档不包含这些大正文；关闭 Debug 不删除既有文件。
