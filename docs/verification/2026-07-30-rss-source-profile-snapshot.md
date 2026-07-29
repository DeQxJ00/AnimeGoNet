# RSS SourceProfile 请求快照

日期：2026-07-30

## 问题

RSS 处理器过去在请求开头、内置 MikanTool filter 和 winner staging 三处分别读取当前 SourceProfile。若管理端在请求处理中更新下载器路由或规则开关，同一个批次可能用旧 revision 完成筛选，却把任务写成新 revision 的下载器与文件策略。

## 实现

- 插件契约增加 AOT-safe 的 `FilterSourceProfileSnapshot`，只包含 revision 和两个 RSS 开关。
- `MikanRssIngestProcessor` 在确认启用的 Mikan profile 后创建一次快照，并把它传入编译期 `mikan-tool` filter。
- `MikanToolFilterPlugin` 有显式快照时使用该 revision 的 `rss_filter_enabled`；管理端独立调用且无快照时仍读取当前 profile。
- `UnifiedIngestProcessor.ProcessRssWinnerAsync` 直接接收请求起点的完整 `SourceProfileRecord`，Torrent Host 策略、adapter、下载器、文件策略、category、tags、做种时长、两个 RSS 开关和 revision 均不再在 staging 前重新查询。

## 验收

- `BuiltInApplicationPluginsTests.FilterPluginHonorsExplicitSourceProfileSnapshot`：数据库当前 profile 已关闭过滤时，显式 revision 1 快照仍按开启状态执行；无快照调用读取当前关闭状态。
- `MikanRssIngestProcessorTests.ConcurrentProfileUpdateOnlyAffectsTheNextRssRequest`：请求在 Bangumi 页面响应处暂停，数据库并发修改下载器、category、两个开关并增加 revision，恢复后任务仍保存 revision 1、原 `bt` 路由、原 category 和原开关；RSS 批次审计也保留原双开关。
- 插件契约、RSS 处理器、来源 CRUD、现代提交和 legacy RSS 回归测试共 43/43 通过。
- 全解决方案 895/895 通过，0 失败、0 跳过。
- `win-x64` NativeAOT 发布与进程级 API/SQLite/WebUI/WebSocket smoke 通过。
