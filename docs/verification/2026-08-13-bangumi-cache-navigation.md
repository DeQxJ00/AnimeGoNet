# Bangumi 缓存一级菜单验收（2026-08-13）

## 边界

- 左侧新增一级 `bangumi缓存`，URL 为 `#/bangumi-cache/versions`。
- 既有 AnimeGoNetData 版本状态、检查更新、仅下载、下载并导入、离线 ZIP 导入、已下载包导入和回滚操作原样迁入该工作区，不建立第二套 API 或状态。
- 页面管理的是版本化 `bangumi_archive_subjects`、`bangumi_archive_episodes` 与 `bangumi_archive_subject_relations` 数据。
- `系统 / 缓存管理` 继续管理通用 `bolt`/`bolt_sub` JSON 缓存；`bolt/themoviedb` 不归入 Bangumi 工作区。

## 验收

- TypeScript strict 检查和静态资源重新生成通过。
- `WorkspaceNavigationTests` 与 `StaticWebUiTests`：140/140 通过。
- 本地浏览器访问 `#/bangumi-cache/versions` 后标题为 `bangumi缓存 · AnimeGoNet`，仅显示“数据版本与更新”，正确读取 active `2026.08.11.2` 和 previous `2026.08.04.2`。
- 本地浏览器访问 `#/system/cache` 时只显示“缓存管理”，Bangumi 数据更新区保持隐藏。
