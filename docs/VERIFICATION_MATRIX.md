# 功能验证矩阵

范围说明：U2/TTG 已由项目所有者确认为首版暂缓。矩阵中已有的 U2/TTG 用例只锁定
通用 adapter、跨来源去重和路由骨架不回退，不代表首版站点支持或默认配置验收。

当前基础设施快照（2026-08-12）：win-x64 NativeAOT 与 Ubuntu 24.04 x86_64 上的
linux-x64 NativeAOT/Docker/双 qB/完整链路/发布 WebUI 已实跑；固定上游 Go 1.22.10
Linux amd64 基线已通过。linux-arm64 Docker、win-arm64/linux-arm64/osx-arm64 原生
runner 和外部 Release 仍未验证。详见
`docs/verification/2026-08-11-ubuntu-ct-docker-validation.md` 与
`docs/verification/2026-08-11-upstream-go-linux-baseline.md`。

## 验证层级

- **U**：纯单元测试，无网络/磁盘或使用临时内存实现。
- **C**：组件/契约测试，使用 fixture server、临时目录或 fake RPC。
- **P**：Go `develop@c7475df` 与 .NET 对同一 fixture 的 parity/golden test。
- **I**：真实基础设施集成测试，如 SQLite、qBittorrent、文件系统。
- **E**：从原生 AOT 发布二进制启动的端到端测试。
- **L**：受控在线 smoke，不作为普通 CI 稳定性门禁。

## 模块矩阵

| 模块 | 必测行为 | 层级 | 完成门禁 |
|---|---|---|---|
| CLI/生命周期 | 参数、env 优先级、Ctrl+C、SIGTERM、5 秒退出保护 | U/C/E | JIT/AOT 行为一致，退出无数据损坏 |
| 配置 | 默认生成、注释、12 版升级、备份、非法值、相对路径、旧 Mikan RSS name/URL/Cron/enable | U/P/I/E | 固定上游 `configs` 文件/符号与测试入口无漏项；所有历史 fixture 通过；RSS seed 真正落入 SQLite，passkey URL 不进入响应/异常 |
| JSON/YAML | 字段名、零值、map/list、Unicode、round-trip | U/P/E | 无反射 fallback；AOT 通过 |
| 领域模型/错误 | 上游 models/constants/exceptions 全文件与导出类型、稳定值、包装错误语义、AOT 替代边界 | U/P/E | 固定 HEAD 清单无漏项；每项有真实 C# 目标或批准例外；结构解析统一 `ParseFailed` 且稳定码安全 |
| 纯函数 | UTF-8 SHA-256、动态 Tag、名称清理/相似度、路径清洗/边界、日期解析/差值/Unix 秒 | U/P/I/E | 上游可观察输入保持 parity；路径/无效输入使用已记录的 fail-closed 安全替代；NativeAOT 无反射 helper |
| SQLite schema/迁移 | 并发首次启动、版本/名称历史、DDL失败回滚、修复续跑、future schema、完整性 | C/I/E | 8 独立连接只应用一次；历史必须是编译期迁移的精确前缀；DDL与版本记录同事务；失败不留半张表；`integrity_check=ok` |
| 缓存 | bucket、TTL、batch、delete、重开、并发 | U/I/E | 时间边界与崩溃恢复通过 |
| 目录 DB | 扫描 anime/season/episode、索引、重复、损坏文件 | U/P/I | 已有媒体目录结果一致 |
| HTTP | UA、proxy、redirect、cookie、query、retry、timeout | U/C | 请求录制与预期一致 |
| RSS | file/url/raw、Mikan XML、缺字段、跳过/错误 | U/P | 全部上游 RSS fixture 通过 |
| Torrent | magnet、单/多文件、info hash、非法 bencode | U/P | 全部 torrent fixture 通过 |
| Mikan | URL/mikanid/字幕组/Bangumi 映射、作品作用域、缓存、错误页 | U/P/L | fixture 全过；同一 mikanid 稳定归并，live 只读 smoke |
| Bangumi | search/get/archive/cache/filter/relation/前传/error | U/P/L | fixture 全过，archive 并发安全，关系循环可终止 |
| TMDB | 搜索、相似度、季度日期、完全失败、无结果、前传回溯、Bangumi兜底资格、失败审计 | U/P/L | 只有成功访问后的SemanticNoMatch可写tmdbid=0；网络/服务/配置等禁止；重启后原因可查 |
| AI 元数据匹配 | 单开关/单Prompt/每任务最多一次调用、任务标题+文件名+容量+可空Bgm/AniDB/IMDb、本地MCP、候选验证、Web Search后备、Season 0拒绝、Other、缓存 | U/C/E | fake server 全过；AniDB/IMDb候选经TMDB验证，已确认Series/Season不能被AI改写，阶段间不得二次调用 |
| C# 内置插件 | source/feed/parser/filter/rename/schedule 的配置、顺序、结果 | U/P/E | 显式注册；上游五类保持 parity，新增 source adapter 通过路由契约 |
| C# 外部插件 | manifest、协议版本、五 RID、配置、超时、取消、崩溃 | C/P/I/E | JIT/AOT 示例均通过，故障不拖垮主进程 |
| Parser | 标题/季度/集数/字幕组/Mikan人工规则/EP偏移/Skip=4/Backtrace=3/Title=2/First=1/独立AI | U/P | 人工规则最高优先级；上游 fixture + 新策略通过；任务级来源证据、逐文件来源候选与经验证 TMDB 规范字段分表/分 DTO/分 UI 区域，不混用 |
| Filter | 顺序、多插件、skip、异常、MikanTool五档/优先级/黑白名单/legacy顺序 | U/P | 上游 Python差分与filter fixture全过 |
| Mikan RSS 同集优选 | mikanid+来源EP分组、动态优先级组/具名数组、逐组短路、黑白名单、双开关、winner→任务→逐文件候选跨请求审计 | U/C/P/I/E | 单候选旁路优先级；重复组选出一个winner；默认720p拒绝；loser无昂贵副作用；任务详情保留历史 batch/revision/decision/groups 且不泄露 URL、URL 派生 candidate ID 或指纹 |
| AnimeGoHelper | 单集、全集、过滤配置上传/获取/往返、认证、CORS | C/P/E | 原油猴脚本无需修改即可完成四个主流程，配置无损往返且真实参与RSS过滤 |
| qBittorrent | connect/retry/add/list/state/delete/category/static tag/metadata dynamic tag/seed | C/I/E | fake + `addTags` HTTP 合同与后置准备流程通过；真实容器主合同进入 CI |
| 不支持的下载器类型 | 旧Transmission配置读取、诊断、禁用、零路由 | C/E | 明确永久Unsupported，不崩溃、不误转qB、不提供创建入口 |
| 多下载器路由 | 命名实例、SourceProfile、ID schema、规则/路径/做种/去重、路由快照 | U/C/I/E | Mikan→bt、U2/TTG→pt；改配置不改变进行中任务，实例状态隔离 |
| 下载状态机 | init/wait/download/seed/complete/pause/error/restart、0/-1/正数做种目标 | U/C/I | schema v33 目标/累计秒数/完成时间持久化；状态和累计值不回退；整理只按持久化门禁推进 |
| 去重 | RSS alias早停、全局TMDB Episode键、包内逐文件跳过、事务复查、来源级通知开关、删除记录后重下 | U/I/E | 跨来源只认第一个完整成功Episode；其他Episode不受影响，无并发双写；schema v38 通知默认开启并固化到路由快照，关闭不绕过去重，RSS/TMDB 命中事件经脱敏 WebSocket 可见 |
| 重命名 | TMDB 名称/Season/Episode、来源字段保留、单/多文件、Other、非法字符、冲突 | U/P/I | 验证成功统一用 TMDB 路径；已知季度的未匹配 Episode 进入 `Other`，未知季度或冲突文件不落盘 |
| 字幕 | 同stem、多语言/轨道后缀、按EP唯一绑定、idx/sub、歧义、Other | U/P/I/E | 匹配字幕随视频继承TMDB EP且后缀不丢；未匹配字幕不猜测，其他附件不移动 |
| 文件策略 | link/link_delete/move/wait_move、跨盘、失败回滚 | U/I/E | 测试根外零写入/删除 |
| 刮削 | `tvshow.nfo` 新建/更新、正常 TMDB 默认不写 Bgm、显式开关恢复 TMDB+Bgm、兜底 `tmdbid=0`+Bgm、恢复重写作业投影、Unicode | U/P/I | 三种 ID 输出模式均有 XML 断言；兜底关闭或前置条件失败时断言没有失败 NFO；恢复作业四态、尝试/重试/完成时间在任务详情可见且不泄露 save root |
| 调度 | 六字段 Cron、NextTime、StartRun、取消、异常隔离 | U/C/E | 虚拟时间稳定，退出不挂起 |
| Web API | 10 路由、method、body/query、auth、响应 envelope | C/P/E | OpenAPI 与响应契约通过 |
| WebSocket | 认证、日志流、pause/resume、断线、慢消费者 | C/E | 无泄漏/死锁，AOT 实机通过 |
| 静态资源 | 首次释放、内嵌页、缓存头、404 | C/E | AOT 单文件环境可访问 |
| Web UI | 仪表盘、下载、配置、插件、缓存、日志、路由、响应式 | U/C/I/E | Node + linkedom DOM/状态/可访问性契约与 Kestrel 资源测试通过；win-x64 NativeAOT Playwright 2/2、Ubuntu CT linux-x64 完整链发布镜像 Chromium 1/1 通过且无 console/page error |
| Web UI 下载进度 | qB状态/进度/速度/ETA/文件priority、做种目标/累计/门禁、业务整理阶段/单位进度、多实例同步、stale恢复 | U/C/I/E | qB100%不提前完成；schema v37 各整理阶段可持久恢复且不会重复文件工作；0/-1/正数目标可解释；wanted进度正确；离线保留快照；暂停恢复/重试通过 |
| Web UI 作品库 | TMDB 名称/Cover/Season、EP完成网格、四种稳定排序、待补全TMDB | U/C/I/E | EP全集与状态只来自TMDB和规范完成记录；排序/分页稳定；`tmdbid=0` 不伪造进度 |
| Web UI 作品详情 | mikanid人工规则、TMDB三层获取阶段、偏移、验证状态、解析时间线 | U/C/E | 页面值与SQLite解析运行一致；人工规则修改有影响预览 |
| 删除编排 | 业务记录、下载器任务、下载源文件、媒体库文件、组合删除、部分失败 | U/C/I/E | 四种可独立执行且不隐式级联；越界零删除，失败可重试且有审计 |
| Web UI 安全 | access-key 会话、敏感字段脱敏、危险操作确认、XSS | U/C/E | 浏览器 E2E 和安全用例通过 |
| Docker | 固定三路径、单一媒体卷、外部客户端路径转换、端口 7991、非 root、UID/GID/TZ、healthcheck、SIGTERM、只读根 | I/E | Ubuntu CT linux-x64 Compose、双 qB、完整下载整理、外部插件和 WebUI 已通过；linux-arm64 与 `link/link_delete` 跨容器同 inode 仍待对应门禁 |
| 旧数据迁移 | YAML、媒体 JSON、可选旧 Go JSON 导出、重复导入 | C/I/E | 不解析 Bolt；已导出的关键数据语义一致 |
| 数据仓库生成 | 上游版本、清洗、分片、schema、计数、引用、确定性 | U/C | 相同输入哈希一致，坏数据不发布 |
| 数据自动更新 | 私有覆盖开关/Cron/URL/自动下载/导入/保留数、环境锁、热重排、manifest、校验、staging、切换、回滚 | U/C/I/E | 各配置组合即时生效，Cron 失败恢复旧任务，任一数据失败继续使用上一可用版本 |
| NativeAOT | analyzers、publish、startup、核心 smoke、size | E | Tier-1 零未批准警告 |

## Web API 契约清单

必须逐条验证成功、参数错误和认证错误：

```text
GET    /ping
GET    /sha256
POST   /api/rss
POST   /api/plugin/config
GET    /api/plugin/config
GET    /api/config
PUT    /api/config
GET    /api/bolt
GET    /api/bolt/value
DELETE /api/bolt/value
POST   /api/download/manager
GET    /websocket/log
```

注意：早期计划写作“10 个 HTTP API + 1 个 WebSocket”，但权威上游 OpenAPI 实际列出 11 个 REST operation + 1 个 WebSocket operation（合计 12 个 operation，分布在 9 个 path template）。移植验收以 OpenAPI method+path 为准，不按旧文案少算 `/sha256` 或同一路径上的不同 method。

## AnimeGoHelper 兼容场景

1. `POST /api/rss`：使用 Mikan RSS fixture 添加全集，断言 MikanTool 过滤规则生效。
2. `POST /api/rss`：`is_select_ep=true` 时仅处理 `ep_links` 指定条目。
3. `POST /api/download/manager`：直接提交 torrent URL 和 Mikan URL，断言按上游行为跳过过滤器并进入解析/下载。
4. `POST /api/plugin/config`：以 `filter/mikan_tool.py` 和 Base64 JSON 上传 `Filiter0`～`Filiter4`。
5. `GET /api/plugin/config?name=filter/mikan_tool.py`：返回可被原脚本解码的同构配置。
6. 正确及错误 SHA-256 `Access-Key`、Mikan 来源跨域、重复提交和错误 RSS 分别验证。
7. `Filiter1/2/3` 同时存在时只应用 `1`；移除后依次验证 `2`、`3`，并验证全局 `0`、字幕组名称 `4` 与适用结果按上游合并。
8. 白名单/黑名单四种开关组合、大小写、空词、Unicode、多个 `Filiter0` 顺序均与上游 Python 输出逐项一致。
9. 油猴上传配置后 Web 立即读取；Web 持有旧 revision 保存时被拒绝。Web 导出的 legacy JSON 再由原油猴上传，规则和关键词无损。
10. `/api/rss` 全集和指定集应用过滤；`/api/download/manager` 明确跳过过滤；新 ingest 按 SourceProfile 快照选择，过滤项不会创建 TMDB/fallback claim 或下载任务。
11. `mikan_rss_filter_enabled` 默认开启；Web 关闭后新 `/api/rss` 请求记录 `SkippedByConfiguration` 并继续后续流水线，旧 GET/POST 配置和规则内容不变；重新开启后原规则立即对新请求恢复。
12. 切换总开关时已创建任务仍使用原 SourceProfile revision/开关快照；并发切换不会使同一任务部分应用过滤。

## Mikan RSS 同集候选优选场景

Mikan RSS winner 的作品身份补全还必须覆盖：

1. 从 Episode URL 同源构造 `/Home/Bangumi/{mikanid}`，页面中 `p.bangumi-info` 的 `https://bgm.tv/subject/{bgmid}` 写入批次与 `ingest_tasks.bangumi_subject_id`。
2. 同批次再次处理复用已解析 `bgmid`，不重复抓取作品页；不同 `mikanid` 即使候选 URL 相同也不得共用批次身份。
3. 链接位于目标段落外、伪造子域名、非 Subject、非正整数、多个冲突 Subject、非法 UTF-8、超限响应和网络失败均返回稳定失败码。
4. 发现失败时 winner 不进入 Torrent staging；相同批次再次显式处理可重新发现并在成功后继续统一导入。

1. 使用四组预设构造同mikanid/EP的简体H.264、繁体H.265两项，断言第一组后只剩简体并立即短路，封装/编码/分辨率组均未执行。
2. 构造第一组无法淘汰、第二组外挂命中后剩一项的候选，断言只执行前两组；全部组仍并列时按原RSS顺序稳定选择。
3. 新增、删除、清空和拖动优先级组与组内具名数组，断言运行顺序与Web预览一致；`name`不参与匹配，只有`values[]`在lowercase后匹配。
4. 覆盖预设的全部值、未知值和大小写；`简繁日`同时存在时采用组内靠前的简体数组。
5. 默认720p黑名单在单候选和多候选时都拒绝；禁用后，资格过滤只剩一项的分组记录`SingleCandidateBypass`且不执行优先级组。
6. 黑名单与白名单同时命中时黑名单优先；存在有效白名单时未命中项拒绝；空白名单不限制。
7. 同批次只有相同mikanid+来源EP才分组；不同mikanid或EP不合并，解析缺失/歧义项旁路并保存原因。
8. loser不获取Torrent、不调用AI、不创建下载任务；winner失败只重试原任务，不隐式晋级。
9. 两个独立开关的四种组合、批次规则快照、Web预览/真实执行一致性、revision冲突和回滚全部通过。

## 人工规则、完成记录与删除场景

1. `/Home/Bangumi/3951`、尾斜杠和 query 均解析为 `mikanid=3951`；新 UI/API 不把它显示为 Bangumi Subject ID。
2. 相同 `mikanid` 的不同字幕组、标题和 Torrent 命中同一人工规则，共享 `bgmid`、TMDB Series/Season 和 Episode Offset。
3. 人工规则命中后自动 TMDB、Backtrace、AI、TitleSeason、FirstSeason 调用数均为 0；规则无效显式失败，不静默覆盖。
4. 一个 Episode 全流程成功后原子写完成记录；相同 RSS 条目再次进入时在 RSS 解析阶段停止，后续过滤/元数据/下载器调用数均为 0。
5. 下载成功但文件策略、重命名、NFO 或目录库提交失败时不写完成记录；修复后可以重试。
6. 两个并发 RSS 条目在提交下载器前事务复查，只允许一个创建任务并最终取得完成记录。
7. 删除业务记录中的“已下载完成记录”后，同集再次解析可以重新进入流程；下载器任务、下载源和媒体库文件保持不变。
8. 四类删除分别单独执行并验证互不隐式级联；组合删除显示准确计划，部分失败保留可重试审计，路径越界时零文件删除。
9. 作品详情分别显示 Series、Season、Episode 获取阶段；人工偏移、直接匹配、Backtrace、AI、TitleSeason、FirstSeason、Other 和 BangumiFallback 均显示正确。
10. Mikan 完成的 `(tmdb=100, season=1, episode=1)` 从 U2/TTG 输入时同样跳过；同剧集 Episode 2 不受影响。
11. 多文件 Torrent 含已完成 EP1 和未完成 EP2：EP1视频及绑定字幕为 unwanted，EP2视频/字幕为 wanted；下载器实际传输字节和最终文件均不包含EP1。
12. qBittorrent 在 paused 状态完成 metadata/文件索引校验后才设置 priority 并恢复；路径/容量不符时不启动下载。
13. 删除规范 EP1 完成记录同时失效 Mikan/U2/TTG alias；任一来源随后均可重新下载 EP1。

## 字幕整理场景

1. `Anime.01.mkv` 与 `Anime.01.ass` 绑定并一起重命名。
2. `.zh-CN.ass`、`.zh-Hans.forced.ass`、`.chs.default.ass`、`.eng.sdh.srt` 等多段后缀绑定同一视频，输出保留完整后缀且互不覆盖。
3. 大小写不同的字幕扩展名、嵌套相对目录和 `.idx/.sub` 成对字幕正确处理。
4. stem 不同但字幕文件名能解析出唯一来源 EP 时绑定；同一任务存在多个候选时标记 `SubtitleAmbiguous`，不得按顺序猜测。
5. 已绑定字幕继承视频的人工 Episode Offset 或 AI 最终 TMDB Episode，不独立再偏移或调用 AI。
6. 未匹配字幕在季度已知时保留原名进入 `Sxx/Other`，季度未知时留在下载目录；字体、图片、校验文件及其他非字幕附件始终不移动。

## TMDB 季度回溯场景

1. Backtrace 关闭：保持 AnimeGo `develop` 行为，不读取 Bangumi 前传关系。
2. Skip 与 Backtrace 同时开启：Skip 优先级 4 立即终止，前传请求数为 0。
3. 当前 Bgm 首播日期已命中：即使 Backtrace 开启也不读取前传。
4. 第一层或更深前传命中：对该前作按日文名、中文名、首播日期重新验证完整 `tmdbid + Season`，允许采用与当前候选不同的 TMDB Series；TitleSeason/FirstSeason 不执行。
5. 同一搜索词返回多个合格 Series：按精确名称、相似度、返回顺序逐个验证，首个候选季度失败后继续第二候选；本轮耗尽后才进入下一清理搜索词。
6. 回溯到首部仍无匹配：依次尝试 `TMDBFailUseTitleSeason=2`、`TMDBFailUseFirstSeason=1`；P2 只读取任务 `title` 并采用本地解析季度，P1 固定采用本地 `S01`，两者都不请求或验证 TMDB Season。
7. 前传缺日期、多前传及循环：遍历次序稳定、无重复请求、可终止。
8. 前传请求瞬时错误恢复；重试耗尽时记录 `BacktraceError`，然后执行较低优先级策略。
9. 当前作品日文名、中文名均未找到 Series：只要存在 `bgmid`，Backtrace 仍发起关系请求并可由前作恢复不同的 `tmdbid + Season`；耗尽后才进入独立 AI/完全失败兜底，P2/P1 因缺少有效 Series 标记不适用。
10. Web 配置页修改四个确定性开关及一个任务级 AI 元数据开关后，私有配置、重载值和阶段说明一致；旧双开关仅用于兼容读取。
11. `Disabled`、`NotApplicable`、`NoMatch`、`Error`、`Succeeded`、`Terminated` 六类策略结果分别持久化正确；最终失败保留已确认的上级 ID。
12. TMDB搜索成功返回空结果/无可接受TV候选，且bgmid有效、兜底开启：`failure_kind=SemanticNoMatch`、`tmdb_access_confirmed=true`，固定使用本地S01并写`tmdbid=0`；P2/P1开关和标题季度不得改变它。
13. DNS/连接/TLS/代理/超时/取消/408/429/5xx/断路器分别重试耗尽：即使bgmid有效也不得下载或写NFO，状态为重试/服务故障且Web显示兜底拒绝原因；已验证Series但仅季度失败也不得进入完全兜底。
14. API Key缺失、401/403、endpoint错误、响应截断/畸形、输入非法、人工规则无效和候选歧义分别验证：全部禁止`tmdbid=0`，进入配置或人工修复。
15. 首次网络失败、后续重试成功并得到确定性空结果时允许按SemanticNoMatch兜底；仅AI/MCP声称未找到而TMDB权威请求从未成功时禁止。
16. DNS/超时/429/5xx 标记为可重试，401/403/API Key 缺失标记为配置错误，无结果/歧义/字段冲突标记为需人工处理；所有原因均不泄漏密钥、Authorization 或完整 Prompt。

## AI 与 TMDB 规范命名场景

1. 四个确定性季度失败开关和一个 `ai_use_metadata_match` 任务级开关的新安装默认值均为 `false`；任一旧 AI 开关为真可兼容启用，但显式规范键优先。
2. AI 关闭时请求数为 0；开启但配置缺失时返回明确配置错误并继续较低优先级策略。
3. 每个 AI 请求只发送任务总标题、候选视频的任务内相对文件名/字节容量、可空 `bgmid`/`anidbid`/`imdbid`，以及Mikan单文件门禁所需的 `torrent_file_count/published_at/bgm_episode_candidate/use_bangumi_pubdate_first`；断言不包含重复的文件名EP字段、输入源/下载器配置、宿主机绝对路径、Bangumi详情、API Key、Cookie 或其他配置。
4. 模型一次返回单文件或多文件的有效 Series/Season/Episode，经 TMDB API 逐项二次验证后生成规范字段。
5. 模型返回不存在的 ID、Season 0、日期冲突、外层/业务畸形 JSON、零个或多个 `choices`：拒绝对应候选；不得静默采用多个候选的第一项。Series/普通季度已确认但 Episode 缺失时保留原名进入季度 `Other`。
6. TMDB `zh-CN` 名称存在时用中文目录名；缺失时用 TMDB `original_name`，不得使用 Bangumi 名称替代。
7. 来源 EP=1、TMDB EP=67：保留 `SourceEpisodeNumber=1`，最终文件为 `E067`，去重键和目录 DB 使用规范集号。
8. 多文件映射存在 Episode 缺口：已确认季度的缺口文件进入 `Other`，其余已验证文件正常落盘；Series/Season 缺失、重复目标或目标冲突的对应文件不落盘并可幂等重试。
9. AI/网络超时、429、5xx、取消和缓存命中分别验证；日志不得包含密钥或完整敏感请求头。
10. NativeAOT 发布二进制执行 fake AI → fake TMDB → 重命名 smoke，无反射序列化警告。
11. 确定性季度成功且本地 EP 与 TMDB 同号 Episode 的标题/日期一致：直接采用，AI 请求数为 0。
12. 同号不存在或存在标题/日期冲突：`ai_use_metadata_match=false` 时进入 `EpisodeUnmatched`；开启且此前未尝试时整个下载任务恰好一次语义 AI 调用。
13. Episode 阶段首次触发统一 AI 时，返回不同 TMDB ID/Season、缺失 Episode、文件列表不一致或无效 JSON：拒绝候选，不改写已确认季度，不使用来源 EP 重命名；季度阶段已尝试时不得再次调用。
14. 多文件仅一个 EP 需要补全时仍按整个下载任务调用一次；未匹配文件按季度 `Other` 规则处理，不影响其他已验证文件。
15. 特别篇、OVA、NCOP、NCED 不匹配 Season 0；季度已确认时保留原名进入 `Sxx/Other`，季度未知时留在下载目录。
16. BD 多文件任务中正片 1–12 全部映射，Menu/Summary/PV/NCOP/NCED/Logo 均返回 `matched=false + season>0 + episode=null` 时，顶层 `matched=true`，全部文件按正片或 `Other` 路径落盘。
17. 任一非正片文件返回 `season=null`、Series 未确认或存在映射冲突时，顶层 `matched=false`，不得用猜测季度安排该文件。
18. `bgmid=null` 时无 Bangumi工具；有值时才连接 BGM MCP并只把结果作为辅助证据。TMDB MCP在两种情况下始终先于 Web Search。
19. `anidbid=null` 时无 AniDB工具；有值时只取固定映射JSON的 `tmdbtv`，候选错误/空/404不直接失败，未经 TMDB MCP验证不得采用。
20. MCP返回充分数据时 Web Search调用数为0；MCP无结果、错误或信息不足时才允许 Web Search，并记录回退原因。
21. 非空 `bgmid`/`anidbid`/`imdbid` 与任务标题及文件组的作品级绑定必须保留；构造 Bangumi/AniDB/IMDb/TMDB 标题字面不同但实际同作品的 fixture，不能仅因标题不等而拒绝候选。
22. 构造 Bangumi/AniDB 来源 EP 与 TMDB Episode Number 不同的 fixture；不得直接复制或按同号确认，只有 TMDB Season/Episode 验证通过后才能生成规范集号。
23. imdbid 为空时不发起 external ID 查询；非空时通过 TMDB MCP find/external ID 查候选，Movie、无结果、格式错误和 TV 标题差异分别验证，未经 Season/Episode 验证不得采用。
24. 配置开关开启、Mikan Torrent实际文件条目数为1且bgmid/pubDate有效：主程序先取得最近普通EP并写入 `bgm_episode_candidate`，候选成功时门禁才为真；Prompt直接结合文件名EP调用TMDB MCP，最终集号仍经TMDB验证。
25. 分别用Torrent单文件模式和“根目录下仅一个文件”的fixture验证文件数均为1并触发；目录节点不能被误计为文件。实际文件条目数大于1时门禁为假。
26. pubDate无时区按 `Asia/Shanghai` 规范化；仅 Mikan 可把该值作为 AI 辅助参数。开关关闭、缺失/非法日期、非Mikan、无bgmid、Bangumi查询失败或无普通正整数Episode时不产生日期候选并转入原通用AI流程；Torrent 发布日期不设置天数拒绝窗口。
27. 文件名EP与Bangumi日期EP相同/不同各建fixture；两者都只是TMDB定向搜索证据，不得直接复制为最终集号。优先分支和通用分支都必须拒绝Season 0并二次验证。
28. 记录日期优先分支的工具次数和回退原因；MCP endpoint/schema发现跨请求复用，同一bgmid缓存不得导致新播Episode永久不可见。
29. 所有来源的 AI Prompt、请求 JSON 和响应 Schema 均不含 `file_episode_candidate/episode_offset`；Mikan 本地状态中的候选由后端重算并拒绝调用方伪造。正/零/负偏移由主程序在 TMDB 逐文件验证后本地计算；偏移不一致或跨季度时不产生缓存证据，但已验证的逐文件映射仍有效。
30. `(mikanid,groupid)` 相同、签名一致且来源EP不同的三个成功样本升级Trusted；重复EP不增加计数，不同键隔离，学习期冲突重置，Trusted后冲突撤销。
31. 可信缓存默认关闭且关闭时零读写；该缓存只在主程序 AI 调用前使用。命中包含有效 `tmdb_id`、普通 `season` 和偏移的 Trusted 记录后，本地计算每个 `candidate+offset` 并断言 AI 请求数为 0、该短路分支的 TMDB Episode 请求数也为 0；缺候选、计算结果非正数、记录缺少有效 TMDB/季度或普通文件存在歧义时回退正常流程。

## 多输入源与下载器路由场景

1. 配置两个命名 qBittorrent 实例 `bt`、`pt`；Mikan profile 只向 `bt` 添加，U2/TTG profile 只向 `pt` 添加。
2. 新安装默认Mikan profile为`move`；下载完成后暂停，移动和整理成功后用`DeleteFile=false`移除下载器任务并写完成记录，源目录消失、媒体库存在且不做种；任一步失败不得丢失源文件或标完成。
3. Mikan 缺 `bgmid` 立即拒绝；U2 的 `anidbid` 和 TTG 的 `imdbid` 为空时仍可凭 title/files 进入匹配，非空格式错误时拒绝。
4. 旧 Mikan `/api/rss`、`/api/download/manager` 与新 `/api/v1/ingest` 对同一输入生成相同内部 command、规则版本和下载器路由。
5. 修改 SourceProfile 的下载器绑定或文件策略后，新任务使用新快照；已经创建的任务继续使用原实例、策略和 profile 版本。
6. `bt` 离线不影响 `pt`；不同实例认证、超时、熔断、category/tag 和任务列表相互隔离。
7. `bt`、`pt` 分别使用 `/download/incomplete/bt`、`/download/incomplete/pt`，共同媒体库为 `/download/anime`；两个容器均通过同一父挂载的硬链接探测。
8. 删除仍被 source profile 或活动任务引用的下载器实例被拒绝；禁用实例保留历史任务可读信息。
9. Web 路由预览与实际 ingest 结果一致；任务详情的 `source_evidence` 显示 source/profile revision、来源标题、metadata IDs 和不透明 work/item ID 的域隔离 SHA-256 指纹，不返回 ID 原值、URL 或 passkey；TMDB 规范字段只显示验证结果。
10. 带 passkey 的 Torrent URL 成功导入后，日志、API响应、Web、审计和AI请求均找不到完整 URL、path/query/announce；只保存来源和不可逆指纹。
11. 未授权 host、跨host redirect、DNS重绑定、超长响应、非torrent、路径穿越和 info-hash 不一致均在进入下载器前拒绝。
12. staging `.torrent` 权限受限且不进入备份；下载器确认接收后删除，模拟崩溃后由 TTL 清理任务回收。
13. 旧配置含Transmission实例时主程序/Web仍可启动并显示`UnsupportedDownloaderType`；该实例不可测试连接、启用或绑定新profile，且不会静默创建qB实例。Web首版新建类型只有qBittorrent。

## 下载 E2E 场景

1. 启动隔离网络、fixture server、qBittorrent `bt`/`pt` 和独立 qBittorrent fixture seeder。
2. 生成合法小文件和 torrent，不访问公网 tracker。
3. 通过 RSS 注入一个 episode。
4. 验证 source/parser/filter 得到预期 AnimeEntity。
5. 验证客户端收到 category/tag/save path/seed time。
6. 等待下载状态完整经过 downloading → seeding → complete。
7. 分别验证四种文件策略、目录 JSON 和 NFO。
8. 重启 AnimeGoNet，验证去重和恢复。
9. 删除任务，按配置断言是否删除原文件。
10. 分别以 `bt`、`pt` 作为被测实例重跑，断言连接、缓存、category/tag、文件选择和故障状态完全隔离。

## Web UI E2E 场景

1. 从 NativeAOT 发布目录或生产 Docker 镜像启动，不使用前端 dev server。
2. 输入正确/错误 access-key，验证会话和认证错误。
3. 仪表盘展示服务、下载器、任务和错误状态。
4. 提交 fixture RSS，观察解析结果和下载状态变化。
5. 修改配置，先验证预览无写入、字段级即时/重启效果、密钥三态脱敏；表单再变化时旧预览失效。明确确认后验证旧 revision 备份与新 revision 原子写入，恢复部署默认前也生成备份；构造同 revision 不同备份内容时必须拒绝覆盖当前配置。
6. 修改插件 args/vars，验证校验失败不会落盘。
7. 查看/过滤/暂停/恢复实时日志，并模拟 WebSocket 断线重连。
8. 浏览缓存并删除测试 key，验证二次确认和结果刷新。
9. 在桌面与窄屏视口跑关键流程和键盘导航检查。
10. 刷新所有前端路由，验证静态 fallback 正常且 API 404 不被吞掉。
11. 修改数据更新私有覆盖，验证 revision 冲突、Cron 热重排、禁用/恢复默认，以及环境变量覆盖字段只读；部署 YAML 保持不变。
12. 构造标题未找到、Series 未找到、Season 未找到、TMDB 网络错误和认证错误，验证失败中心的筛选、最终原因、尝试时间线、重启保留及重新匹配操作。
13. qB任务从metadata未知依次进入queued/downloading/paused/stalled/checking/100%，断言页面状态、百分比、容量、速度和ETA正确；100%后move/NFO失败仍显示业务未完成。
14. 多文件任务包含wanted/unwanted及不同priority，断言列表/详情总容量和百分比只统计wanted文件，文件表映射正确。
15. `bt`活动、`pt`离线时断言同步/熔断隔离、速度合计不含stale、pt保留最后快照和时间；重启后按实例+hash恢复且不重复添加。
16. 暂停/恢复重复调用幂等，陈旧revision拒绝，业务重试沿用原快照；删除操作只能进入删除中心预览。
17. 活动/空闲/页面隐藏刷新频率符合设计，浏览器不直连qB；API/日志/DOM/截图不包含密码、passkey、announce或完整敏感路径。
18. 构造一个有 12 个 TMDB Episode 的普通季度；来源只含 EP 1、3，另含 Menu/NCOP/字幕，断言网格仍恰有 12 格，只有具有有效规范完成记录的 EP 标为已下载，附属文件不计数。
19. 依次模拟等待下载、下载中、整理失败、完整成功和删除完成记录，断言只有完整成功为已下载，删除完成记录后恢复未下载；保留完成记录但删除媒体文件时显示一致性警告。
20. 分别按最后更新日期、TMDB 名称、TMDB Season 开播日期和加入日期升/降序分页；验证空开播日期置后、同值稳定排序、翻页无重复漏项，默认最后更新日期降序。
21. Cover 依次验证 Season poster、Series poster、本地占位图和缓存命中；浏览器请求、DOM、日志中不得出现 TMDB API key。
22. 构造 TMDB 未解析与 Bangumi `tmdbid=0` 兜底记录，断言它们只出现在“待补全 TMDB”，没有 TMDB EP 网格或完成比例；恢复并验证真实 TMDB 映射后才进入标准季度列表。
23. 对同一 `mikanid+来源EP` 并发提交不同字幕组、不同 Torrent 和不同下载器路由，断言 SQLite 只有一个活动 claim 且至多一个下载器收到该文件；第一项成功后其余输入早停，失败/崩溃恢复不会盲目重下，其他 EP 不受影响，页面标明去重范围仅为同一 mikanid。
24. 对两个来源提交标题/容量相似但没有共同可靠 Episode 身份的文件，断言系统不跨来源误拦截，并在待补全详情提示可能重复；相同 source item、info-hash 或文件指纹仍可幂等早停。
25. 将多个 fallback 记录恢复到同一 TMDB Episode，断言事务只产生一个规范完成记录，其他项标记 `DuplicateAfterResolution`，不新增下载任务、不自动删除或移动冲突文件。

## Docker 路径 E2E

1. 从官方 Docker YAML 读取 `data_path=/data`，数据库、日志、配置备份和插件元数据只写入 Compose 对应的 `/data` 卷。
2. `download_path=/download/incomplete`，下载器与 AnimeGoNet 读取同一个 fixture 文件。
3. `save_path=/download/anime`，分别执行 `link`、`link_delete`、`move`、`wait_move`。
4. 断言硬链接 inode/文件标识符合平台预期，并确认没有 `EXDEV` 跨卷错误。
5. 将外部下载器配置为不同可见路径，验证 `client.download_path` 转换；映射错误时启动诊断给出可操作提示。
6. 不设置路径环境变量时，断言最终有效路径逐字等于 Docker YAML；设置覆盖后，Web 和启动日志必须明确标记来源。

## 每次模块提交的证据

提交前至少保存并在提交说明中列出：

- 执行的 verify 命令。
- 通过/失败/跳过数和失败白名单链接。
- 是否执行 NativeAOT publish/published smoke。
- 使用的上游 fixture 或真实容器版本。
- 行为偏差及其已批准的理由；无偏差写 `Parity deviations: none`。
