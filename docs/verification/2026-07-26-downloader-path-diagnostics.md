# qBittorrent 客户端与共享路径诊断（2026-07-26）

## 范围

- `QbittorrentClient` 通过官方 Web API 读取客户端版本和默认保存路径；可选 `IDownloadClientDiagnostics` 不扩大下载写操作契约。
- 下载器连接测试在登录和列出任务成功后返回版本、默认路径、任务数和延迟，不返回用户名、密码、Cookie 或异常正文。
- 显式路径探测检查命名实例 `download_path` 与全局 `save_path` 是否存在，再创建、验证并清理随机临时硬链接。
- Windows 使用 `CreateHardLinkW`，Linux 使用 `libc link`，macOS 使用 `libSystem.B.dylib link`；均由 `LibraryImport` 生成，避免运行时反射。
- WebUI 为每个启用实例提供“测试连接”和“探测路径”，并显示稳定、可操作的结果。

## 安全边界

- 路径探测不连接 qBittorrent，不创建或删除 Torrent、category、tag，也不读取用户下载内容。
- 临时文件固定使用 `.animegonet-hardlink-<GUID>.tmp` 前缀和 3 字节内容，源、目标都在已验证配置目录内。
- 缺目录不会自动创建；权限或文件系统异常仅返回脱敏错误码。`finally` 对源和目标执行尽力清理。
- 常规测试使用 `RunningApp` 的独立随机临时根目录，默认 CI 不附着 `TestSpace`。

## 自动验收

- `QbittorrentClientTests.DiagnosticsReadVersionAndDefaultSavePath` 固定验证两个官方端点和去除响应尾部空白。
- `DownloaderAdminApiTests.ConnectionTestAuthenticatesListsAndPersistsHealth` 验证诊断字段及既有健康状态。
- `DownloaderAdminApiTests.PathProbeCreatesAndCleansTemporaryHardLink` 在独立临时目录实际创建硬链接，并断言两侧无探测残留。
- `DownloaderAdminApiTests.PathProbeReportsMissingDirectoriesWithoutCreatingThem` 验证 `directory_missing` 且服务端不擅自创建共享目录。
- 静态 WebUI 契约验证路径探测函数及 qB 默认路径字段存在；TypeScript 编译和生成 JavaScript 语法检查纳入提交验收。
- 完整解决方案测试通过：Core 168、Data 66、App 172，共 406 项。
- `win-x64` NativeAOT 发布成功，新增 `LibraryImport` 与源生成 JSON 契约未产生裁剪/AOT 警告。

## 待外部环境验收

Compose 中 AnimeGoNet 与 qBittorrent 共同把同一宿主父目录映射为 `/download` 后，需再次运行连接与路径探测，确认 qB 默认路径、实例下载路径和媒体保存路径使用容器视角一致的映射。真实 Torrent 下载仍必须使用显式、可合法分发的测试输入和可辨识清理标签。
