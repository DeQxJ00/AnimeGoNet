# 唯一全局选择性代理（2026-08-09）

## 已确认语义

- 只有一个代理 URL 与一组 host pattern；TMDB/Bangumi/Mikan 地址下不再设置代理。
- pattern 保存为小写，支持精确域名和 `*.example.com`；通配符匹配任意深度子域，但不匹配 apex。
- 未命中域名直连并关闭环境系统代理；命中域名使用显式无凭据 HTTP(S)/SOCKS5 proxy origin。
- 程序尚无正式运行数据，所有者确认直接移除 `tmdb_proxy_url`、`bangumi_proxy_url`、`ANIMEGO_PROXY_URL`，不实现历史迁移。

## 覆盖范围

- Mikan RSS、作品页与 Torrent 暂存；
- TMDB API、Bangumi API、TMDB 海报；
- OpenAI-compatible 模型 endpoint 与 TMDB/Bangumi MCP；
- AnimeGoNetData manifest/package 下载。

qBittorrent 实例连接和编译期固定 AniDB mapping 查询明确直连。Torrent/RSS 每跳仍先执行 SourceProfile host allowlist、DNS 公网地址（或明确配置的 Mikan 私网反代）、redirect 数量与 HTTPS downgrade 检查；匹配 forward proxy 后目标连接由代理解析，因此仅直连分支承诺连接钉在预先校验的 IP。

## 验收

- Core 配置与选择策略：合法 proxy origin、缺 URL 的 host 列表、非法/重复/非小写 pattern、精确/通配/apex/大小写边界。
- App 配置：YAML、扁平/环境/命令行绑定、私有 revision、字段锁、API 投影与 WebUI 单一编辑区域。
- Transport：命中/未命中 `IWebProxy`，真实 loopback forward-proxy 请求行，以及原有 pinned direct socket/cookie/no-redirect 回归。
- 前端：TypeScript 编译、静态 DOM 契约和配置预览字段。

## 本机结果

- `npm run web:test`：17/17 通过。
- `dotnet build AnimeGoNet.slnx --no-restore`：0 warning / 0 error。
- Core 369、Data 212、App 858、Plugin Abstractions 13、Plugin SDK 16、PluginTool 23，共 1491/1491 通过。
- `win-x64` NativeAOT publish 成功，0 AOT/trim warning。
- `eng/smoke-native.ps1` 连续两次在既有 `tracker.invalid` 导入夹具处超过脚本固定的 5 秒 HTTP 时限；发布、启动前置接口均已执行，但不把整套 published-binary smoke 标记为通过。
- Docker 按所有者要求不在本机执行，保持未验证。

真实外部代理不在默认 CI 启动；forward-proxy loopback 测试已验证命中域名时发送 absolute-form target，未命中域名时旁路代理。
