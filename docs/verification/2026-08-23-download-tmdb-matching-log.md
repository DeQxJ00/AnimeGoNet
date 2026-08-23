# 下载任务 TMDB 信息与匹配日志验收（2026-08-23）

## 行为边界

- 下载列表 API 从当前页任务的 `task_files` 批量读取已经持久化的正数 TMDB Series ID、可空 Season 和 Episode，不解析 Torrent 标题，不生成未验证占位。
- 多文件任务按 `task_id + TMDB Series + Season` 合并并去重 Episode；Series/Season 名称只取本地 TMDB 权威投影。
- WebUI 仅在 `tmdb_metadata` 非空时显示“已确认 TMDB 元数据”，并提供按任务 ID 进入“日志 / 匹配日志”的入口。
- 匹配日志复用现有元数据任务、详情与 Attempt API，不复制第二套业务状态；流程卡展示 Series、Season、Episode 实际策略、Run/Attempt 引用、错误和文件统计，入口会自动展开来源/TMDB 对照和策略时间线。

## 自动验证

- `npm run web:test`：42/42 通过，覆盖匹配日志二级页、下载卡条件渲染、任务关联和三阶段响应式流程。
- `DownloadManagementApiTests + StaticWebUiTests`：218/218 通过；API 用例同时验证未解析任务返回空数组、已解析任务返回 TMDB 82684 / S04 / E041 及详情页同一投影。
- win-x64 `.NET 10 NativeAOT` 发布完成原生代码生成，无 AOT/trim 错误。

## 本机真实 WebUI 验收

现有 TestSpace 数据以 JIT 主程序运行在 `http://127.0.0.1:6180/`，只读检查当前 93 个下载任务：下载页当前 25 张卡片均取得已落库 TMDB 映射。浏览器从第一张卡的“查看匹配流程”进入 `#/logs/matching`，精确筛到 1 个任务，显示三段流程并自动展开详情与 Attempt；示例显示 `TMDB 82684 / S02 / E017`，没有把 Torrent 标题中的 `41` 当作 TMDB Episode。

本轮未创建、暂停、恢复或删除 qBittorrent 任务，也未修改媒体文件、Cookie、passkey 或用户配置。
