# 合集非视频附件独立 Extras 分类

## 已确认语义

- 同一 Torrent 至少有一个视频文件成功匹配并通过 TMDB Episode 验证后，未匹配 Episode 的非视频文件使用 `disposition=extras`。
- `extras` 文件仍是 wanted 下载文件，并保留原文件名整理到 `<TMDB动画名>/Sxx/Extras/`。
- `extras` 不计入 Other 数量，不触发 Other 通知，也不进入“重新适配 Other”。
- 未匹配 Episode 的视频继续使用 `disposition=other`；没有任何成功视频的纯附件任务也继续保留为 Other，避免任务被静默隐藏。
- schema v67 会把已有任务中“成功正片旁边的非视频 Other”迁移为 Extras，并保留原 `other_reason` 作为审计原因。

## 验收覆盖

- Episode worker：成功视频加两个 `.7z` 附件得到 `episode + extras + extras`，任务 `other_file_count=0`。
- 单独图片/附件：仍为 Other，验证条件不会过度放宽。
- 路径规划与真实整理处理器：`extras` 保留原名写入 `Sxx/Extras`，不生成 Episode 完成记录。
- schema migration：仅转换成功视频旁的非视频附件，未匹配视频和纯附件任务保持 Other，并验证外键引用完整。
