# 下载器部署锁（2026-07-30）

## 范围

- 环境变量和命令行参数按 `downloader_id + field` 建模部署锁，支持
  `type`、`base_url`、`username`、`password`、`download_path`、`enabled`。
- 兼容 `ANIMEGO_CLIENT*` 旧键，并将其限制到 `bt` 实例；规范嵌套键可锁定任意
  命名 qBittorrent 实例。
- 部署锁在私有覆盖后重应用；读取待重启私有覆盖时也重新投影最终有效值，避免
  WebUI 显示一个实际不会生效的值。
- 下载器 API 返回字段、来源和控制键名，不返回环境变量值或命令行参数值；WebUI
  逐字段禁用并显示来源。
- API 拒绝改变锁字段。只保存未锁字段时，不把环境/命令行用户名和密码复制到
  `downloaders.private.json`。

## 验证

- `npm run web:check`：TypeScript 检查通过。
- 聚焦测试：11/11 通过，覆盖规范/旧键、环境与命令行合并来源、实例隔离、锁值
  重应用、API 拒绝、响应脱敏、私有覆盖不复制部署凭据及 WebUI 静态契约。
- `dotnet build AnimeGoNet.slnx -c Release --no-restore`：0 warning /
  0 error。
- 全量 `ANIMEGO_UPSTREAM_REPO=E:\WorkSpaceAI\AnimeGoNet\AnimeGo dotnet test
  AnimeGoNet.slnx -c Release --no-build`：1005/1005 通过
  （Plugin 11、Core 301、Data 160、App 533）。
- `win-x64` NativeAOT publish 成功，无 trim/AOT warning。
- `eng/smoke-native.ps1` 使用发布后的原生程序验证 schema 32、SQLite、静态
  WebUI、WebSocket 与安全配置投影并正常清理。

全部测试使用临时 data/download/save 目录和 fake 下载器；未连接本机
qBittorrent，未读取 TestSpace、Cookie、WebUI 凭据或 passkey。
