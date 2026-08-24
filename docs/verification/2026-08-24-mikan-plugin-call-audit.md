# AnimeGoHelper Mikan 调用日志验收

## 记录范围

- `/api/download/manager`：单条请求显示为“单集”，多条显示为“批量单集”。
- `/api/rss`：`is_select_ep=false` 显示为“全集”，`is_select_ep=true` 显示为“选集”。
- 记录 TV / Movie、请求/接收/未接收数量、耗时、稳定失败码，以及每项的标题、`mikanid`、`groupid`、任务 ID 和状态。
- 不保存 Cookie、插件 AccessKey、RSS/Torrent URL、passkey 或请求原文；标题最多保存 1000 字符。

## WebUI 验收

入口：`输入源 → Mikan → 插件调用日志`。可按调用方式和成功/部分成功/失败筛选，展开后查看逐项标题和结果；存在任务 ID 时，“查看任务”链接直接打开“任务中心 → 匹配与整理”并按该任务筛选。

## 自动化验收

```powershell
dotnet test tests/AnimeGoNet.Data.Tests/AnimeGoNet.Data.Tests.csproj -c Release --filter "FullyQualifiedName~MikanPluginCallLogStoreTests"
dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj -c Release --filter "FullyQualifiedName~LegacyRssApiTests|FullyQualifiedName~LegacyDownloadManagerUsesSameMikanRouteAndEnvelope"
```
