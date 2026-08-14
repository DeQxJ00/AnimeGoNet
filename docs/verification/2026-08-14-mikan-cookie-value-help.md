# Mikan Cookie 纯值输入说明验证

## 用户可见契约

- “输入源 / 输入源管理”的字段名称为“Mikan 登录 Cookie 值”。
- 只填写浏览器 Cookie 中 `.AspNetCore.Identity.Application=` 后面的内容。
  例如 `.AspNetCore.Identity.Application=ABC...` 只输入 `ABC...`。
- 不填写 Cookie 名、分号或整段 `Cookie` Header；占位符显示“只粘贴等号后的内容”。
- 未配置、已回填和部署锁只读状态均保留同一说明，避免动态状态文字覆盖输入契约。
- 服务端继续兼容旧配置中的完整键值格式，规范化后仍只把纯 value 写入 SQLite，
  不影响已有配置。

## 验证

- `npm run web:test`：27/27 通过。
- `dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj -c Release --filter "FullyQualifiedName~SourceProfileApiTests" --no-restore`：22/22 通过。
- `dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -o artifacts/mikan-cookie-help-aot-win-x64`：通过。
- 本机 NativeAOT 实例使用现有 `data_path` 在 `127.0.0.1:6180` 启动；浏览器检查确认
  Mikan 来源的字段名、占位符和已配置状态说明均正确显示。检查未修改或提交 Cookie 值。
