# AI Debug 完整链路验收（2026-08-13）

## 范围

- 默认关闭的 `ai_debug_mode` 配置、部署锁、私有覆盖和 WebUI 开关。
- `data_path/ai-debug` 独立持久化；run_id 只用于 SHA-256 文件定位，不直接成为文件名。
- AI 前置确定性尝试、任务输入、发布时间证据、Prompt 模板、最终渲染 Prompt、AI/MCP 完整 Body、候选、用量和 TMDB 本地验证。
- AI 调用日志的 `debug_available`、读取 API、删除 API与分阶段 WebUI。
- 不捕获 Authorization Header、API Key、Cookie、passkey、Torrent URL、announce 或下载器凭据。

## 自动验收

```powershell
dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~OpenAiCompatibleMetadataMatcherTests|FullyQualifiedName~AiMetadataTaskResolverTests|FullyQualifiedName~AiMetadataDebugTraceStoreTests|FullyQualifiedName~MetadataAttemptApiTests|FullyQualifiedName~DetailedLogWindowTests|FullyQualifiedName~AiDeploymentConfigurationTests"
npm run web:test
```

结果：最终 .NET 定向测试 65/65 通过；完整 `AnimeGoNet.App.Tests` 1029/1029 通过；WebUI 测试 20/20 通过。覆盖 Debug 开关、开关关闭零捕获、模板/渲染 Prompt、AI/MCP request/response、前置链路模型、哈希文件名、读删 API、日志标记、页面安全 DOM 和可访问性。

独立临时 JIT 实例的真实浏览器检查确认：配置编辑器中的开关默认未勾选、可操作、说明完整且实际可见；页面无 console warning/error。临时实例没有连接 TestSpace、真实 AI 或用户 qBittorrent。

`dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 --no-restore -p:PublishAot=true` 成功，确认新增 Debug 文档的 source-generated JSON 和文件存储没有引入 NativeAOT 反射阻断。

## 手工查看

1. 在“设置与备份 / 应用配置 / AI 与 MCP”开启“AI Debug 完整链路”，保存并重启。
2. 触发一条确实进入正式 AI 匹配的任务。
3. 打开“日志 / AI 调用日志”，展开对应条目并点击“查看完整链路”。
4. 依次检查 AI 前置链路、两份 Prompt、AI/MCP 请求链、模型结果与 TMDB 本地验证。
5. 点击“删除本次 Debug 链路”，确认普通调用日志和任务仍保留，而该条不再显示查看按钮。
