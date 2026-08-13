# 本机配置敏感值直显验收

日期：2026-08-13

## 范围

- 应用配置：TMDB API Key、TMDB Read Access Token、AI API Key 及保存前差异直接显示。
- 下载器与来源：qBittorrent 用户名/密码、Mikan Cookie、Mikan RSS URL 直接回填。
- AI 匹配测试工具：从主程序当前配置回填 Base URL、API Key、模型、API 模式、Reasoning、全局代理和 MCP/AniDB 地址；旧浏览器 localStorage 只恢复任务输入，不再覆盖连接配置。
- 手动导入：Torrent URL 与 RSS URL 使用普通 URL 输入框，录入时可见。
- 外部 C# 插件：schema 标记 `writeOnly` 的 vars 与其 schema 注解在配置 API/WebUI 直接回填；显式清除仍通过 `clear_write_only_paths`，勾选清除时不会把已回填值同时提交。

## 保留的脱敏边界

- `/api/v1/status`、实时/滚动日志、插件 stderr、任务审计、AI 请求/工具轨迹和错误消息仍不得包含 API Key、Cookie、password、passkey 或完整私密 URL。
- Mikan 页面导入结果不回传带 passkey 的 Torrent URL；它属于运行输入证据，不是可编辑配置。
- 配置文件、备份、TestSpace、浏览器数据和凭据继续由 `.gitignore` 与部署目录权限保护，不提交 Git。

## 自动验收

- `AiMetadataTestApiTests.ExposesValidatedTesterPromptAndConfiguredBootstrap` 验证 AI Tester bootstrap 回填当前 API Key 和模型。
- `ConfigurationApiTests.PreviewValidatesAndReturnsVisibleEffectAwareDiffWithoutWriting` 验证明文密钥差异且预览零写入。
- `ExternalPluginConfigurationApiTests.SaveReturnsWriteOnlyValueToConfigurationApiButNotRuntimeStatus` 验证配置 API 可见、运行状态仍不泄漏。
- `ExternalPluginConfigurationServiceTests.EditableViewReturnsAndSafeSaveRetainsWriteOnlyVars` 验证回填后省略更新仍保留原值。
- `StaticWebUiTests` 验证 AI Key、Torrent/RSS URL 的直显表单契约。
- 2026-08-13 定向 Release 测试：186 passed，0 failed。
