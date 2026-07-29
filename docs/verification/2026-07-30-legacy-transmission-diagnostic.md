# 旧 Transmission 配置安全迁移诊断（2026-07-30）

## 行为

- 只支持 qBittorrent；没有实现或注册 Transmission client。
- 旧环境变量 `ANIMEGO_CLIENT` 优先；没有该值时读取
  `ANIMEGO_CONFIG`/`--config`，否则读取 `data_path/animego.yaml`。
- AOT-safe 行解析器只检查 `setting.client.client`，支持生成配置中的缩进、注释和
  单/双引号，不反序列化任意 YAML 对象。
- 文件最大 1 MiB，使用严格 UTF-8。显式文件缺失、过大、无权限或非法编码都生成
  `LegacyConfigurationUnreadable` 并 fail closed。
- Transmission 或其他非 qB 类型生成 `UnsupportedDownloaderType`。响应只含类型、
  来源类别和安全修复说明，不读取或回显旧 URL、用户名、密码。

## 阻断边界

诊断存在时，应用仍初始化 SQLite、Minimal API 和静态 WebUI，但：

- 强制把 `background_workers_enabled` 的运行值改为 false；
- 无论调用方是否注入 registry，都替换为零实例 registry；
- `/api/v1/status` 将 `unified_ingest` 和 `qbittorrent` capability 标为 false；
- 统一/旧导入在 Torrent staging 前拒绝，不创建任务；
- 新 RSS 入口在 feed HTTP 前返回 409，旧 `/api/rss` 在 fetch 前返回兼容 code 300；
- pause/resume/retry、下载器连接测试和路径探测返回 409 及稳定诊断码；
- 下载器列表把所有运行实例显示为停用和 `blocked_by_legacy_migration`；
- 配置页与下载器页显示诊断，但仍允许用户写入新的 qB 私有配置。

旧环境变量或 YAML 仍然生效时，新增 qB 覆盖不能绕过阻断；运维人员必须先移除或
修复旧来源并重启。这避免把 Transmission 静默解释成默认 `bt`。

## 自动验证

- 环境变量大小写/空白、显式 qB 覆盖、嵌套 YAML、注释/URL 中相似文本、凭据不
  泄漏、显式路径、缺失/超大文件均有 detector tests。
- 两条真实 `AnimeGoApplication.BuildAsync` 配置加载路径分别验证环境变量和 YAML
  会关闭 workers 并注册空 downloader registry。
- 运行中 API 测试验证 Web/config/status/downloaders 可访问，连接与路径操作为
  409，统一导入为零接受且没有访问调用方提供的下载器 registry。
- TypeScript build/check 通过；Release 全量测试 908/908 通过，0 失败、0 跳过。
- `win-x64` .NET 10 NativeAOT publish 与 published-binary smoke 通过，覆盖
  schema v30、SQLite、静态 WebUI、WebSocket、安全 ingest 拒绝和 capability。
- 使用隔离的 NativeAOT 实例和 `ANIMEGO_CLIENT=Transmission` 在真实浏览器验收：
  配置页显示稳定诊断码、类型和来源，后台 workers 显示为关闭；下载器页的两个
  实例均停用，连接/路径操作按钮共 4 个均不可用；浏览器控制台无错误。
