# 动画库最后 EP 变动排序验证

## 已确认语义

- 动画库新增 `episode_changed_at` 升序/降序排序，WebUI 名称为“最后 EP 变动时间”。
- 值取当前 TMDB Series + Season 下规范完成记录 `completed_at_utc` 的最大值。
- TMDB/Bangumi 缓存刷新、作品资料修改和匹配任务更新时间不会改变该值。
- 删除完成记录后投影按剩余完成记录重新计算；没有完成记录的季度始终置后。
- 动画电影没有 Episode，不提供此排序项；原默认“最后更新时间降序”保持不变。

## 验收结果

- Data：14 项 `AnimeLibraryStoreTests` 通过，覆盖双向排序、最新记录取值、空值置后与稳定 tie-breaker。
- API/WebUI：19 项动画库 API 专项测试、44 项静态 WebUI 契约测试通过；返回 `last_episode_changed_at_utc`，页面排序项和详情时间均可见。
- 全解决方案：1890 项 .NET 测试通过；`git diff --check` 通过。
