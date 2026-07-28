# Bangumi Episode fallback scope（2026-07-28）

## 规则

fallback 去重身份优先级：

1. 唯一普通 Bangumi Episode ID；
2. 同一 mikanid + 来源 Episode；
3. 同来源作品 + 来源 Episode；
4. Torrent/文件指纹。

`BangumiEpisodeIdentityResolver` 只接受正整数来源集号，只匹配 Bangumi `type=0`，并要求 Episode ID 唯一。小数、特别篇、非数字、非正数和同编号多条记录返回无可靠身份。

自动 TMDB 完全失败分支在写 SQLite claim 前最多读取一次对应 bgmid 的 Episode 列表，并按文件解析 ID。成功时 `FallbackDedupScopeResolver` 以 `bangumi_episode` 覆盖任何来源本地身份；失败或歧义时保守降级。Bangumi 网络/服务错误记录为 `bangumi_fallback_episode_identity` attempt，不把较弱身份伪装为跨来源全局去重。

## 验收

- Core：唯一普通集、小数/特别篇/非正数、歧义编号。
- Data：Bangumi ID 高于 mikan/source/torrent，两个不同来源得到相同 scope。
- App：权威 TMDB no-match → Bangumi fallback 实际调用 Episode API，并在 SQLite 创建 `bangumi_episode / 1001` claim。

实际验证：

- `dotnet test AnimeGoNet.slnx --no-restore`：548/548 通过（Core 204、Data 92、App 252）。
- `dotnet publish src\AnimeGoNet.App\AnimeGoNet.App.csproj -c Release -r win-x64 --self-contained true /p:PublishAot=true --no-restore`：通过。
