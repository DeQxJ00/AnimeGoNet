# legacy MikanTool 规则引擎（2026-07-22）

## 上游基准

直接对照 `wetor/AnimeGo@develop assets/plugin/filter/mikan_tool.py`，保留其拼写与可观察语义：

- `Filiter0` 全局规则按配置迭代；每项覆盖 `isPush0`，因此多个条目最终由最后一项决定，不改成 AND。
- `Filiter1` 键为 `key_{mikanid}_{groupid}`，优先于作品键 `Filiter2[mikanid]`，后者优先于字幕组键 `Filiter3[groupid]`；使用 `if/elif`，不会同时执行。
- `Filiter4` 用 raw parser 得到的第一个括号字幕组名称做 ordinal 精确键匹配。
- whitelist/blacklist 都使用大小写敏感普通子串：仅白=必须命中，仅黑=命中拒绝，白+黑=白命中且黑不命中，两者关闭=接受。
- 0、1/2/3、4 最终 AND。任一 1/2/3 配置存在时必须取得正整数 mikanid/groupid；身份解析失败等价于上游 item 外层异常，被丢弃。

## 验收

- `dotnet test tests/AnimeGoNet.Core.Tests/AnimeGoNet.Core.Tests.csproj --no-restore --verbosity minimal`：150/150 通过。
- 新增 13 个 cases：大小写差异、四种白黑开关、Filiter0 覆盖顺序、1→2→3 优先级、身份缺失和半角/全角/无括号 group。
- `dotnet test AnimeGoNet.slnx -c Release --no-restore --verbosity minimal`：Core 150、Data 57、App 132，共 339/339 通过。
- `dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 -p:PublishAot=true --no-restore -o artifacts/legacy-mikan-filter-win-x64`：NativeAOT 发布通过，无裁剪警告。

本模块是无 I/O 纯函数，不读取 Python、不访问 SQLite/Mikan 网络，也尚未接入 RSS pipeline。下一模块将保存原始 legacy 配置结构并实现 revision/导入导出，之后再接 Mikan 页面身份解析。
