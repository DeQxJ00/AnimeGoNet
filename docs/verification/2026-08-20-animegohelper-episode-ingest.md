# AnimeGoHelper Episode URL 快速下载验证

日期：2026-08-20

## 问题与修复

未经修改的 AnimeGoHelper 在“单集”快速下载时向 `/api/download/manager` 提交标题、Torrent URL 和 `/Home/Episode/{40位ID}`，不会直接提交数字 `mikanid`。旧适配器把该请求直接交给统一导入，因此在规范化阶段以 `mikan_bgmid_mikanid_missing` 拒绝。

当前适配器会先通过所选 Mikan SourceProfile 解析 Episode 页面，取得并持久化 `source_item_id`、`mikanid`、`groupid` 和 `bgmid`，再进入统一导入；标题和 Torrent URL 保持原请求值。解析失败使用 legacy `code=300` 返回实际稳定错误码，不创建导入任务。

## 自动验证

- 未修改请求形状回归测试：3/3 通过。
- Core 统一导入测试：10/10 通过。
- 相关 App API 套件：39/39 通过。
- 断言 SQLite `ingest_tasks` 保存 Episode ID、`mikanid`、`groupid`、`bgmid`、原始标题和无查询参数的来源页面 URL。

## 真实只读解析 smoke

真实 Mikan Episode 页面解析得到 `mikanid=3980`、`groupid=370`、`bgmid=558064`，并保留原 40 位 Episode ID。该 smoke 仅调用身份解析端点，不提交 Torrent、不创建 qBittorrent 任务；成功身份允许写入设计中的长期缓存。
