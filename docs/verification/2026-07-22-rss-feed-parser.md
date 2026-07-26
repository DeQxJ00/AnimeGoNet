# RSS 输入解析验证（2026-07-22）

## 范围

本模块按上游 `internal/animego/feed/rss.go` 的可观察行为实现 RSS raw/file/URL 三种输入边界：只采用每个 item 的第一个 enclosure、没有有效 enclosure URL 的 item 跳过、非法或负数 length 记为 0，并完整保留 Mikan 扩展 `torrent/pubDate`。早期实现只保留 `T` 前的日期；schema v19 接入 AI 日期证据后改为保留原始时间字符串，避免丢失时分秒。

`mikanid` 统一为正整数。解析顺序为显式 RSS source URL 优先、channel link 回退，支持 `/Home/Bangumi/{mikanid}` 和不区分大小写的 `bangumiId` query。带 passkey 的 Torrent URL 仅保存在内存 DTO；错误消息不回显请求 URL。

## NativeAOT 边界

- XML 使用 `XmlReader` 和 LINQ to XML，不依赖运行时反射或动态类型。
- 单次输入上限 5 MiB；HTTP 同时校验 Content-Length 和流式实际字节数。
- 禁用 DTD 和外部实体解析，URL 只允许 HTTP/HTTPS。
- 网络获取通过 `IRssFeedHttpClient` 注入，当前没有注册公网端点；后续接入时还需复用统一代理、Host redirect、重试与 SSRF 策略。

## 验收

- 初始提交的 Core/App/全量测试、静态 Web 和 win-x64 NativeAOT 验证均通过；schema v19 的完整时间持久化由 `2026-07-26-mikan-publication-evidence.md` 继续记录。
- 覆盖 raw、文件、注入式 URL、空输入、损坏/非 RSS XML、DTD、缺少 enclosure、非法 length、Mikan pubDate、source URL 覆盖 channel link、文件和 HTTP 稳定错误分类、URL 脱敏，以及 HTTP 声明/流式容量超限。

本提交不接入 `/api/rss`，不写 SQLite，不调用 Torrent/TMDB/AI/qBittorrent，也不启动真实下载。下一模块会解析可靠来源 Episode，并把同批 RSS 交给已版本化的规则引擎。
