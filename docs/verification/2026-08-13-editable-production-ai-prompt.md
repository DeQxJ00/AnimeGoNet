# 可编辑生产 AI Prompt 与 WebUI 菜单验收（2026-08-13）

## 范围

- 主程序后台 Worker 与内置“AI 匹配测试工具”默认读取同一份有效生产 Prompt。
- 正式 Prompt 可由部署 YAML/扁平配置或 WebUI 私有覆盖编辑，最大 128 KiB，保存后重启生效。
- 自定义模板必须保留全部输入占位符，以及 `TMDB_MCP`、`BGM_MCP`、`ANIDB_LOOKUP`、`IMDB_LOOKUP`、`BANGUMI_PUBDATE_FIRST` 条件块；运行时仍按任务字段和开关逐块渲染，禁用块不发给模型。
- 配置预览只返回 Prompt 版本、字符数和短 SHA-256，不把完整正文复制到差异或日志。
- 左侧一级菜单新增“下载工具配置”，现有 qBittorrent 管理页面迁入其 `qBittorrent` 二级页；原“测试工具”更名为“AI 匹配测试工具”。
- HTML 使用版本化 `app.js` 请求，避免已有浏览器标签在升级后把新菜单 DOM 与旧路由脚本混用。

## 自动验收

- `AiMetadataPromptRendererTests`：条件块开启/裁剪、占位符替换和非法模板拒绝。
- `OpenAiCompatibleMetadataMatcherTests`：后台匹配器在请求未覆盖 Prompt 时使用主程序配置模板，并在真实 Provider 请求体中留下测试标记。
- `AiMetadataTestApiTests`：Prompt API、Tester bootstrap 与后台配置模板逐字一致；当时版本为 `tmdb-ai-match-v12`，2026-08-13 EP 日期规则最终细化后内置版本升级为 `tmdb-ai-match-v14`。
- `ConfigurationApiTests`：私有覆盖持久化、非法模板拒绝、脱敏 Prompt 预览。
- `DeploymentConfigurationLocksTests`：环境变量/命令行锁定 `ai_prompt_template` 后 WebUI/API 不得覆盖。
- `StaticWebUiTests`、`WorkspaceNavigationTests`：编辑器、恢复按钮、一级菜单名称、workspace/hash 归属和生成静态资产。

2026-08-13 执行 `dotnet test AnimeGoNet.slnx -c Release --no-restore --verbosity minimal`：0 失败，共 1,621 项通过（App 966、Data 217、Core 386、Plugin 52）。相关功能定向回归另为 226/226 通过；TypeScript strict 检查和静态资产重新生成通过。

真实模型调用不属于本次菜单与配置面验收；主程序仍会在模型输出后执行既有 TMDB Series/Season/Episode 二次验证。
