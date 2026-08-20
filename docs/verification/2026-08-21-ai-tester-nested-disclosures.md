# AI 匹配测试工具审计折叠与来源状态验收

日期：2026-08-21

## 范围

- “发送给 AI API 的请求”按请求轮次折叠；每轮内部的完整请求 Content 可独立展开/收起。
- “工具调用顺序与完整 Content”按工具调用折叠；每次调用内部的请求 Content、返回 Content 可分别展开/收起。
- TMDB MCP、Mikan/BGM 与 AniDB 来源状态随表单即时更新；启用为绿色，关闭为中性灰色。

## 自动验收

- `npm run web:check`：通过。
- `npm run web:test`：36/36 通过；覆盖两层 `details/summary`、可点击摘要、来源状态类与绿色/灰色样式契约。
- `dotnet build AnimeGoNet.slnx -c Release --no-restore`：通过，0 warning、0 error。
- `dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj -c Release --no-build --filter "DisplayName~20260821-ai-tester-nested-disclosures"`：1/1 通过，确认发布静态资源版本一致。

## 浏览器验收

本机主程序使用 TestSpace 配置运行在 `http://127.0.0.1:6180/#/tools/ai-metadata`：

1. 初始已启用的 TMDB MCP 显示 `TMDB MCP 已启用` 和 `enabled` 状态；已启用的 BGM MCP 显示 `BGM MCP 已启用` 和 `enabled` 状态。
2. 关闭 TMDB MCP 后即时显示 `TMDB MCP 已关闭` 和 `disabled` 状态。
3. 开启 AniDB Lookup 后即时显示 `AniDB 已启用 · 待填写 ID` 和 `enabled` 状态。
4. 已将 TMDB/AniDB 开关恢复为验收前状态；未运行模型、未发出外部请求、未创建或修改业务任务。
5. 受控 loopback smoke 曾生成 1 个 AI 请求轮次与 1 个内部 Content 折叠项，并实际展开两层；目标为 `127.0.0.1:1`，未向外部模型发送数据。

