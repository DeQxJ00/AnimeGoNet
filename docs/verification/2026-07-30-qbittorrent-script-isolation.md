# qBittorrent 本机脚本隔离验收

日期：2026-07-30

## 发现的问题

`eng/qbittorrent-local-integration.ps1` 原先执行整个
`AnimeGoNet.LocalIntegration.Tests` 项目。qB 登录、版本、空任务列表和路径检查
已经通过后，脚本仍会启动 `TmdbLiveSmokeTests`，并因本次没有设置 TMDB 环境
变量而把 qB smoke 误报为失败。

## 修复

- qB 脚本固定添加
  `--filter 'FullyQualifiedName~QbittorrentSandboxTests'`；
- 默认解决方案新增静态测试，断言 qB 脚本保留该过滤器且不自行启用 TMDB
  integration；
- 用户名、密码只存在于调用进程环境，脚本结束后清除；未写入仓库、配置、
  profile 或测试输出。

## 真实 TestSpace 验收

本次只启动并附着到：

```text
E:\WorkSpaceAI\AnimeGoNet\TestSpace\qbittorrent\qbittorrent.exe
```

端口 8080 的唯一所有者与上述进程一致，portable profile lock 存在。修复后的
显式 smoke：

```text
QbittorrentSandboxTests.IsolatedQbittorrentVersionLoginListAndPaths
Passed: 1, Failed: 0
qBittorrent API/executable: v5.2.3
download_path: E:\WorkSpaceAI\AnimeGoNet\TestSpace\download_temp
save_path: E:\WorkSpaceAI\AnimeGoNet\TestSpace\jellyfin_data
data_path: E:\WorkSpaceAI\AnimeGoNet\TestSpace\animegonet_data
```

测试确认用户名/密码 Cookie 登录、API/文件版本一致、任务列表为空、默认保存/
临时目录为 `download_temp`，三个 AnimeGoNet 路径存在并相互分离。没有创建
Torrent、category、tag 或下载文件，也没有修改 qB 偏好。

默认测试：

```text
LocalIntegrationScriptTests
Passed: 1, Failed: 0

Full Release solution: 892 passed, 0 failed, 0 skipped
win-x64 NativeAOT smoke: passed
```
