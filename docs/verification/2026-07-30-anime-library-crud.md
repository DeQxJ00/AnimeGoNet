# TMDB 作品库季度 CRUD 验收

日期：2026-07-30

## 行为边界

- 创建入口只接受正整数 `TMDB Series ID + Season`，分别读取并验证 TMDB
  Series/Season 后，保存 TMDB 名称、开播日期、封面和完整 Episode snapshot。
- 更新不是手工编辑，而是用 64 位 `resource_revision` 做乐观并发后重新读取
  TMDB 权威投影；远端已移除的 Episode 会从 snapshot 删除，完成记录不改写。
- 删除只允许无业务引用的本地 Season/EP 投影。最后一个 Season 删除后才删除
  Series；任务文件、完成记录、Episode claim、Mikan 人工规则、fallback 完成
  记录或活动 NFO 重写任一存在时返回 `409 library_season_in_use`。
- 作品库删除不调用 qBittorrent，不删除下载源文件或媒体文件；这些动作仍只能
  进入四类删除预览/确认/执行流程。
- API 不返回内部 SQLite 行 ID、绝对路径、Torrent URL、passkey 或凭据。

## 自动测试

定向数据/API/UI 契约测试：

```text
AnimeLibraryAdminStoreTests + AnimeLibraryStoreTests
Passed: 10, Failed: 0

AnimeLibraryAdminApiTests + AnimeLibraryApiTests + StaticWebUiTests
Passed: 83, Failed: 0
```

覆盖 TMDB 权威创建、重复冲突、完整 Episode snapshot、revision 冲突、刷新后
清理过期 Episode、保留同 Series 其他季度、业务引用保护、最后季度删除、安全
TMDB 失败投影和静态 TypeScript/HTML/CSS 契约。

完整 Release 门禁：

```text
dotnet test AnimeGoNet.slnx -c Release --no-restore
Plugin abstractions: 11 passed
Core: 264 passed
Data: 149 passed
App: 460 passed
Total: 884 passed, 0 failed, 0 skipped

dotnet build AnimeGoNet.slnx -c Release --no-restore
0 warnings, 0 errors
```

前端唯一 TypeScript 源码重新编译到 `wwwroot/app.js`，`web:check` 与
`git diff --check` 通过。

## 隔离浏览器验收

使用随机测试数据目录和关闭后台 Worker 的本地进程，不读取 TestSpace、
qBittorrent 或正式数据库。DOM 验收确认：

- 动画作品库显示 `TMDB Series ID`、`Season` 和“从 TMDB 添加”；
- 两个输入只能提交正整数，按钮唯一且可恢复启用；
- 未配置 TMDB 凭据时提交 `100 / S01`，页面稳定显示
  `添加失败：TMDB library creation failed.`；
- 页面未回显配置值或密钥，浏览器 `warning/error` 控制台记录为 0。

成功创建、刷新、revision 冲突和安全删除使用真实 Kestrel API 加 fake TMDB
完成集成验证，避免浏览器验收依赖外网或用户测试密钥。

## NativeAOT

```text
dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj \
  -c Release -r win-x64 --self-contained true --no-restore \
  -o artifacts/publish/win-x64 \
  -p:PublishAot=true -p:ContinuousIntegrationBuild=true

eng/smoke-native.ps1 \
  -Executable artifacts/publish/win-x64/AnimeGoNet.App.exe

Publish: passed, no trim/AOT warnings
Native smoke: passed
```

五个目标 RID 与 Docker 构建仍由既有 GitHub Actions/Buildx 门禁持续执行；本次
改动未增加反射 ORM、运行时 JSON 元数据或动态插件扫描。
