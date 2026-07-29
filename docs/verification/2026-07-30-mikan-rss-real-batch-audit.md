# Mikan RSS 真实批次优选复核

日期：2026-07-30

## 结论

- 旧 `POST /api/rss` 与现代 `POST /api/v1/rss/ingest` 均把解析后的 feed 交给同一个 `MikanRssIngestProcessor`。
- 处理顺序为上游兼容的 `Filiter0`～`Filiter4`，再执行本功能的具名黑白名单和同集有序优选；legacy filter 拒绝或身份解析失败的候选不会进入后续优选与 Torrent staging。
- 优选开启时，具名黑名单先于白名单执行；只有可靠且相同的 `mikanid + 来源 Episode kind + 来源 Episode` 多候选组才运行有序规则组。剩一个候选立即短路，最终并列按 RSS 原顺序稳定选择。
- `mikan_rss_priority_enabled=false` 是整套“同集批次优选”功能的总开关：真实批次中的 legacy-filter 合格候选全部记录为 `Winner / SkippedByConfiguration`，不会执行本功能的黑白名单或优先级组，规则内容不被删除。
- 批次 SQLite 记录保存规则 revision、开关快照、逐候选决策、实际执行组、legacy filter revision/决策、winner lease 与最终 ingest task 关联；Torrent URL 只保存 SHA-256 指纹。

## 验收

- `MikanRssRuleEngineTests`：黑名单优先、白名单、小写匹配、单候选旁路、多候选逐组短路和稳定 RSS 顺序。
- `MikanRssBatchPlannerTests`：来源 Episode 分组、小数/特别篇隔离、优选禁用、legacy filter 前置资格。
- `MikanRssIngestProcessorTests`：真实处理器只 staging winner、重复批次幂等、禁用优选时全部合格候选 staging，并读取 SQLite 验证开关、决策、空执行组和任务关联。
- `MikanRssBatchStoreTests` 与 `MikanRssRuleStoreTests`：完整决策审计、租约、规则 CRUD、revision、快照和回滚。
- RSS 定向测试：Core 13、Data 11、App 13，共 37/37 通过。
- 全解决方案：893/893 通过，0 失败、0 跳过。
- `win-x64` NativeAOT 发布及 `eng/smoke-native.ps1` 通过，覆盖真实二进制启动、API、SQLite、静态 WebUI 与 WebSocket。
