# 本机 qBittorrent 隔离集成测试

## 边界

本测试只附着到 `E:\WorkSpaceAI\AnimeGoNet\TestSpace` 中的 qBittorrent portable 实例，不查找或使用系统安装的其他 qBittorrent。它不属于默认解决方案，也不会由 CI 启动；常规单元测试继续使用 fake handler。

沙箱目录固定为：

```text
TestSpace/
├─ download_temp/       # download_path；qBittorrent 下载和临时目录
├─ jellyfin_data/       # save_path；AnimeGoNet 整理输出
├─ animegonet_data/     # data_path；AnimeGoNet 本机测试数据
└─ qbittorrent/
   ├─ qbittorrent.exe
   └─ profile/          # portable profile，不得提交
```

整个 `TestSpace/`、`*.local-integration.json` 和 `.env.integration` 均已被 Git 忽略。不得把 profile、Cookie、WebUI 凭据、API key、passkey、Torrent 文件或下载内容复制到仓库的其他路径。

## 启动与配置

1. 直接启动沙箱中的 `qbittorrent.exe`。同级存在 `profile` 时 qBittorrent 使用 portable profile；不要改用系统 qBittorrent。
2. 在 qBittorrent WebUI 设置中启用 WebUI，确认监听端口和可访问地址。qBittorrent 5.2.3 要求 WebUI 密码至少 6 个字符。
3. 首版集成只验收用户名/密码 Cookie 登录，不使用 API key。启用本机认证，避免 loopback 绕过导致假阳性。
4. 在当前 PowerShell 进程设置本地变量；不要把值写进脚本或提交：

```powershell
$env:ANIMEGONET_QBIT_BASE_URL = 'http://<sandbox-host>:8080/'
$env:ANIMEGONET_QBIT_USERNAME = '<local-test-user>'
$env:ANIMEGONET_QBIT_PASSWORD = '<local-test-password>'
```

首次运行先显式还原独立测试项目，然后执行 smoke：

```powershell
dotnet restore tests/AnimeGoNet.LocalIntegration.Tests/AnimeGoNet.LocalIntegration.Tests.csproj
./eng/qbittorrent-local-integration.ps1
```

脚本确认端口所有者就是沙箱 `qbittorrent.exe`、portable profile lock 存在，并创建独立的 `animegonet_data`。默认模式随后通过主程序 qBittorrent adapter 完成用户名/密码登录、读取任务列表、核对程序/API 版本和 `download_path`。默认模式不会修改 qBittorrent 偏好、关闭用户已启动的进程或创建 Torrent。

`AnimeGoNet.LocalIntegration.Tests` 同时包含独立的 TMDB live smoke，因此 qB 脚本必须以 `FullyQualifiedName~QbittorrentSandboxTests` 过滤，只运行 qB 测试。不得通过设置无关 TMDB key 来掩盖脚本串跑。

要显式验收统一导入写入链，使用：

```powershell
./eng/qbittorrent-local-integration.ps1 -DispatchFixture
```

该开关只使用仓库中的 `tests/fixtures/animegonet-ci.torrent.b64`：Torrent
总大小 5 字节，tracker 固定为不可用的 `127.0.0.1:9`，任务从添加到删除始终
保持暂停。测试经过 `UnifiedIngestProcessor`、隔离 SQLite、staging、
`StagedTorrentDispatcher` 和真实 qB Web API，并验证任务进入
`download_preparing`。它不会读取 RSS、私人 Torrent URL 或现有任务内容。

要显式执行合法小文件的真实下载、状态和 `move` 整理闭环，使用：

```powershell
./eng/qbittorrent-local-integration.ps1 -DownloadFixture
```

这个模式不读取仓库外 Torrent。测试进程每次动态生成一个 128 KiB 确定性 payload
和 BitTorrent v1 metainfo；唯一 web seed 是测试进程自身随机端口上的
`127.0.0.1` HTTP server，tracker 仍固定为不可用的 `127.0.0.1:9`。测试先要求
隔离 qB 任务为空，然后依次执行统一导入、暂停 dispatch、已验证 Episode 边界注入、
真实 qB 文件 priority/resume/download、SQLite snapshot、Mikan 默认 `move`、NFO 与
三层 sidecar、completion 和 `deleteFiles=false` downloader cleanup。Episode 边界
使用测试 SQLite 中的合成已验证身份，不调用真实 TMDB/Bangumi，也不消耗 API key。
同一命令还运行四文件多文件 fixture：主视频与关联的 `.zh-Hans.forced.ass` 字幕为
wanted，重复 EP 与 ignored 海报为 unwanted；验收 qB priority 固定为 `1,1,0,0`，
unwanted 文件下载进度保持 0，字幕随主视频整理为 `E001.zh-Hans.forced.ass`，并且只
产生一条 Episode completion。

## 验收

- `qbittorrent.exe` 和监听端口属于同一个沙箱进程。
- portable profile 位于 `TestSpace\qbittorrent\profile`。
- WebUI 用户名/密码登录成功；不是依赖 `LocalHostAuth=false` 的绕过结果。
- API 版本与 exe 产品版本一致。
- qBittorrent 默认保存路径是 `TestSpace\download_temp`。
- `download_path`、`save_path`、`data_path` 三个目录存在且彼此分离。
- 默认测试前后没有新增 Torrent、category、tag 或下载文件。
- `-DispatchFixture` 使用每次唯一的 category
  `animegonet-integration-<runid>` 和 tag `animegonet-test-<runid>`；添加结果必须
  是预期 info-hash、暂停状态、5 字节容量和正确路由快照。
- 写入测试结束后用 `deleteFiles=false` 删除精确 info-hash，再删除精确
  category/tag；测试前不存在、测试中也未创建 fixture payload，运行 SQLite
  位于 `animegonet_data/integration/qbit-dispatch-<runid>` 并在结束后清理。
- `-DownloadFixture` 必须看到 qB 文件 priority=1、loopback web-seed 请求、源文件
  的真实长度/逐字节内容、`downloaded → organizing_cleanup → organized` 状态、媒体库
  `E001.mkv`/NFO/sidecar 和单一 completion；随后精确任务、category、tag、源/媒体
  文件及本次 `qbit-legal-download-<runid>` 数据目录全部不存在。
- 多文件 fixture 还必须看到 wanted 主视频/字幕 priority=1，duplicate/ignored
  priority=0 且 progress=0；媒体库只能出现 `E001.mkv`、
  `E001.zh-Hans.forced.ass` 和该 EP 的 sidecar，不能出现重复 EP 或海报。

## 真实 Torrent 的安全边界与清理

真实下载必须由测试调用方提供明确、可合法分发的固定输入，禁止接入私人 RSS 或现有私人任务。未来的写入型测试统一使用 category `animegonet-integration`、tag `animegonet-test-<runid>` 和可辨识任务名；测试只清理同时带这些标记且 run ID 匹配的任务及其测试文件。

默认 smoke 是只读的，不创建任何需要清理的 qBittorrent 对象。显式
`-DispatchFixture` 模式会在 `finally` 中清理精确 info-hash、唯一 category/tag、
可能的精确 fixture payload 和本次 SQLite 根目录；即使断言失败也执行清理。
它固定使用 `deleteFiles=false`，不会让 qB 递归删除下载目录。若进程被强制终止，
只可按控制台中的 run ID 清理上述固定前缀对象，不能批量删除其他任务。

`-DownloadFixture` 使用相同唯一前缀，并只删除本次精确 payload、`!qB` 临时名、
多文件 Torrent 根、唯一系列目录和独立 SQLite 根。即使下载或整理断言失败，`finally` 仍先对精确
info-hash 调用 `deleteFiles=false`，再执行这些受父目录边界验证的精确清理；不枚举、
不读取也不删除其他 qB 任务或其他媒体目录。

结束后清除当前 PowerShell 中的 `ANIMEGONET_QBIT_*` 认证变量；不要删除 portable
profile。`animegonet_data` 是独立测试数据目录，可在确认 AnimeGoNet 测试进程已
停止后按具体测试文档清理。

## 主程序诊断 API

主程序启动且下载器配置已指向本沙箱后，可在 WebUI“下载器实例”中分别执行：

- “测试连接”：调用 `POST /api/v1/downloaders/{id}/test`，验证用户名/密码 Cookie 登录、任务列表、qB 客户端版本和默认保存路径。
- “探测路径”：调用 `POST /api/v1/downloaders/{id}/path-probe`，验证 AnimeGoNet 进程能同时访问 `download_path` 与 `save_path`，并实际验证两者之间能否创建硬链接。

路径探测会创建 `.animegonet-hardlink-<随机值>.tmp`，成功或失败后均尽力删除。它不会创建 Torrent、分类或 tag，也不读取现有任务内容。若进程意外终止，可仅在确认没有探测请求运行后，手工清理 `download_temp` 与 `jellyfin_data` 中这个固定前缀的残留临时文件；不得批量删除其他内容。
