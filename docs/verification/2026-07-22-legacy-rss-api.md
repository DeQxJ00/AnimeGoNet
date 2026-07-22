# 上游兼容 `/api/rss`（2026-07-22）

## 合同

按 `wetor/AnimeGo@develop internal/web/api/plugin.go` 和 `SelectEpRequest` 保留：

- `POST /api/rss` JSON：`source`、`rss.url`、`is_select_ep`、`ep_links`。
- `is_select_ep=true` 时按 RSS entry 的 Mikan Episode URL 做 ordinal 精确匹配，不按标题猜测。
- 成功 HTTP 200、body `code=200`、`msg=开始处理N个下载项`；业务失败仍 HTTP 200、body `code=300`。
- 响应扩展返回 batch/decision/task 状态；Mikan 作品 ID 字段显式固定为 `mikanid`。

入口只适配 HTTP：URL → `RssFeedReader` → `MikanRssIngestProcessor`，没有复制规则、去重、staging 或下载器逻辑。

## 安全获取

`ProfileBoundRssFeedHttpClient` 使用启用 Mikan SourceProfile 的 host allowlist。每个初始 URL和重定向目标都拒绝 userinfo/fragment、未授权 host 和 HTTPS 降级；每跳重新解析 DNS，任一地址为 loopback/private/link-local/documentation/multicast 即拒绝；实际连接复用固定已验证 IP 的 transport，防止 DNS rebinding。响应头和实际流均限制为 5 MiB，最多 5 次重定向，错误不回显带凭据 URL。

## 验收

- `dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj --no-restore --verbosity minimal`：132/132 通过。
- Kestrel 合同：两项 RSS + 精确 ep_links 只处理一项，返回 legacy 成功消息、`mikanid=3951` 和 staged task。
- 安全重定向：Mikan HTTPS 重定向到 loopback HTTP 返回 `code=300/rss_redirect_rejected`，transport 调用数保持 1。
- `dotnet test AnimeGoNet.slnx -c Release --no-restore --verbosity minimal`：Core 137、Data 57、App 132，共 326/326 通过。
- `dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 -p:PublishAot=true --no-restore -o artifacts/legacy-rss-api-win-x64`：NativeAOT 发布通过，无裁剪警告。

尚未完成的是上游 `mikan_tool.py Filiter0..4` 与 `/api/plugin/config` 的兼容配置读写；当前新批次黑白名单/有序优选已真实执行，但不能据此宣称旧 AnimeGoHelper 过滤配置完全兼容。
