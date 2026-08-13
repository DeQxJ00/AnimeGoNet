# 主程序 AI 推理程度配置验收（2026-08-13）

## 行为

- “设置与备份 / AI 与 MCP”在模型旁增加推理程度选择：`none`、`low`、`medium`、
  `high`。
- `none` 在 `AiMatchingOptions` 中保存为 `null`，正式请求不写入 `reasoning`；其余值由
  已有 NativeAOT-safe JSON writer 写为 `reasoning: { "effort": "..." }`。
- API 使用 `ai_reasoning_effort`，私有配置以显式 override + 可空值区分继承与明确
  `none`，保存后重启生效。
- 部署 YAML/命令行支持 `metadata:ai:reasoning_effort` / `ai_reasoning_effort`；部署键存在
  时配置页只读。
- 配置预览显示“AI 推理程度”的变更，不改变 Prompt。

## 验收范围

- 部署值 `high` 可进入运行时 `AiMatchingOptions`，非法值启动失败且只返回安全字段名；
- 私有覆盖可保存/重载 `medium`，也可明确保存 `none`；
- API 保存 `high` 后可回填，后续旧式省略字段的更新保持已保存值；
- 多级部署锁会恢复部署值，WebUI 存在四个固定选项；
- TypeScript strict build与 Web 测试 21/21 通过；配置 API/Store/Deployment/Lock、既有请求
  JSON 等定向测试 122/122 通过。另覆盖预览中的 `none → high / restart` 和 API 非法值拒绝。
- win-x64 NativeAOT 发布成功；使用独立 TestSpace、关闭后台 worker 并以
  `--ai_reasoning_effort=high` 启动真实原生二进制，`metadata.ai.reasoning_effort` 与
  `editable.ai_reasoning_effort` 均返回 `high`，静态页面包含新选择框，验收后进程已停止。
