# 运行日志 HTTP 连接筛选验收（2026-08-13）

## 行为

“日志 / 运行日志”的详细筛选增加“HTTP 连接”选项：

- 全部日志；
- 仅程序外连；
- 仅 WebUI / API 入站；
- 排除 HTTP 连接日志。

该选项与 AI、TMDB、匹配、下载、RSS、整理等业务域筛选独立组合。摘要新增“HTTP 外连 /
入站”计数，便于识别 ASP.NET Core 页面轮询噪音。入站识别覆盖 Hosting、Routing、
StaticFiles 和 Kestrel category；外连识别覆盖 `System.Net.Http`、
`Microsoft.Extensions.Http` 及已脱敏消息中的 HTTP endpoint。无法确认方向的普通业务日志
保持 `none`，不会被错误归入 HTTP。

筛选仅作用于浏览器当前最新 500 条日志，不改变 WebSocket 协议、服务端文件日志、日志级别
或脱敏边界。

## 验收

- `npm run web:test`：21/21 通过；
- TypeScript strict build 通过；
- 测试分别断言外连、入站、非 HTTP 分类及“排除 HTTP”组合筛选。
