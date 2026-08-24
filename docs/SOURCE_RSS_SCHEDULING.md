# 来源 RSS 自动调度

首版自动调度只支持 `adapter=mikan`。每个 SourceProfile 可独立绑定 RSS URL、六字段 Cron、规则开关和 qBittorrent 路由，因此不同输入源可以按各自周期进入已有的 `MikanRssIngestProcessor`，不会绕过黑白名单、有序优选、SQLite 去重、TMDB 流程或不可变下载路由。

首次部署 YAML 也可为 SourceProfile 提供 `display_name`、`rss_feed_url`、
`rss_schedule_enabled` 和 `rss_schedule_cron` seed。旧 AnimeGo 的
`setting.feed.mikan` 与内置 `plugin.feed` name/URL/Cron/enable 会在原子升级时迁入；
上游原本关闭的 feed 保持关闭。seed 只作用于首次创建，用户在 SQLite/WebUI 中明确
清除后，重启不会从旧 YAML 反复恢复私密 URL。

## 配置和私密字段

- `rss_feed_url` 必须是无 userinfo、无 fragment 的绝对 HTTP(S) URL，可保留包含 passkey 的 query；原始 Host 必须包含在该来源的 `allowed_torrent_hosts` 中，重定向的每一跳仍执行同一白名单和公网 DNS 校验。
- API 只接受写入或 `clear_rss_feed_url=true`；响应仅返回 `rss_feed_url_configured`，永不返回原 URL。
- 更新请求省略 URL、Cron 或调度开关时保留当前值；若明确停用来源或清除 URL，则省略的调度开关自动变为关闭，显式要求继续开启会因前置条件不满足而拒绝。
- URL 保存在 `data_path` 下的 SQLite，不进入 Git、浏览器存储、调度参数、错误消息或状态投影。数据库备份本身仍应按敏感数据保护。
- 开启调度要求来源同时启用、adapter 为 Mikan 且 URL 已配置；默认 Cron 为 `0 0/15 * * * ?`（每 15 分钟）。

## NativeAOT 边界和执行流程

`MikanRssIngestSchedulePlugin` 由 `PluginCatalog` 编译期显式注册，不扫描 DLL、程序集或脚本。`SourceRssScheduleManager` 的任务参数只有 `source_profile_id` 与 `source_profile_revision`：

1. 启动时把上次异常退出遗留的 `running` 标记为 `failed/rss_schedule_interrupted`。
2. 读取所有启用调度的来源并注册 `source-rss-*`；后台 worker 被禁用时不注册。Mikan
   来源同时持久化 `media_type=tv|movie`，手动触发与自动调度均在执行前按 revision
   重读该值，旧数据库升级默认 `tv`。
3. 触发时按 ID+revision 重新取得启用来源和 RSS URL。revision 已变化时返回 `stale`，不访问网络。
4. SQLite 原子把状态从非运行态改为 `running`；同来源已有执行时旁路本次触发。
5. 通过 SourceProfile Host/DNS/redirect/Cookie 边界读取 feed，再进入现有 Mikan RSS 处理器。
6. 成功保存 `succeeded`、完成时间和 batch ID；失败只保存稳定失败码。协调器按既有策略最多重试三次。

WebUI 的“Mikan 手动设置 / 导入任务”还可调用
`POST /api/v1/sources/{source_profile_id}/rss/run` 立即执行来源已保存的 RSS。此入口不要求
`rss_schedule_enabled=true`，也不依赖后台调度器已注册；但来源必须启用、adapter 必须为
Mikan 且 RSS URL 已保存。手动执行与 Cron 共用 SQLite `running` 门禁和最近运行审计，进入
相同的抓取、过滤、优选、去重与统一导入链，响应和日志不回显保存 URL 或 passkey。

来源配置每次更新都会增加 revision 并清空旧执行审计。CRUD 成功后管理器立即移除/重建对应任务；删除来源后移除任务。运行中的旧 revision 即使稍后返回，也不能写入新 revision 的审计状态。

## API 与 WebUI

来源创建/更新接受：

- `rss_feed_url`
- `clear_rss_feed_url`
- `rss_schedule_enabled`
- `rss_schedule_cron`

来源响应增加配置布尔值、是否已注册、下次执行时间、最近运行状态/起止时间/失败码/batch ID。静态 WebUI 使用密码输入框，加载来源时不会填充 URL；保存成功后输入仍保持空白。界面明确区分“已配置但后台工作器未运行”和真实已注册状态。

## 验收与清理

- 单元/集成测试只使用内存 feed transport 和临时 SQLite，不访问用户真实 RSS、passkey、Torrent 或 qBittorrent。
- 手工测试应创建可识别的临时 Mikan SourceProfile，先用无下载候选的测试 feed 验证状态；完成后关闭调度并显式清除 URL，再删除没有任务/RSS batch 引用的来源。
- 若来源已产生 batch，引用保护会拒绝删除；这属于审计保留语义，不应直接改库绕过。
