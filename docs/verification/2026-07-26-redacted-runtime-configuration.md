# 脱敏运行配置验收

日期：2026-07-26

## 范围

- 新增 `GET /api/v1/config`，返回当前进程实际生效的强类型配置，而不是配置文件原文。
- 路径包含 `data_path`、`download_path`、`save_path`，并明确修改需要重启。
- 部署状态包含容器模式、后台 worker 和 Access-Key 是否启用。
- 元数据包含 TMDB 安全端点/语言/超时、四级季度失败链开关、AI 季度与 EP 独立开关、AI 超时、Bangumi 完全兜底和可信 offset 缓存开关。
- Torrent 抓取包含超时、最大响应、最大跳转和暂存 TTL。

## 安全边界

- TMDB API key、TMDB read access token、Access-Key 只投影为 `*_configured` 布尔值。
- `/api/v1/config` 继续受统一 API 鉴权中间件保护。
- DTO 加入 source-generated JSON context，NativeAOT 不依赖反射。
- WebUI 只使用 `createElement`、`textContent` 和 `replaceChildren`，不使用 `innerHTML`。

## 自动验收

- `ConfigurationApiTests.EffectiveConfigurationIsTypedAndNeverReturnsCredentials` 验证未授权 401、直接 Access-Key 鉴权、全部配置字段以及三个明文 secret 均不出现在响应中。
- `ConfigurationApiTests.StaticWebUiLoadsRedactedConfigurationPanel` 验证静态 HTML/JS 发布资源接入配置端点且不存在 `innerHTML`。
- `npm run web:build` 通过。
- 解决方案全量 430 passed（Core 169、Data 69、App 192）。
- `win-x64` NativeAOT 发布成功；隔离产物的 `/api/v1/config` 返回 200、AI timeout 600、background workers false。
