# RSS 输入解析验证（2026-07-22）

## 范围

本模块按上游 `internal/animego/feed/rss.go` 的可观察行为实现 RSS raw/file/URL 三种输入边界：只采用每个 item 的第一个 enclosure、没有有效 enclosure URL 的 item 跳过、非法或负数 length 记为 0，并从 Mikan 扩展 `torrent/pubDate` 保留 `T` 之前的发布日期。

`mikanid` 统一为正整数。解析顺序为显式 RSS source URL 优先、channel link 回退，支持 `/Home/Bangumi/{mikanid}` 和不区分大小写的 `bangumiId` query。带 passkey 的 Torrent URL 仅保存在内存 DTO；错误消息不回显请求 URL。

## NativeAOT 边界

- XML 使用 `XmlReader` 和 LINQ to XML，不依赖运行时反射或动态类型。
- 单次输入上限 5 MiB；HTTP 同时校验 Content-Length 和流式实际字节数。
- 禁用 DTD 和外部实体解析，URL 只允许 HTTP/HTTPS。
- 网络获取通过 `IRssFeedHttpClient` 注入，当前没有注册公网端点；后续接入时还需复用统一代理、Host redirect、重试与 SSRF 策略。

## 验收

- `dotnet test tests/AnimeGoNet.Core.Tests/AnimeGoNet.Core.Tests.csproj --no-restore --verbosity minimal`：115/115 通过。
- `dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj -c Release --no-restore --verbosity minimal`：127/127 通过。
- `dotnet test AnimeGoNet.slnx -c Release --no-restore --verbosity minimal`：Core 115、Data 53、App 127，共 295/295 通过。
- `npm run web:check` 与 `npm run web:build` 通过，生成的静态 JavaScript 无变更。
- `dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 -p:PublishAot=true --no-restore -o artifacts/rss-feed-parser-win-x64`：NativeAOT 发布通过，无裁剪警告。
- 覆盖 raw、文件、注入式 URL、空输入、损坏/非 RSS XML、DTD、缺少 enclosure、非法 length、Mikan pubDate、source URL 覆盖 channel link、文件和 HTTP 稳定错误分类、URL 脱敏，以及 HTTP 声明/流式容量超限。

本提交不接入 `/api/rss`，不写 SQLite，不调用 Torrent/TMDB/AI/qBittorrent，也不启动真实下载。下一模块会解析可靠来源 Episode，并把同批 RSS 交给已版本化的规则引擎。
