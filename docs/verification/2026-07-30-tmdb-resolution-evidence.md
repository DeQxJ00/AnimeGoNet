# TMDB 三级取得证据（2026-07-30）

## 范围

- 增加显式 `TmdbResolutionSource`，覆盖人工覆盖、TMDB 标题/日期、P3 回溯、
  AI、P2/P1、本地/可信 Mikan offset、确定性 Episode 和字幕关联。
- SQLite schema 升至 v32。Series/Season 在解析 Run 完成时固化 source 和精确
  Attempt；Episode 按文件固化 source、Run 和精确 Attempt。
- 数据库触发器验证同任务、同 Run、正确 stage、相同 strategy 和 `matched` 结果，
  防止查询期推断或写入伪造证据。
- 任务列表、逐文件详情和正式作品库 API/WebUI 显示同一份权威证据。多个文件证据
  不同则返回 mixed 摘要并要求下钻，不虚构一个共同来源。

## 验证

- `npm run web:build`：TypeScript 7 构建通过。
- `dotnet build AnimeGoNet.slnx --no-restore`：0 warning / 0 error。
- 聚焦测试：
  - Core 12/12：所有 source 存储值双向映射及未知值拒绝；
  - Data 47/47：v31→v32 回填、触发器拒绝错误引用、完成事务、作品库投影；
  - App/WebUI 107/107：自动/人工/AI/Episode worker、视频与字幕各自精确
    Attempt、mixed API、静态 UI 和作品库引用。
- 全量 `ANIMEGO_UPSTREAM_REPO=E:\WorkSpaceAI\AnimeGoNet\AnimeGo dotnet test
  AnimeGoNet.slnx -c Release --no-build`：1002/1002 通过
  （Plugin 11、Core 301、Data 160、App 530）。
- `win-x64` NativeAOT publish 成功，无 trim/AOT warning。
- `eng/smoke-native.ps1` 使用发布后的原生程序验证 schema 32、SQLite、静态
  WebUI、WebSocket 与安全配置投影并正常清理。

全部测试使用临时 SQLite/fake TMDB/fake AI；未访问用户 TMDB key、qBittorrent、
TestSpace、Cookie 或 passkey。
