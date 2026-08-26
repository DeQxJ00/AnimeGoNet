# AniDB title cache verification

- 一级“系统缓存”已改名为“缓存”，二级菜单为“Bangumi缓存 / AniDB缓存 / 其他缓存管理”；原 Bangumi 版本、回滚和命中审计功能迁移后保持原接口与数据。
- schema v68 新增单例更新状态与 `anidb_titles` 明文索引；原始压缩包固定保存在 `data_path/cache/anidb/anime-titles.xml.gz`。
- AniDB 更新使用流式 gzip/XML 读取和 SQLite 事务。下载、格式或导入失败时事务回滚，上一版标题与原始压缩包均保留。
- 后台 Worker 首次延迟一分钟，随后每小时检查一次是否达到 24 小时更新期限；HTTP ETag/Last-Modified 可用于未修改响应。WebUI 可显式强制刷新。
- 数据层测试覆盖相邻标题、AID/规范化标题查询与损坏包回滚；应用层测试覆盖 HTTP 下载、导入和原始包落盘；WebUI TypeScript 和静态契约测试通过。
