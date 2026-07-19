# Core 配置与目录模型验证

- 模块：`AnimeGoNet.Core.Configuration`
- 日期：2026-07-19
- SDK：10.0.302；运行时：10.0.10；宿主：win-x64

## 命令

```powershell
dotnet restore tests/AnimeGoNet.Core.Tests/AnimeGoNet.Core.Tests.csproj --source "$env:USERPROFILE/.nuget/packages" -p:NuGetAudit=false
dotnet build AnimeGoNet.slnx --no-restore --configuration Release
dotnet test AnimeGoNet.slnx --no-build --configuration Release --logger "console;verbosity=normal"
```

## 结果

- Restore：通过。项目外 NuGet/MyGet 源在当前环境超时，因此验证使用机器已有的 NuGet global-packages 本地层级源；未修改用户 NuGet 配置。
- Build：通过，0 warning，0 error。
- Tests：9 passed，0 failed，0 skipped。
- NativeAOT：Core 是 AOT-compatible library，`IsAotCompatible`、trim/AOT analyzer 已开启；可执行 publish smoke 在 App 模块执行。
- 上游 fixture：本模块是新增部署/安全不变量，没有直接使用上游 fixture；旧 YAML parity 尚未标为完成。
- Parity deviations：Python/Transmission 例外按已确认范围执行；其余 none。

覆盖：Docker 三个挂载路径、命名 qBittorrent、Mikan 默认 move、AI 600 秒、所有高风险 fallback 默认 false、POSIX/Windows 路径边界、数据目录创建范围和不支持 Transmission 诊断。
