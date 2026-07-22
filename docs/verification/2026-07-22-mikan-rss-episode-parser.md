# Mikan RSS 来源 Episode 解析验证（2026-07-22）

## 边界

`MikanRssEpisodeParser` 只解析 RSS entry title，用于构造同批候选的可靠分组键。它与 Torrent 相对文件名解析、`Auto_Bangumi/raw_parser.py` 的严格逐文件移植是三个不同边界，不改变 raw parser 的 1:1 兼容要求。

解析器兼容上游 Go `ParseEp` 的 `[04]`、`[11v2]`、` - 11`、空格整数、`EP12`/`E13`、`第14話` 和 `[15 END]`，并沿用上游贪婪语义选择 title 中最后一个有效 Episode 标记。因此 `[716][2022.07.23][1080P]` 得到 716，日期和分辨率不形成候选。

小数 Episode 保留规范化小数字符串但 `NormalEpisode=null`；SP/Special/OVA/OAD/PV/NCOP/NCED/Menu/S00E 分类为 Special；无可靠标记分类为 Unknown。后三类都不会伪装成普通整数，后续批次只允许相同 `mikanid + kind + value` 的可靠键进入多候选优选。

## 验收

- `dotnet test tests/AnimeGoNet.Core.Tests/AnimeGoNet.Core.Tests.csproj --no-restore --verbosity minimal`：131/131 通过。
- 新增 16 个 title cases，覆盖上游三条 ParseEp fixture、中文/全角括号、Doraemon 日期标题、小数、特别篇、纯日期/分辨率旁路。
- `dotnet test AnimeGoNet.slnx -c Release --no-restore --verbosity minimal`：Core 131、Data 53、App 127，共 311/311 通过。
- `dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 -p:PublishAot=true --no-restore -o artifacts/mikan-rss-episode-parser-win-x64`：NativeAOT 发布通过，无裁剪警告。

本模块不读取 RSS、不执行规则、不写 SQLite、不获取 Torrent，也不调用 TMDB/AI/qBittorrent。下一提交会把解析结果与 RSS document、版本化规则快照及 winner/loser 门禁组合成批次计划。
