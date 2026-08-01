# 下载做种生命周期验收

## 范围

- SourceProfile 的 `seeding_time_minutes` 在 dispatch 时复制为 download job 不可变目标。
- qBittorrent `torrents/info.seeding_time` 以秒读取，不依赖反射 JSON。
- schema v33 保存 `not_required/waiting/seeding/completed`、单调累计秒数和首次完成 UTC。
- `0` 不阻塞；正数达到目标或 qB 报告完成时完成；`-1` 不因累计时间自动完成。
- `wait_move` 与 `link/link_delete` cleanup 只读取持久化做种门禁；原始 qB state 单独变成 `complete` 不会绕过门禁。
- 下载列表和详情 API/WebUI 显示目标、状态、累计时间、正数目标百分比和完成时间。

## 自动化证据

- Core 聚焦测试：8/8，通过目标边界、状态推进、无限目标、完成/累计值不回退和非法值拒绝。
- Data 聚焦测试：8/8，通过 v32→v33 数据回填/约束/索引、快照同步、审计和重启可读投影。
- App/API/WebUI 聚焦测试：106/106，通过 qB JSON 字段、四种文件策略持久化门禁、API 字段和静态 UI 资源。
- `npm run web:check` 与 `npm run web:build` 通过；生成的 `wwwroot/app.js` 已更新。
- Release 解决方案编译通过，0 warning、0 error。
- 全量 Release 回归 1031/1031：Plugin Abstractions 11、Core 309、Data 163、App 548。
- `win-x64` NativeAOT 发布完成 `Generating native code`，无 trim/AOT warning；发布后的原生程序通过 first-start 与 legacy-YAML-upgrade 两种 smoke，均确认 schema v33、静态 WebUI、SQLite 和安全退出。

本模块不连接用户正在运行的私人 qB 任务；真实 qB/TestSpace 仍只由显式隔离 integration fixture 执行，默认单元测试和 CI 不启动本机 qB。
