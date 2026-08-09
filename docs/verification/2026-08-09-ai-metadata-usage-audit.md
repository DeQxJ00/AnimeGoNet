# AI 元数据用量审计（2026-08-09）

## 范围

- `OpenAiCompatibleMetadataMatcher` 汇总一次任务级 AI 语义尝试中的全部
  Chat Completions 响应；MCP 工具往返形成的多轮请求累加
  `prompt_tokens`、`completion_tokens` 与 `total_tokens`。
- `request_count` 统计真实 HTTP 尝试，因此 429/5xx 重试也计数；
  `tool_call_count` 统计 provider 要求主程序执行的 function calls。
- schema v40 只把用量附在实际承载 AI 调用的单条
  `metadata_resolution_attempts` 记录上，避免 Series/Season 双审计重复计算。
- 任务详情和策略时间线 API/WebUI 显示模型、token、请求数和工具调用数；
  provider 未返回 token 时保留模型与请求计数，并明确显示 token 不可用。

## 安全边界

用量审计不保存 API key、Authorization、Prompt、模型输出、MCP 参数/正文、
Torrent URL、passkey、Cookie 或绝对路径。模型名限制为 1～256 个可打印字符，
所有计数必须非负。

## 自动验证

- matcher fake-server 两轮工具调用验证 token 累加、provider 模型、HTTP 数和
  tool call 数；限流和超时仍保留稳定安全分类。
- resolver 验证用量与已验证 TMDB 结果一起向编排层传播。
- SQLite 测试验证 schema v40、重启后 attempt 查询和任务详情投影。
- API 测试验证时间线与任务详情字段，同时继续断言 Torrent/passkey 不回显。
- `npm run web:check` 与 `npm run web:build` 验证静态 TypeScript 页面。

真实模型请求只在显式 Mikan LocalIntegration/远端验收中启用；密钥仅通过进程
环境或测试机私有配置提供，不进入仓库。
