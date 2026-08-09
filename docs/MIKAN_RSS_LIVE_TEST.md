# Mikan 私人 RSS 显式测试

入口：`eng/mikan-rss-live-audit.ps1`。该测试不会由默认单元测试或 CI 自动执行。私人 RSS URL、Cookie、凭据和 passkey 不得写入参数、配置文件、报告或 Git；只允许放在当前进程环境变量中。

该测试真实请求 RSS，调用生产 RSS 读取/解析代码，并以当前默认黑名单和有序优先规则生成逐条决策。它不会访问 Torrent URL、不会下载 Torrent、不会创建 qBittorrent 任务，也不调用 AI。

```powershell
$env:ANIMEGONET_MIKAN_RSS_URL = '<private RSS URL>'
& .\eng\mikan-rss-live-audit.ps1
Remove-Item Env:ANIMEGONET_MIKAN_RSS_URL
```

报告写入 `TestSpace/animegonet_data/mikan-rss-live-audit/`。报告包含标题、发布时间、EP 解析、筛选决定和规则组轨迹；URL 只保留不可逆 SHA-256 指纹，查询串、token、Torrent URL 均不输出。
