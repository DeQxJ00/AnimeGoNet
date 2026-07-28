# 待补全 TMDB NFO 可恢复重写（2026-07-28）

## 设计

- schema v21 新增 `pending_tmdb_nfo_rewrite_jobs`。
- 人工恢复事务只接受原下载任务已捕获的 `save_root_path`；找不到可信根目录时整个恢复回滚。
- 作业唯一键为 bgmid、真实 TMDB Series、save root 和原兜底作品目录，分批恢复不会重复建作业。
- worker 使用 `pending/failed → writing → completed` 状态机、五分钟租约、attempt 和 next-attempt。
- 过期 `writing` 租约自动恢复为可重试失败；普通写入失败三十秒后重试。
- `TvShowNfoWriter` 在原兜底作品目录内原子替换 `tvshow.nfo`，标题和 TMDB ID 使用已验证的正式值，同时保留对应 bgmid。
- 不移动、重命名或删除媒体文件，也不从响应、日志暴露路径。

## 测试

- 数据层：首次 claim、排他租约、失败延迟、重试 attempt、过期租约恢复、完成后不再领取。
- 恢复事务：两条 fallback 共享同一根目录时只入队一个作业，且不创建新下载。
- 应用层：真实临时文件从 `<tmdbid>0</tmdbid>` 重写为 TMDB 700，仍位于原 `Fallback Anime` 目录；不会误建 `Canonical Anime` 目录。
- 文件系统失败持久化为 `nfo_rewrite_failed`，作业不误报完成。
- API：人工恢复成功后数据库中存在 pending NFO 作业。

全解决方案 542/542 通过（Core 199、Data 91、App 252）；win-x64 NativeAOT 发布通过。
