# Mikan MyBangumi 聚合 RSS

日期：2026-08-13

## 原因与边界

`/RSS/MyBangumi?token=...` 是多番组聚合订阅。频道地址、频道链接和 item 链接均不提供一个可用于整批处理的 `bangumiId`；每条 item 只提供 `/Home/Episode/{id}`。因此不能把整份 RSS 当作一个 `mikanid`，也不能让不同番组的相同来源 EP 互相参与优选。

RSS URL、token、Episode URL 和 Torrent URL 仍按 secret 边界处理，不写入验证报告或日志。本功能不会改变单番组 `/RSS/Bangumi?bangumiId=...` 的处理路径。

## 实现

- 聚合 RSS 在规则执行前逐条读取 Episode 页面，从 `mikan-rss` 链接解析 `mikanid + groupid`；同一 Episode URL 在一次解析中只请求一次。
- 成功项按 `mikanid` 拆成独立持久化子批次。每个子批次分别执行原有五级过滤、有序规则、Bangumi Subject 获取、SQLite 去重和统一导入，因此相同 EP 只在同一番组内竞争。
- 解析失败只阻断对应 item，并保留稳定失败码；不会让一个坏 Episode 页面阻断其他番组。
- 子批次保存解析出的 `identity_mikanid/identity_groupid`。聚合 API 返回兼容的顶层 items，并增加 `batches` 展示各子批次的 batch id、mikanid、bgmid 和发现状态。
- Mikan 手动设置页面将聚合结果显示为多个“番组批次”，不再把正常的 MyBangumi 频道概括成单个“mikanid 未识别”批次。

## 验收

- `MikanFeedIdentityResolverTests`：覆盖逐项身份解析、重复 Episode URL 请求合并，以及单项解析失败隔离。
- `MikanRssIngestProcessorTests.MyBangumiFeedSplitsItemsByResolvedMikanWork`：两个番组在同一 RSS 中分别持久化、发现不同 bgmid，并把正确 mikanid 写入统一导入任务。
- App 定向回归（含旧 API 字段 golden）：157/157 通过；App 全量测试 982/982 通过；Web 原生测试 19/19 通过。
- 使用项目所有者提供的真实 MyBangumi RSS 执行只读 live smoke：当前 RSS 的全部 item 均从真实 Episode 页面取得身份，且确认包含多个不同 mikanid。测试没有请求 Torrent、没有调用 qBittorrent、没有创建下载任务。
- win-x64 NativeAOT 在 RID/AOT restore 后出现 `Generating native code`，生成约 40.7 MiB 的原生 EXE，`--help` smoke 退出码为 0。其他 RID 继续由既有 CI 构建矩阵验证。
