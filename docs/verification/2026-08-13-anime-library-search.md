# 动画作品库搜索验收（2026-08-13）

## 范围

- `GET /api/v1/library/seasons?search=...` 在 SQLite 分页前筛选全部正式作品库季度。
- 支持 TMDB 规范名称、原名、季度名的大小写不敏感包含匹配，以及精确 TMDB Series ID。
- `%`、`_` 和反斜杠按普通字符处理，不允许调用方把搜索框变成 SQL 通配查询。
- WebUI 提供搜索、清除和无结果状态，并保存搜索词；新查询总是回到第一页并关闭旧详情。
- 搜索最多 200 个字符且拒绝控制字符，API 返回稳定错误 `library_search_invalid`。

## 自动验收

- `AnimeGoNet.Data.Tests`：名称、原名、季度名、精确 ID、通配符转义和非法输入。
- `AnimeGoNet.App.Tests`：HTTP 查询、稳定错误和静态 HTML/CSS/JavaScript 契约。
- TypeScript 严格构建及 WebUI DOM/可访问性测试。
