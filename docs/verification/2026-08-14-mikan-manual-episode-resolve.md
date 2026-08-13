# Mikan 单条 Episode 地址解析验收

## 行为边界

- `Mikan 手动设置 → 导入任务 → 单个 Torrent` 可先填写受支持的
  `/Home/Episode/{40位十六进制ID}` 地址。
- `POST /api/v1/ingest/mikan/resolve` 使用当前选中的、已启用的 Mikan
  SourceProfile；Episode 页面、分组 RSS、作品页统一使用该来源的 Cookie 与网络策略。
- 解析结果回填任务 title、Torrent URL、source item/work ID、`mikanid`、
  `groupid` 与可取得的 `bgmid`。只有随后点击“提交下载”才调用统一导入。
- 解析阶段不会暂存 Torrent、创建任务、连接 qBittorrent，也不会把 URL 写入
  浏览器本地存储；最终统一导入请求发出后清空 Mikan/Torrent URL 输入。
- Episode URL → `mikanid+groupid` 与 `mikanid→bgmid` 继续复用已有长期 SQLite
  缓存；失败结果不写成功缓存。

## 自动验证

- `npm run web:test`：27/27 通过。
- `dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj --no-restore
  --filter "FullyQualifiedName~AiMetadataTestApiTests|FullyQualifiedName~StaticWebUiTests"`：
  195/195 通过。
- `dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64
  -p:PublishAot=true -p:SelfContained=true`：通过。
- API fixture 明确断言只解析时 `ITorrentStagingService` 未被调用，并验证全部回填字段。

## 真实只读验收

2026-08-14 使用用户提供的公开 Episode 地址执行解析（未提交统一导入）：

- `source_item_id=391f813697a494787ff1d345894d2bcecc17cc4c`
- `source_work_id=3951`
- `mikanid=3951`
- `groupid=583`
- `bgmid=547888`
- title 成功取得，Torrent URL 成功取得但未写入报告。
- 未创建下载任务，未访问 qBittorrent。
