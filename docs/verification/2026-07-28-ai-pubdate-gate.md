# Mikan `pubDate` → Bangumi Episode → AI 门控验证（2026-07-28）

## 范围

本模块把 schema v19 已保存的内部 Mikan 发布时间证据接入季度 AI 与后置 EP-AI：

1. metadata claim 从 SourceProfile 读取 adapter，并携带原始/规范 `pubDate`；
2. `torrent_file_count` 用该任务全部 `task_files` 计数，而不是仅计 pending 或候选视频；
3. 启用的同 `mikanid` 人工规则中的 `bgmid` 高于任务来源值；
4. 仅在配置开启、adapter=mikan、实际文件数=1、`bgmid` 和内部时间均有效时查询 Bangumi；
5. 从 Bangumi 普通正整数 Episode 中选择与来源本地日期最近者，同距优先不晚于发布时间，再按集号/ID稳定排序；
6. 本次验证时使用的 31 日 Torrent 发布窗口已被后续业务确认废止；现行实现不以 Torrent 日期差拒绝候选，Mikan `published_at` 只作为 AI 参数；
7. 人工 Series/Season 仍先于季度 AI，人工 EP offset 非空时仍抑制后置 EP-AI；
8. AI 结果仍须逐级重读并验证 TMDB Series/Season/Episode。

公开统一导入 API 不接受发布时间证据；该模块只消费 Mikan RSS 内部写入的 schema v19 字段。

## Bangumi 与 NativeAOT 边界

- 依据 Bangumi 官方 OpenAPI `GET /v0/episodes`：请求 `type=0`、每页200条、显式 offset，客户端硬限制总计10,000条。
- Episode page 和 item 使用 source-generated `System.Text.Json` DTO；不使用反射、动态对象或厂商 SDK。
- 分页 offset/limit/total 不一致、响应超限、无效 JSON、超时、网络、429/5xx 均转换为稳定安全码。
- 失败只写 `ai_pubdate` attempt，不把原始时间、URL、Prompt、Cookie 或凭据写入错误。
- 官方合约参考：<https://bangumi.github.io/api/dist.json>（检查版本 2026-07-24）。

## 验收证据

- Core：普通正整数过滤、只选已播条目、稳定 tie-break、小数/特别篇排除。原 31 日 Torrent 发布窗口已于 2026-08-09 按业务确认删除。2026-08-13 进一步确认：`±1` 日只适用于 Bangumi/TMDB 季度首播日期；单集 Episode 日期确定性映射只接受同一日。
- App HTTP：官方 Episode page 映射、跨页 offset、User-Agent、非法分页安全失败。
- Data：季度与 EP claim 均带 adapter/时间/实际文件数；`ignored` 文件仍计入实际 Torrent 文件数。
- App gate：非 Mikan、多文件、缺 bgmid、缺时间、配置关闭均零 Bangumi 请求；网络失败安全降级。
- 季度 AI：从同 `mikanid` 人工规则取得共享 `bgmid`，日期候选7进入请求并完成 TMDB 二次验证。
- EP-AI：已确认季度下同一可信证据进入请求，候选7通过已锁定 Series/Season 的 TMDB 验证。
- `dotnet test AnimeGoNet.slnx -c Release --no-restore --verbosity minimal`：Core 199、Data 73、App 227，共499/499通过。
- `dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 -p:PublishAot=true --no-restore -o artifacts/ai-pubdate-gate-win-x64`：通过，无裁剪或 AOT 警告。

本模块不访问本机 qBittorrent/TestSpace，不创建真实 Torrent 任务，也不修改下载和媒体路径。
