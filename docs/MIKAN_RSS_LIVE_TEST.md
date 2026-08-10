# Mikan 私人 RSS 显式测试

入口：`eng/mikan-rss-live-audit.ps1`。该测试不会由默认单元测试或 CI 自动执行。私人 RSS URL、Cookie、凭据和 passkey 不得写入参数、配置文件、报告或 Git；只允许放在当前进程环境变量中。

该测试真实请求 RSS，调用生产 RSS 读取/解析代码，并以当前默认黑名单和有序优先规则生成逐条决策。默认继续读取 Mikan 条目/作品页，解析 mikanid/groupid/bgmid，调用 Bangumi 与 TMDB 完成 Series/Season/EP 验证。它不会访问 Torrent URL、不会下载 Torrent、不会创建 qBittorrent 任务，也不调用 AI。可用 `-SkipMetadata` 只检查 RSS。

```powershell
$env:ANIMEGONET_MIKAN_RSS_URL = '<private RSS URL>'
$env:ANIMEGONET_TMDB_API_KEY = '<private TMDB key>'
& .\eng\mikan-rss-live-audit.ps1
Remove-Item Env:ANIMEGONET_MIKAN_RSS_URL
Remove-Item Env:ANIMEGONET_TMDB_API_KEY
```

报告写入 `TestSpace/animegonet_data/mikan-rss-live-audit/`。报告包含标题、发布时间、mikanid/groupid/bgmid、Bangumi/TMDB 标识、搜索标题、最终季度/EP、失败代码、筛选决定和规则组轨迹；URL 只保留不可逆 SHA-256 指纹，查询串、token、Torrent URL 均不输出。`title_season_hint` 仍只是未验证的本地标题提示，只有 `canonical_tmdb_season` 是 TMDB 验证后的季度。
