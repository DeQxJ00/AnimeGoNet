# 运行日志 HTTP 连接筛选验收（2026-08-13）

## 行为

“日志 / 运行日志”的详细筛选增加“HTTP 连接”选项：

- 全部日志；
- 仅外部 HTTP 请求（Mikan / TMDB / Bangumi 等）；
- 仅 WebUI / API 入站；
- 排除 HTTP 连接日志。

详细筛选上方另提供“外部 HTTP（Mikan / TMDB / Bangumi 等）”快捷按钮，再次点击恢复全部
HTTP 范围；按钮与下拉框双向同步，并可继续与 AI、TMDB、匹配、下载、RSS、整理等业务域
筛选组合。摘要显示“外部 HTTP / 入站”计数，便于识别 ASP.NET Core 页面轮询噪音。入站识别覆盖 Hosting、Routing、
StaticFiles 和 Kestrel category；外连识别覆盖 `System.Net.Http`、
`Microsoft.Extensions.Http` 及标准 HttpClient 请求/响应消息。仅包含 URL 的应用启动、配置或
普通业务消息保持 `none`，不会被误报为真正的外部请求。

筛选仅作用于浏览器当前最新 500 条日志，不改变 WebSocket 协议、服务端文件日志、日志级别
或脱敏边界。

## 验收

- `npm run web:test`：21/21 通过；
- TypeScript strict build 通过；
- 测试分别断言外连、入站、非 HTTP 分类及“排除 HTTP”组合筛选。
