# 一级日志工作区与 AI 调用审计验收

日期：2026-08-13

## 范围

- 将运行日志从“任务中心”迁入独立一级“日志 / 运行日志”。
- 增加级别、关键词、category、Event ID、本地时间范围、仅异常及 AI/TMDB/匹配/
  下载/Mikan RSS/整理/系统快速分类；单条继续展开结构化字段和脱敏原文。
- 新增 `GET /api/v1/logs/ai-invocations` 和“日志 / AI 调用日志”。只列出
  `ai_model` 非空的实际 provider 调用，跨任务按搜索、阶段、结果、模型和时间分页；汇总
  当前全部筛选结果的成功/失败、Token、HTTP 请求和工具调用。
- 单条 AI 审计提供任务、Run/Attempt、来源作品 ID、最终 TMDB Series/Season、耗时、
  稳定错误码和安全原因；不持久化或返回 Prompt、工具正文、原始响应和凭据。

## 自动验证

- Web TypeScript/Node：20/20，通过解析、业务域分类、时间与异常组合筛选，以及既有
  DOM/XSS/accessibility 契约。
- App 定向测试：11/11，通过一级/二级导航、静态日志控件、WebSocket 与 AI 日志 API；
  API 用真实临时 SQLite 验证筛选、分页汇总、空结果、稳定参数错误和 Torrent secret
  不可见。
- 全仓 .NET 回归：1689/1689（Core 399、Data 229、App 1009、插件相关 52），0 失败。
- `dotnet build src/AnimeGoNet.App/AnimeGoNet.App.csproj --no-restore`：0 warning / 0 error。
- win-x64 NativeAOT Release publish：成功生成本机二进制。

## 浏览器验证

使用上述 NativeAOT 二进制及隔离 TestSpace 数据目录启动 `127.0.0.1:6180`：

- 一级“日志”及“运行日志 / AI 调用日志”二级菜单可见，hash 为
  `#/logs/runtime` 与 `#/logs/ai-invocations`。
- 运行日志 WebSocket 已连接，统计、快速分类、时间/异常筛选和可展开行正常显示。
- TestSpace 现有持久审计读取 4 条 AI 调用，生产结果为 1 条 `matched` 与 3 条 `error`；
  汇总准确显示 1 / 3，`error` 筛选返回 3 条，`matched` 筛选返回 1 条，页面无第二套
  推测性计数。
- 画面宽屏布局无水平溢出；运行日志和 AI 调用列表均使用安全 DOM 文本渲染。

本次未发出新的真实 AI、TMDB、Mikan 或 qBittorrent 请求，也未修改 TestSpace 业务数据。
