# Bangumi 完全兜底最终决定投影（2026-08-01）

## 范围

- 继续以 SQLite `metadata_resolution_runs` 为唯一权威来源，不修改既有兜底业务
  门禁。
- 元数据任务列表和详情返回最高 attempt Run 的 `latest_run_status`、
  `tmdb_access_confirmed`、`bangumi_fallback_eligible`、
  `bangumi_fallback_denial_reason`。
- WebUI 只对失败或已兜底 Run 显示决定：明确区分 TMDB 权威访问已确认的确定性
  无匹配、未确认访问的网络/服务/配置类失败，以及已经使用固定 S01 的例外路径。
- 决定不从单条策略 Attempt、`retryable` 或任务显示分类反推；没有 Run 和正常 TMDB
  成功任务不显示伪造资格。

## 门禁审计

- `CompleteBangumiFallbackAsync` 继续同时要求有效 bgmid、
  `SemanticNoMatch`、`TmdbAccessConfirmed=true` 和正数本地季度。
- `FailAsync` 拒绝把非 `SemanticNoMatch + access_confirmed` 写成 eligible。
- 自动处理器故障注入覆盖 Network、RemoteService、Authentication、Configuration、
  Protocol、InvalidInput、Ambiguous；全部保持 `fallback_eligible=false`、稳定拒绝原因，
  且不创建 `anime_series.tmdb_series_id=0`。

## 验证

- `npm run web:build`：TypeScript 7 构建通过。
- Data 聚焦测试 18/18 通过。
- App/API/WebUI 聚焦测试 112/112 通过。
- `dotnet build AnimeGoNet.slnx -c Release --no-restore`：0 warning /
  0 error。
- 全量 `ANIMEGO_UPSTREAM_REPO=E:\WorkSpaceAI\AnimeGoNet\AnimeGo dotnet test
  AnimeGoNet.slnx -c Release --no-build`：1018/1018 通过
  （Plugin 11、Core 301、Data 161、App 545）。
- `win-x64` NativeAOT publish 成功，无 trim/AOT warning。
- `eng/smoke-native.ps1` 使用发布后的原生程序验证 schema 32、SQLite、静态
  WebUI、WebSocket 与安全配置投影并正常清理。

测试全部使用临时 SQLite、fake TMDB/Bangumi 和静态 WebUI；未访问用户 TMDB key、
qBittorrent、TestSpace、Cookie 或 passkey。
