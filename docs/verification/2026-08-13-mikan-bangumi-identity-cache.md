# Mikan → Bangumi 身份缓存与可配置 TTL 验证

日期：2026-08-13

## 实现边界

- 主程序先从 Episode 页面取得 `mikanid/groupid`，再用 `/Home/Bangumi/{mikanid}` 解析
  `bgmid`。只有成功取得正整数 bgmid 才写入 SQLite `bolt/mikan_bangumi_identity`；key
  为十进制 mikanid，value 仅含 schema 版本、mikanid 和 bgmid。
- 新 RSS 批次和 AI 匹配测试工具都会先读取该缓存。命中时不请求 Mikan 作品页；失败、
  页面缺链接或网络异常均不缓存，下一次仍可恢复。
- Episode 身份缓存和 Mikan→Bangumi 映射缓存分别由
  `metadata.mikan.episode_identity_cache_hours`、
  `metadata.mikan.bangumi_identity_cache_hours` 控制。默认均为 8760 小时（1 年），范围
  0–87600 小时；0 表示永久。WebUI 应用配置、部署 YAML、环境变量和命令行均已接入，
  部署值会锁定对应表单字段。
- 两个 bucket 均通过“系统缓存”显示完整明文 JSON、更新时间与过期时间，并支持精确删除。
  缓存不保存作品页 HTML、Cookie、RSS/Torrent URL、passkey 或其它凭据。

## 自动化验收

- `MikanBangumiIdentityCacheTests`：成功写入、跨 resolver 命中、一年 TTL、到期回源更新、
  失败不缓存、0 小时永久、系统缓存查看和精确删除。
- `MikanEpisodeIdentityCacheTests`：可配置 TTL 到期回源，以及 0 小时永久模式。
- `AiMetadataTestApiTests`：连续两次导入相同 Episode URL 时，Episode 页面和作品页都只
  请求一次。
- `ConfigurationApiTests`、`DeploymentConfigurationLocksTests`、
  `DeploymentYamlConfigurationTests` 与静态 WebUI/DOM 测试覆盖默认值、保存、锁定和表单。

最终完整解决方案回归通过 1706/1706（Plugin Abstractions 13、Core 400、Plugin SDK 16、
Plugin Tool 23、Data 229、App 1025）；WebUI TypeScript 严格构建和 20/20 Node DOM/状态测试通过。
win-x64 NativeAOT 完成原生代码生成；最终产物以 TestSpace 三目录运行在 6180，
`/ping=pong`、`native_aot=true`、WebUI=200，实际配置 API 的生效值和可编辑值均返回
Episode=8760、Mikan→Bangumi=8760 小时。
本地浏览器刷新后的“运行配置”卡片显示两项 8760 小时；“编辑覆盖”弹窗直接回填两项
8760，并明确标注“填 0 表示永久缓存”。
