# SQLite schema 与迁移验证

- 模块：`AnimeGoNet.Data.Sqlite`、`CompletionRecordStore`
- 日期：2026-07-19
- SQLite provider：Microsoft.Data.Sqlite 9.0.4

## 命令

```powershell
dotnet restore tests/AnimeGoNet.Data.Tests/AnimeGoNet.Data.Tests.csproj --source "$env:USERPROFILE/.nuget/packages" -p:NuGetAudit=false
dotnet build AnimeGoNet.slnx --no-restore --configuration Release
dotnet test AnimeGoNet.slnx --no-build --configuration Release --logger "console;verbosity=minimal"
```

## 结果

- Build：通过，0 warning，0 error。
- Core tests：9 passed，0 failed，0 skipped。
- Data tests：9 passed，0 failed，0 skipped。
- NativeAOT：Data/Core 均启用 AOT/trim analyzer；可执行 publish smoke 在 App 模块执行。
- SQLite：schema v1 完整表检查、migration 幂等、`foreign_keys=ON`、`integrity_check=ok`、可信 offset 至少三份证据、重复来源 EP 不计数、`tmdbid=0` 必须有 bgmid、并发完成写入只成功一次、其他 EP 不受影响均通过。
- 上游 fixture：本提交建立新 SQLite 数据模型，不宣称 `.bolt` 二进制 parity；旧数据 JSON 导入另行实现。
- Parity deviations：显式接受 SQLite 替代 Bolt；其余 none。
