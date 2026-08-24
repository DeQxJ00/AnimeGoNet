# 跨平台 Torrent 文件清单路径验收（2026-08-24）

## 问题与边界

Torrent metainfo 可以包含 `:` 等在 Linux 合法、但在 Windows 文件名中非法的字符。
qBittorrent 在 Windows 落盘时会把这些字符改写，因此原始 Torrent 清单与 Web API
返回路径不能直接逐字比较。原实现只统一路径分隔符，会把已经完成元数据匹配的任务
误判为 `download_file_manifest_mismatch` 并反复暂停。

## 实现

- `PortablePathNormalizer` 提供唯一的文件名段与相对路径规范化规则；媒体库目标命名和
  下载准备清单比较共用该实现。
- 规则固定统一 `/`/`\\`、Unicode NFC、控制字符、`<>:"/\\|?*`、尾随点/空格及
  Windows 保留设备名，不调用随运行平台变化的 `Path.GetInvalidFileNameChars()`。
- 比较保持 `StringComparer.Ordinal`，因此 Linux 上大小写不同的真实文件不会被误合并；
  macOS 常见的 Unicode 组合/分解形式会先归一为 NFC。
- 绝对路径、空段、`.`、`..`、文件索引/大小不一致，以及任一侧规范化后的路径碰撞
  都继续返回安全的文件清单不匹配，任务不会恢复。

## 自动验收

- Core 测试覆盖 Windows 分隔符、`:`/`?` 改写、Unicode 规范化、绝对路径和目录穿越。
- App 测试使用 `Cyborg 009: Nemesis` 原始路径与 Windows qBittorrent 返回的
  `Cyborg 009_ Nemesis` 路径，验证正确绑定 index/priority 并恢复任务。
- App 测试另覆盖两个原始路径规范化为同一路径时保持暂停，防止跨平台命名碰撞。

该修复不修改 Torrent 内容、qBittorrent 中的文件名或媒体文件，只修正下载准备阶段
的路径身份比较。

## 本机真实任务回归

在 TestSpace 已有的 `Cyborg 009: Nemesis - 03` 任务上启动修复后的主程序；未创建、
删除或替换 Torrent。原始清单的冒号路径与 Windows qBittorrent 返回的下划线路径
成功关联，准备状态从 `download_file_manifest_mismatch` 变为 `completed`，文件绑定
为 index 0 / priority 1，任务随后进入 `downloading` 且下载进度持续增加。测试过程
未把 qBittorrent 凭据、Cookie、Torrent URL 或 passkey 写入仓库。

验收命令：

```powershell
dotnet build AnimeGoNet.slnx -c Release --no-restore
dotnet test tests/AnimeGoNet.Core.Tests/AnimeGoNet.Core.Tests.csproj -c Release --no-build
dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj -c Release --no-build
dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 --no-restore -p:PublishAot=true
```

结果：解决方案构建 0 警告/0 错误；Core 429/429、App 1174/1174 通过；win-x64
NativeAOT 发布成功。其他目标平台由相同的无 OS 分支 Core 逻辑测试及 CI 的
win-arm64、linux-x64、linux-arm64、osx-arm64 NativeAOT 矩阵持续覆盖。
