# 2026-07-29 下载仪表盘验收

## 已实现

- 下载列表 API 返回未套用当前筛选的全局 `summary`。
- 汇总活动、暂停、失败、stale、等待整理、完成、准备失败和整理失败任务。
- 连接速度只累加 `connected=true` 且 `is_stale=false` 的任务快照。
- qB `state` 与 AnimeGoNet `business_status` 使用独立服务端筛选参数。
- 默认排序为失败优先、活动/暂停其次、历史完成最后，再按更新时间和 job ID 稳定排序。
- 静态 TypeScript WebUI 显示汇总卡片、最近安全失败码、离线实例和最后成功同步时间。

## 安全与测试

- 汇总只返回稳定失败码，不返回异常正文、Torrent URL、passkey、Cookie、下载器凭据或绝对路径。
- 数据层测试覆盖在线速度、离线 stale 不计速度、离线实例计数、最近失败码和最后成功时间。
- API 测试覆盖业务阶段筛选和汇总 DTO；静态资源测试覆盖仪表盘与筛选控件。
- `npm run web:check` → `npm run web:build` → `npm run web:check`：通过。
- `dotnet test AnimeGoNet.slnx --no-restore`：708/708 通过（App 354、Core 228、
  Data 115、插件契约 11），0 失败、0 跳过。
- win-x64 NativeAOT Release 发布通过，无 trim/AOT 警告；`eng/smoke-native.ps1`
  通过 schema v24、原生 JSON、SQLite、静态 WebUI 和安全导入检查。
