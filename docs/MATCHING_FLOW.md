# AnimeGoNet 匹配与整理流程图

下图按当前实现说明从 Mikan/U2 输入开始，经过作品、季度、Episode、AI、
全局去重、下载和文件整理，最终写入动画库完成记录的主要流程。

```mermaid
flowchart TD
    START((输入内容))

    subgraph SOURCE["① 输入源与任务建立"]
        MIKAN["Mikan<br/>RSS / AnimeGoHelper"]
        U2["U2 油猴插件<br/>人工确认 TV 或 Movie"]
        PROFILE["读取输入源配置快照<br/>下载器 · 文件策略 · 做种时间<br/>AniDB 映射选项 · 重复通知"]
        TORRENT["安全获取并解析 Torrent<br/>以后端读取的真实文件名、路径、容量为准"]
        ROUTE{"传入的媒体类型"}
    end

    START --> MIKAN
    START --> U2
    MIKAN --> PROFILE
    U2 --> PROFILE
    PROFILE --> TORRENT --> ROUTE

    subgraph TVWORK["② TV 作品与季度匹配"]
        MANUAL_RULE{"存在人工指定<br/>或 Mikan 作品规则？"}
        MANUAL_OK["验证指定 TMDB Series / Season"]
        SOURCE_KIND{"来源"}

        MIKAN_TITLES["读取 Bangumi Subject<br/>official / 中文名 / 别名"]
        MIKAN_TMDB["搜索 TMDB TV<br/>标题相似度 + 首播日期 ±1 日<br/>逐候选验证 Series 与 Season"]
        MIKAN_FALLBACK["季度失败链<br/>Skip P4 → Bangumi Backtrace P3<br/>→ 统一 AI → 标题季度 P2 → 第一季 P1"]
        BGM_FALLBACK["可选 Bangumi 完全兜底<br/>仅限 TMDB 确认无结果"]

        U2_ANIDB{"U2 是否有 AniDB ID？"}
        U2_PREF{"优先使用 AniDB 映射 TMDB？"}
        U2_MAP["Anime-Lists 映射<br/>直接取得并验证 tmdbtv"]
        U2_CACHE["AniDB 标题缓存<br/>优先 official title<br/>再搜索 TMDB TV"]
        U2_TITLE["AnitomySharp 解析任务总标题<br/>搜索 TMDB TV"]
        U2_SEASON{"TMDB 普通季度数量"}
        U2_ONE["只有一个非 S00 季度<br/>直接验证并选中"]
        U2_MULTI["多个普通季度<br/>读取映射中的 tmdbseason"]
        U2_MULTI_OK{"tmdbseason 存在且有效？"}
        TV_AI["进入统一任务级 AI"]
    end

    ROUTE -->|TV| MANUAL_RULE
    MANUAL_RULE -->|有| MANUAL_OK
    MANUAL_RULE -->|无| SOURCE_KIND
    SOURCE_KIND -->|Mikan| MIKAN_TITLES --> MIKAN_TMDB
    MIKAN_TMDB -->|Series + Season 成功| TVEP
    MIKAN_TMDB -->|未确定| MIKAN_FALLBACK
    MIKAN_FALLBACK -->|匹配成功| TVEP
    MIKAN_FALLBACK -->|TMDB 语义无结果且允许| BGM_FALLBACK
    BGM_FALLBACK --> DOWNLOAD_GATE

    SOURCE_KIND -->|U2| U2_ANIDB
    U2_ANIDB -->|有| U2_PREF
    U2_ANIDB -->|无| U2_TITLE
    U2_PREF -->|开启| U2_MAP
    U2_PREF -->|关闭| U2_CACHE
    U2_MAP --> U2_SEASON
    U2_CACHE --> U2_SEASON
    U2_TITLE --> U2_SEASON
    U2_SEASON -->|1 个| U2_ONE --> TVEP
    U2_SEASON -->|多个| U2_MULTI --> U2_MULTI_OK
    U2_MULTI_OK -->|有效| TVEP
    U2_MULTI_OK -->|缺少或无效| TV_AI

    MANUAL_OK -->|成功| TVEP
    MANUAL_OK -->|失败| STOP

    subgraph TVFILES["③ TV 文件与 Episode 匹配"]
        TVEP{"来源"}
        MIKAN_EP["Mikan 文件规则<br/>人工 Offset → 可信 Offset<br/>→ 文件名 EP → Bangumi/TMDB 日期证据"]
        MIKAN_VERIFY["逐文件验证 TMDB Episode"]
        MIKAN_GAP{"仍有普通视频未匹配？"}

        U2_PARSE["U2 专属规则解析整个 Torrent<br/>忽略 .pad 的匹配意义<br/>但不从下载清单排除"]
        U2_EXTRAS["识别 NCOP / NCED / SP / 特典<br/>无数字冲突时标记 Extras"]
        U2_GATE{"正片 EP 集合是否与 TMDB Season 完全相同？<br/>数量相同 · 编号集合相同 · 一一对应<br/>无缺集/超集/重复/数字冲突"}
        U2_DIRECT["整季确定性通过<br/>不调用 AI"]

        AI_ONCE["统一 AI 匹配，仅一次<br/>Prompt v24 + file_id 契约<br/>Series / Season / 全部视频一起返回"]
        AI_VERIFY["程序重新调用 TMDB 严格验证<br/>Series → Season → Episode<br/>拒绝 S00、身份不一致和重复目标"]
        AI_RESULT{"AI 结果有效？"}
        AI_EXTRAS["明确 Extras 或 U2 已完成 TV 正片之外的文件<br/>进入 Extras"]
    end

    TVEP -->|Mikan| MIKAN_EP --> MIKAN_VERIFY --> MIKAN_GAP
    MIKAN_GAP -->|没有| SUBTITLE
    MIKAN_GAP -->|有且本任务未调用过 AI| AI_ONCE

    TVEP -->|U2| U2_PARSE --> U2_EXTRAS --> U2_GATE
    U2_GATE -->|完全一致| U2_DIRECT --> SUBTITLE
    U2_GATE -->|单集/缺集/超集/重复/未解析/冲突| AI_ONCE
    TV_AI --> AI_ONCE

    AI_ONCE --> AI_VERIFY --> AI_RESULT
    AI_RESULT -->|有效| AI_EXTRAS --> SUBTITLE
    AI_RESULT -->|无效且可重试| RETRY
    AI_RESULT -->|无效且不可重试| OTHER

    subgraph MOVIEWORK["④ Movie 匹配"]
        MOVIE_LAYOUT["分析视频文件容量与布局"]
        MOVIE_MAIN{"能否唯一确定主文件？"}
        MOVIE_PRIMARY["最大且明显更大的视频<br/>作为 Movie 主文件"]
        MOVIE_EXTRAS["其他较小视频<br/>作为 Movie Extras"]
        MOVIE_ID["解析 TMDB Movie<br/>AniDB 映射 / AniDB official 标题缓存<br/>/ AnitomySharp 标题搜索"]
        MOVIE_VERIFY["验证 TMDB Movie 身份"]
    end

    ROUTE -->|Movie| MOVIE_LAYOUT --> MOVIE_MAIN
    MOVIE_MAIN -->|可以| MOVIE_PRIMARY --> MOVIE_EXTRAS --> MOVIE_ID
    MOVIE_MAIN -->|多个文件无法区分| OTHER
    MOVIE_ID --> MOVIE_VERIFY
    MOVIE_VERIFY -->|成功| SUBTITLE
    MOVIE_VERIFY -->|失败| OTHER

    subgraph FINISH["⑤ 关联、去重、下载与整理"]
        SUBTITLE["字幕关联<br/>同目录同 stem → 语言后缀 → 唯一 EP 候选"]
        ATTACHMENT["字幕、字体、压缩包等附件<br/>能关联则跟随 Episode<br/>否则进入 Extras"]
        DUPLICATE["全局 Episode 去重与 Claim<br/>键：TMDB Series + Season + Episode"]
        DOWNLOAD_GATE{"是否已有完成记录<br/>或被其他任务占用？"}
        DUPLICATE_SKIP["对应文件标记重复并跳过"]
        PREPARE["生成下载清单并恢复 qB"]
        MIKAN_DL["Mikan：可只下载需要的文件<br/>重复 Episode 可设为 unwanted"]
        U2_DL["U2：完整下载 Torrent<br/>正片、Extras、.pad 均不筛除"]
        ORGANIZE["按文件策略整理<br/>move / link / link_delete / wait_move"]
        TV_PATH["TV：作品 / Sxx / Eyyy.ext<br/>Extras：作品 / Sxx / Extras / 原名"]
        MOVIE_PATH["Movie：电影规范主文件<br/>附属文件进入该 Movie 的 Extras"]
        COMPLETE["文件、NFO、数据库全部成功后<br/>原子写入完成记录"]
        LIBRARY((动画库))
    end

    SUBTITLE --> ATTACHMENT --> DUPLICATE --> DOWNLOAD_GATE
    DOWNLOAD_GATE -->|已完成或被占用| DUPLICATE_SKIP
    DOWNLOAD_GATE -->|允许| PREPARE
    PREPARE -->|Mikan| MIKAN_DL --> ORGANIZE
    PREPARE -->|U2| U2_DL --> ORGANIZE
    ORGANIZE -->|TV| TV_PATH --> COMPLETE
    ORGANIZE -->|Movie| MOVIE_PATH --> COMPLETE
    COMPLETE --> LIBRARY

    subgraph MANUAL_ACTIONS["⑥ 人工处理入口"]
        OTHER["Other / 待处理"]
        RETRY["等待重试"]
        ASSIGN["手动指定<br/>TV/Movie · TMDB ID · Season<br/>逐文件 EP / Extras / Movie 主文件"]
        POST["TV+Movie 后处理<br/>从已匹配 TV 合集中拆出 Movie<br/>指定 Movie 正片与 Movie Extras"]
        IGNORE["忽略处理<br/>保留文件与现有 Extras"]
    end

    OTHER --> ASSIGN --> SUBTITLE
    OTHER --> POST --> MOVIE_VERIFY
    OTHER --> IGNORE
    RETRY --> MANUAL_RULE
    STOP["人工规则无效或关键验证失败<br/>停止，不静默降级"]
```

## 关键规则

- U2 的 `TV` / `Movie` 完全采用油猴插件传入值，后端不根据标题改写类型。
- U2 TV 只有整个 Torrent 的普通正片 EP 与 TMDB 普通季度完全一致时才跳过 AI；
  一旦进入 AI，本地解析结果不再作为最终答案。
- U2 完整下载种子内容，包括 Extras 和 `.pad`；`.pad` 只在匹配时忽略。
- 人工/可信 EP Offset 只适用于 Mikan；Mikan 也可以按文件跳过已经完成的 Episode。
- AI 只提供候选，程序仍会重新验证 TMDB Series、Season 和 Episode，并拒绝
  Season 0、身份不一致及重复目标。
- Extras 不占普通 Episode，也不计入 Other 提醒；无法确认的普通视频才进入 Other。
- 只有下载、链接或移动、NFO 与数据库写入全部成功，才创建动画库完成记录。

