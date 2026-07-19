# Minimal API、静态 WebUI 与 NativeAOT 验证

- 模块：`AnimeGoNet.App`
- 日期：2026-07-19
- 宿主/RID：Windows x64 / `win-x64`

## 命令

```powershell
dotnet build AnimeGoNet.slnx --no-restore --configuration Release
dotnet test AnimeGoNet.slnx --no-build --configuration Release --logger "console;verbosity=minimal"
dotnet restore src/AnimeGoNet.App/AnimeGoNet.App.csproj --runtime win-x64 --source "$env:USERPROFILE/.nuget/packages" -p:NuGetAudit=false
dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj --configuration Release --runtime win-x64 --self-contained true --no-restore --output artifacts/publish/win-x64 -p:PublishAot=true
./eng/smoke-native.ps1 -Executable artifacts/publish/win-x64/AnimeGoNet.App.exe
```

## 结果

- Build：通过，0 warning，0 error。
- Tests：Core 9 + Data 9 + App 6 = 24 passed，0 failed，0 skipped。
- App integration：真实 Kestrel 随机端口；legacy `/ping` envelope、snake_case status、直接/legacy hash Access Key、HTML/CSS/JS 静态资源均通过。
- NativeAOT：`win-x64` publish 成功，无 AOT/trim warning；发布后的 `.exe` 返回 `/ping=200/pong`、`native_aot=true`、schema v1，创建 SQLite 数据库并提供静态首页。
- WebUI：生产路径是静态 HTML/CSS/JavaScript，并保留无框架 TypeScript 源；不包含 Vue/React 等运行时。
- Browser QA：in-app browser 连接初始化被当前运行时拒绝，未执行视觉截图；HTTP/Kestrel 集成和原生 smoke 已完成，视觉项保持待补测。
- Parity deviations：新增 `/api/v1/status`；legacy `/ping` 与 `/sha256` 保持 envelope。完整旧 API 尚未宣称完成。
