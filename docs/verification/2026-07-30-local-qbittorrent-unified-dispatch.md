# 本机统一导入到 qBittorrent 安全验收（2026-07-30）

## 范围

- 只连接 `E:\WorkSpaceAI\AnimeGoNet\TestSpace\qbittorrent\qbittorrent.exe`
  及其 portable profile，不发现或连接系统安装实例。
- 使用用户名/密码 Cookie 登录，不使用 API key。
- `data_path` 为
  `TestSpace\animegonet_data\integration\qbit-dispatch-<runid>`，
  `download_path` 为 `TestSpace\download_temp`，
  `save_path` 为 `TestSpace\jellyfin_data`。
- 固定 Torrent 只有一个 5 字节文件，tracker 为不可用的
  `http://127.0.0.1:9/announce`，不会连接公开 tracker 或下载私人内容。

## 执行链

显式 `-DispatchFixture` 测试依次经过：

1. Mikan 强类型 adapter 与 `UnifiedIngestProcessor`；
2. fixture staging 与真实 Torrent metainfo/info-hash 解析；
3. 独立 SQLite `ingest_tasks`/`staged_torrents`；
4. `StagedTorrentDispatcher`；
5. 真实 qBittorrent multipart add、按 info-hash 确认和再次暂停；
6. SQLite `download_jobs` 与 `download_preparing` 原子状态；
7. staging 文件删除。

验收 qB 任务的 category、唯一 tag、info-hash、5 字节容量和暂停状态，并核对
SQLite 捕获的 download/save root。

## 安全与清理

- 测试开始时要求 qB 任务列表为空，避免接触私人任务。
- category 为 `animegonet-integration-<runid>`，tag 为
  `animegonet-test-<runid>`。
- 结束时精确删除测试 info-hash，固定 `deleteFiles=false`，再删除精确
  category/tag。
- fixture payload 在测试前必须不存在；任务全程暂停，测试中和测试后均不得出现。
- 每次运行的独立 SQLite 根目录在应用释放后删除，portable profile、Cookie、
  WebUI 凭据和用户路径中的其他内容不删除。

## 实测结果

- TestSpace qBittorrent `v5.2.3`，用户名/密码 Cookie 登录成功。
- 显式测试 2/2 通过：只读版本/路径 smoke 与统一导入 dispatch fixture。
- qB 新任务会短暂进入 `checkingResumeData`；测试等待其稳定到暂停态后再验收，
  从未调用 start/resume。
- 测试结束后精确 info-hash、唯一 category/tag、fixture payload 和本次
  `data_path` 均不存在；portable qB 进程继续运行。
- 默认 Release 解决方案测试 908/908 通过，0 失败、0 跳过；本机集成项目仍不在
  默认 solution/CI 中，只有显式脚本会启动。
