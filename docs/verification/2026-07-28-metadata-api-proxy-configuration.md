# TMDB / Bangumi API 地址与代理配置（2026-07-28）

> 已被 2026-08-09 的唯一全局选择性代理模型取代。本文件只记录当时实现，不再描述当前配置契约；当前程序已删除 TMDB/Bangumi 独立代理字段。

## 行为

- TMDB 与 Bangumi 各自拥有强类型 `BaseUrl`、可空 `ProxyUrl` 和 HTTP timeout。
- Base URL 支持反向代理路径前缀并要求以 `/` 结尾；代理支持无凭据 `http://`、`https://`、`socks5://` origin。
- `tmdb_*`、`bangumi_*` 部署键、`application.private.json`、`GET/PUT /api/v1/config` 和静态 WebUI 使用同一模型。
- 未配置代理时 `HttpClientHandler.UseProxy=false`，不会意外继承宿主系统代理；配置代理时 TMDB/Bangumi 各自创建独立 handler。
- 私密配置仍保持 format v1；新增字段为向后兼容可选字段，旧文件缺失时继承部署值。

## 验收

- Core 12/12 配置定向测试通过：默认值、路径前缀、HTTP/SOCKS5 代理、凭据/路径/query 拒绝和独立超时。
- App 30/30 定向测试通过：TMDB/Bangumi fake HTTP、自定义 Base URL、超时、handler 代理、部署绑定、私密覆盖（含旧 v1 文件继承新字段）、API 脱敏和 WebUI 字段。
- TypeScript strict/build 与生成 JavaScript 语法检查通过。
- 显式 LocalIntegration 使用进程环境中的测试 key，从官方 TMDB 只读取得 Series `72517`，1/1 通过；key 未写入仓库或输出。
- 全量 566/566 通过（Core 205、Data 99、App 262）。
- `dotnet publish ... -r win-x64 -p:PublishAot=true --no-restore` 生成原生代码，published-binary smoke 返回 `native_aot=true` 并完成进程/临时目录清理。
