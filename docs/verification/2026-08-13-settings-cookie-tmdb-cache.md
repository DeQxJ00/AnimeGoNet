# 设置菜单、Mikan Cookie 与 TMDB 缓存默认值验证

日期：2026-08-13

## 范围

- 一级菜单“连接与配置”改名为“设置与备份”；内部稳定路由仍为 `connections`，既有 hash 不失效。
- “Mikan 手动设置 / 导入任务”增加“管理来源与 Cookie”，跳转到“设置与备份 / 输入源”并选中当前 Mikan SourceProfile。
- Mikan 登录 Cookie 继续使用唯一的 SourceProfile 字段与 revision 保存，配置页直接回填已保存值，不复制第二份设置。
- TMDB Search/Series/Season/Episode 成功响应缓存的新部署默认 TTL 从 336 小时调整为 144 小时；已有显式覆盖和旧 YAML 迁移值不被改写。

## Cover 链路

- 动画库只从已验证 TMDB Series/Season 投影取得相对 `poster_path`。
- 浏览器只请求同源 `/api/v1/library/covers/{series}/{season}`；后端优先 Season poster、再回退 Series poster。
- 后端以可配置 `metadata.tmdb.image_base_url` 拼接固定 `w500` 路径，按全局域名代理策略下载，并缓存到 `data_path/cache/covers`。
- 当前本机显式图片地址为 `http://image.tmdb.local/t/p/`，因此实际上游形如 `http://image.tmdb.local/t/p/w500/{poster}`；当前缓存目录已有 25 个文件。图片不是 qBittorrent 下载内容，也不会写入媒体库目录。

## 自动验证

- TypeScript `web:check` 与确定性 `web:build`：通过。
- `AnimeGoDefaultsTests`：4/4。
- 配置锁、导航、来源配置和静态 WebUI：192/192。
- 前端 Node 测试：19/19。
- win-x64 NativeAOT 发布：通过，输出到 `artifacts/mybangumi-win-x64-native-v46-settings`（Git 忽略）。
- 本机 NativeAOT WebUI 浏览器验收：快捷入口跳到 `#/connections/sources`，工作区标题为“设置与备份”，Mikan Cookie 输入框可见。当前 `mikan` 来源状态为“未配置”；RSS URL token 不会被误当作登录 Cookie。
