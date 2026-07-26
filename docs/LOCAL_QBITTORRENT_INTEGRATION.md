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

脚本确认端口所有者就是沙箱 `qbittorrent.exe`、portable profile lock 存在，并创建独立的 `animegonet_data`。随后它通过主程序 qBittorrent adapter 完成用户名/密码登录、读取任务列表、核对程序/API 版本和 `download_path`。它不会修改 qBittorrent 偏好、关闭用户已启动的进程或创建 Torrent。

## 验收

- `qbittorrent.exe` 和监听端口属于同一个沙箱进程。
- portable profile 位于 `TestSpace\qbittorrent\profile`。
- WebUI 用户名/密码登录成功；不是依赖 `LocalHostAuth=false` 的绕过结果。
- API 版本与 exe 产品版本一致。
- qBittorrent 默认保存路径是 `TestSpace\download_temp`。
- `download_path`、`save_path`、`data_path` 三个目录存在且彼此分离。
- 测试前后没有新增 Torrent、category、tag 或下载文件。

## 真实 Torrent 的后续测试与清理

真实下载必须由测试调用方提供明确、可合法分发的固定输入，禁止接入私人 RSS 或现有私人任务。未来的写入型测试统一使用 category `animegonet-integration`、tag `animegonet-test-<runid>` 和可辨识任务名；测试只清理同时带这些标记且 run ID 匹配的任务及其测试文件。

当前 smoke 是只读的，不创建任何需要清理的 qBittorrent 对象。结束后只需清除当前 PowerShell 中的三个 `ANIMEGONET_QBIT_*` 认证变量；不要删除 portable profile。`animegonet_data` 是独立测试数据目录，可在确认 AnimeGoNet 测试进程已停止后按具体测试文档清理。

## 主程序诊断 API

主程序启动且下载器配置已指向本沙箱后，可在 WebUI“下载器实例”中分别执行：

- “测试连接”：调用 `POST /api/v1/downloaders/{id}/test`，验证用户名/密码 Cookie 登录、任务列表、qB 客户端版本和默认保存路径。
- “探测路径”：调用 `POST /api/v1/downloaders/{id}/path-probe`，验证 AnimeGoNet 进程能同时访问 `download_path` 与 `save_path`，并实际验证两者之间能否创建硬链接。

路径探测会创建 `.animegonet-hardlink-<随机值>.tmp`，成功或失败后均尽力删除。它不会创建 Torrent、分类或 tag，也不读取现有任务内容。若进程意外终止，可仅在确认没有探测请求运行后，手工清理 `download_temp` 与 `jellyfin_data` 中这个固定前缀的残留临时文件；不得批量删除其他内容。
