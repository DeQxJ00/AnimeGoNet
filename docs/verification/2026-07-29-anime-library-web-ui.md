# 动画作品库静态 WebUI（2026-07-29）

## 功能

- 原生 TypeScript/HTML/CSS，不引入前端运行时框架；
- 服务端分页前排序：最后业务更新时间、TMDB 名称、Season 开播日期、本地加入日期，均支持升/降序；
- 季度卡片显示同源 Cover、TMDB Series/Season、验证状态、规范 EP 完成比例和一致性警告；
- 详情显示 Series/Season 取得策略、日期、最后解析 run、snapshot 完整度；
- EP 网格只展示 API 返回的 TMDB Episode，支持全部/已下载/未下载筛选；
- EP 展开项显示 TMDB 名称、air date、时长、snapshot 时间、完成来源/时间和媒体路径一致性状态；
- 排序、方向、页大小、筛选和当前详情保存在浏览器本地；
- 详情展开期间不执行会重绘网格的自动刷新；
- Cover URL 只使用同源后端代理；页面不显示媒体绝对路径或 TMDB API key。

## 可访问性与响应式

- 季度卡片是具名按钮；EP 是具名、可展开的详情控件；
- Enter/Space 显式切换 EP，并同步 `aria-expanded`；
- 已下载/未下载同时使用符号和文字，不只用颜色；
- Cover 有描述性 alt，失败时更新失败文本；
- 1000/760/460 px 三档 CSS 网格降列，窄屏详情转单列。

## 自动与浏览器验证

- TypeScript strict `web:check` 与静态构建通过。
- 静态资产测试：29/29。
- 浏览器空库验证：正确显示 tmdbid=0 分流说明，分页按钮禁用。
- 浏览器真实临时 SQLite 数据验证：
  - 四种控件通过真实 API 查询；`name desc` 为 Beta→Alpha，`name asc` 为 Alpha→Beta；
  - 12 个 TMDB Episode 恰好产生 12 格，只有规范完成记录 EP1/EP3 为已下载；
  - 已下载筛选为 2/12，未下载筛选为 10/12；
  - Enter 展开 EP3 后 `open=true` 且 `aria-expanded=true`；
  - 刷新后保留 `name/asc/not_downloaded` 和当前季度详情；
  - DOM 中不存在测试 TMDB key、媒体绝对路径或 `image.tmdb.org` URL，三个 `<img>` 均指向同源 Cover API。
- TypeScript `web:check` / `web:build`：通过。
- Release 全量测试：654/654（Plugin 11、Core 215、Data 109、App 319）。
- `win-x64` NativeAOT 发布：通过，明确完成 `Generating native code`。
- NativeAOT 可执行文件启动、SQLite schema v23、受限导入和静态 WebUI smoke：通过。
