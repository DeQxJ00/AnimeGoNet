# Mikan Episode 身份长期缓存验证

日期：2026-08-13

## 行为边界

- Mikan Episode 页面只有同时解析出正整数 `mikanid` 与 `groupid` 才写入缓存。
- 缓存位置固定为 SQLite `bolt/mikan_episode_identity`。默认 TTL 为 8760 小时（1 年），
  可配置为 `0` 永久；RSS 再刷新或应用重启后仍会命中未过期项，用户可在“系统缓存”
  逐条查看和删除。
- key 是不含 userinfo、query 和 fragment 的绝对 HTTP(S) Episode URL。带参数 URL 继续按
  正常网络流程解析，但不长期落盘，避免意外保存 token/passkey。
- value 仅包含 schema 版本、`mikanid` 和 `groupid`；不保存响应 HTML、Cookie、Torrent URL、
  passkey 或请求凭据。
- 页面解析失败、网络失败、缺少任一 ID 均不做 negative cache，下一次刷新仍会重新请求。
- 聚合 RSS 身份解析、legacy Mikan 五级过滤和 AI 匹配测试工具的 Mikan URL 导入共用该缓存。
  SQLite 缓存不可用时仅退化为网络解析，不阻断业务主链。

## 自动化验收

- `MikanEpisodeIdentityCacheTests` 验证首次解析写入、无过期时间、跨新 resolver/cache 实例
  命中、二十年后的读取、系统缓存明文查看、精确删除、失败后恢复、缺 groupid 不缓存，
  以及带查询参数 URL 安全旁路。
- `MikanLegacyFilterProcessorTests.SuccessfulIdentityIsReusedAcrossLaterRssBatches` 验证两次独立
  RSS 批次只抓取一次 Episode 页面，同时不改变既有任务幂等语义。
- 完整解决方案回归实际通过 1694/1694（Plugin Abstractions 13、Core 399、Plugin SDK 16、
  Plugin Tool 23、Data 229、App 1014）；此项不需要真实 Torrent 下载。
- win-x64 NativeAOT publish 已完成原生代码生成；发布二进制以 TestSpace 独立三目录启动，
  `/ping` 返回 `pong`、`native_aot=true`、`runtime_identifier=win-x64`，静态 WebUI 返回 200。
