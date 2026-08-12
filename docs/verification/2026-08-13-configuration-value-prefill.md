# 配置值回填（2026-08-13）

WebUI 配置编辑改为本地易用优先。受统一 API 鉴权保护的配置响应返回并回填：

- qBittorrent 用户名、密码、Base URL 与下载目录；
- Mikan SourceProfile 的登录 Cookie、RSS URL 和其他来源字段；
- TMDB API Key、TMDB Read Token、AI API Key，以及原本已回填的 Mikan/TMDB/Bangumi/AI/MCP 地址。

凭据输入未修改时前端提交 `null`，继续沿用原有“保留现值”语义；显式清除开关保持不变。应用自身的 Web API Access Key 不回传。任务、日志、业务 SQLite、测试报告和 Git 仍不得复制这些凭据。

验收覆盖 API 返回值、清除/保留语义、TypeScript 静态构建和浏览器中的实际表单回填。
