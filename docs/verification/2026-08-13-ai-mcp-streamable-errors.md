# AI / MCP Streamable HTTP 与错误分类验收（2026-08-13）

## 修复范围

- 本地 TMDB/Bangumi MCP 的 `tools/call` 若返回 HTTP 202 空正文，客户端使用同一
  `Mcp-Session-Id` 打开 GET SSE，读取与原 JSON-RPC request id 相同的结果。
- JSON 同步响应和 202 + SSE 异步响应均保留；SSE envelope 有 8 MiB 上限、取消和
  request id 校验，并进入 AI Debug 请求链。
- MCP 传输或协议错误直接终止本次 AI 尝试，不再把 `tool_protocol_error` 文本交给模型后
  误记成普通“未匹配”。
- Prompt 未修改。

## 稳定错误分类

| 场景 | 稳定错误码 |
| --- | --- |
| AI provider 未配置 | `ai_provider_not_configured` |
| AI DNS / 连接 / 其他网络失败 | `ai_dns_error` / `ai_connection_error` / `ai_network_error` |
| AI HTTP 超时、鉴权、限流、服务端错误、请求拒绝 | `ai_http_timeout` / `ai_authentication_failed` / `ai_rate_limited` / `ai_remote_service_error` / `ai_http_rejected` |
| TMDB MCP DNS / 连接 / 其他网络失败 | `ai_tmdb_mcp_dns_error` / `ai_tmdb_mcp_connection_error` / `ai_tmdb_mcp_network_error` |
| Bangumi MCP DNS / 连接 / 其他网络失败 | `ai_bangumi_mcp_dns_error` / `ai_bangumi_mcp_connection_error` / `ai_bangumi_mcp_network_error` |
| MCP 超时 / SSE / JSON-RPC协议失败 | `ai_{source}_mcp_timeout` / `ai_{source}_mcp_sse_error` / `ai_{source}_mcp_protocol_error` |
| MCP HTTP 鉴权、限流、5xx、其他拒绝 | `ai_{source}_mcp_authentication_failed` / `ai_{source}_mcp_rate_limited` / `ai_{source}_mcp_service_error` / `ai_{source}_mcp_http_rejected` |
| TMDB MCP 工具返回 `isError=true`，且没有成功工具结果 | `ai_tmdb_mcp_tool_error` |
| 模型返回最终结果但未使用必需的 TMDB MCP | `ai_tmdb_mcp_not_used` |

`ai_file_identity_mismatch`、TMDB Series/Season/Episode 本地验证失败等仍属于模型输出或
业务候选验证失败，不与 MCP 连接/工具故障合并。

## 自动验证

- AI/MCP 定向测试：40/40 通过。
- `AnimeGoNet.App.Tests`：1044/1044 通过。
- 其余解决方案测试：681 项通过。
- win-x64 NativeAOT 发布成功。

## 本机隔离真实回放

使用 `TestSpace/ai-debug-replay-20260813-1715` 的 SQLite 与媒体硬链接副本，在 6181
启动新 NativeAOT；不修改 6180 主实例、不新增 qB 任务、不访问私人 Torrent。

三条历史 AI 样本均完成真实 OpenAI-compatible + TMDB/Bangumi MCP 链路：

1. Re:0：P3 先锁定 TMDB 65942/S01，9 次 MCP `tools/call` 均经 202 + SSE 返回，
   最终通过主程序验证并写入 S01E78；41,495 tokens。
2. 名侦探柯南：6 次 MCP 202 + SSE 返回，模型给出 TMDB 30983/S01E118，本地验证成功；
   因隔离库已有相同完成记录，最终按 `episode_already_completed` 标记 duplicate；
   73,505 tokens。
3. Re:0 重复样本：8 次 MCP 202 + SSE 返回；模型把任务标题当作文件名并漏掉真实扩展名，
   被本地边界明确拒绝为 `ai_file_identity_mismatch`，不是 MCP 或“匹配失败”；
   74,071 tokens。

三份新 Debug 文件均包含 `tools/call` 202 与对应 `tools/call/sse` 200，未再出现旧的
`tool_protocol_error`。

## 清空隔离记录后的再次回放

经用户许可，只清除 `TestSpace/ai-debug-replay-20260813-1715` 隔离库内这三条样本的
任务结果、metadata run/attempt、claim、completion 与旧 Debug 文件；6180 主实例、正式
数据和真实下载任务均未改动。为避免测试 RSS 调度抢占 SQLite 写锁，再次回放前仅在隔离库
关闭全部 SourceProfile RSS schedule，随后逐条走主程序原生链路。

| 样本 | 模型最终候选 | 主程序最终结果 | Token | AI HTTP | MCP tools/call | MCP SSE |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| Re:0 样本一 | TMDB 65942 / S01 / E78 | `ai_file_identity_mismatch`，模型把真实 `.mp4` 文件名末尾改写成 `[MP4]`；落入已确认季度 Other | 73,326 | 5 | 6 | 6 |
| Re:0 样本二 | TMDB 65942 / S01 / E78 | 本地 TMDB Series/Season/Episode 与文件身份验证通过，写入 S01E78 | 41,612 | 4 | 8 | 8 |
| 名侦探柯南 | TMDB 30983 / S01 / E118 | 清除既有 completion 后重新验证通过，写入 S01E118 | 51,057 | 6 | 8 | 8 |

本轮合计 165,995 tokens、15 次 AI HTTP、22 次 MCP `tools/call` 和 22 条对应 SSE；
TMDB MCP 与 Bangumi MCP 均未发生连接、协议或工具错误。Prompt 仍未修改。隔离回放完成后
6181/6182 临时实例均已停止，RSS schedule 的关闭也只存在于该隔离数据库。
