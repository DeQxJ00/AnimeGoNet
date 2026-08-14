# Mikan 来源级身份 Cookie（2026-07-30）

## 行为边界

- 对齐上游 `advanced.source.mikan.cookie` 与 Cookie 名
  `.AspNetCore.Identity.Application`，同时兼容旧
  `advanced.anidata.mikan.cookie`。
- 凭据属于单个 `SourceProfile`。默认 `mikan` 与自定义
  `adapter: mikan` 来源相互隔离，不读取或复用其他来源的值。
- WebUI 的输入契约是只填写 `.AspNetCore.Identity.Application=` 后面的纯
  Cookie value，不填写 Cookie 名、分号或整段 `Cookie` Header。服务端继续兼容
  旧配置中的完整 `.AspNetCore.Identity.Application=<value>` 格式，入 SQLite
  前仍统一只保存 value。
- 仅允许 RFC 6265 cookie-octet 子集，最大 8 KiB；分号、空白、引号和
  CR/LF 注入在启动、API 与数据层边界拒绝，诊断不包含输入值。
- RSS 与 Torrent staging 只在当前请求 Host 等于原始 URL Host 时添加
  Cookie；允许的跨 Host redirect 会继续执行 SSRF/Host 白名单校验，但
  必定剥离凭据。
- REST API 和静态 WebUI 仅返回
  `mikan_identity_cookie_configured`。值只写、可显式清除；省略字段保留原值，
  `ToString()`/错误响应不回显。

## 数据与 UI

- SQLite schema v31 为 `source_profiles` 增加可空
  `mikan_identity_cookie`，迁移保留现有 profile revision 和业务字段。
- YAML、扁平环境变量 `ANIMEGO_MIKAN_COOKIE`、SourceProfile seed、CRUD、
  RSS 插件、统一导入 Torrent staging 使用同一模型。
- 输入源页面仅在 Mikan adapter 下启用 Cookie 输入框，明确提示只填
  `.AspNetCore.Identity.Application=` 后面的内容，直接回填已保存的纯 value，
  并支持独立“明确清除 Mikan Cookie”操作。

## 验证

- `dotnet build AnimeGoNet.slnx -c Release --no-restore`：
  0 warning / 0 error。
- 聚焦测试：Core 8/8、Data 7/7、最终配置/RSS App 24/24；完整 App
  522/522。覆盖规范化、注入拒绝、旧 YAML
  迁移、schema 30→31、只写 CRUD、保留/清除、RSS 自定义 profile、Torrent
  redirect、真实 loopback 精确 Cookie header。
- `ANIMEGO_UPSTREAM_REPO=E:\WorkSpaceAI\AnimeGoNet\AnimeGo dotnet test
  AnimeGoNet.slnx -c Release --no-restore`：977/977 通过。
- `npm run web:build` 与 `npm run web:check`：通过，生成 JavaScript 与
  TypeScript 源一致。
- `dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release
  -r win-x64 --self-contained true -p:PublishAot=true --no-restore
  -o artifacts/mikan-cookie-win-x64`：通过，无警告。
- `eng/smoke-native.ps1`：原生进程验证 schema 31、SQLite、WebUI、WebSocket、
  SourceProfile 凭据 configured 状态和响应不含测试值；smoke 后进程与监听端口
  均为 0。

测试只使用合成 Cookie，未读取或提交用户 Cookie、WebUI 凭据或 passkey。
