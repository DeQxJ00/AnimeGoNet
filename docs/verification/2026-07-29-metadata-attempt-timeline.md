# 元数据策略尝试时间线验证（2026-07-29）

## 范围

本增量补齐已有 `metadata_resolution_runs` / `metadata_resolution_attempts` 审计表的查询与展示闭环：

- 每次策略尝试保存阶段、策略、优先级、结果、错误码、脱敏原因、可重试性、运行/尝试次数、耗时和 UTC 时间。
- 未提供独立原因时以稳定错误码作为安全原因；原因拒绝控制字符并限制为 512 个字符。
- `MetadataResolutionStore.ListAttemptsAsync` 按任务跨运行查询，最新记录优先，限制为 1～500 条。
- `GET /api/v1/metadata/tasks/{taskId}/attempts` 返回版本化、source-generated JSON；未知任务与非法 limit 返回稳定错误。
- WebUI 元数据任务卡片可展开/收起策略时间线，显示运行状态、阶段、策略、P 优先级、结果、耗时、错误原因和可重试性。
- 页面自动刷新后，已经展开的任务仍保持展开并重新读取服务端数据。

API 不返回任务标题、Torrent URL、passkey、绝对文件路径或内部租约信息。

## 自动验证

专项测试覆盖：

- SQLite 写入、默认安全原因、显式安全原因、关闭并重新打开数据库后的查询。
- 多次运行的稳定倒序、运行状态、错误码、原因、可重试性和耗时投影。
- 未知任务、非法 limit 与响应不泄漏 passkey/Torrent URL。
- 静态 WebUI 产物包含时间线 API、展开控件、错误与可重试性标识。

验证结果：

- `npm run web:check` 与 `npm run web:build`：通过。
- `dotnet test AnimeGoNet.slnx -c Release --no-restore`：616/616 通过（插件 11、Core 215、Data 101、App 289）。
- `dotnet publish ... -r win-x64 --self-contained true -p:PublishAot=true`：完成 `Generating native code`，0 warning / 0 error。
- `eng/smoke-native.ps1`：原生进程通过 `/ping`、schema 22、`native_aot=true`、SQLite 初始化、安全 ingest 拒绝、qB capability 和静态 WebUI 检查；进程与临时目录随后清理。
