# 外部媒体手动扫描与补录验证

## 边界

- 默认不扫描；没有启动任务、HostedService、Cron 或自动配置项。
- 全库入口：`POST /api/v1/library/external-media/import`。
- 单季度入口：`POST /api/v1/library/seasons/{tmdbSeriesId}/{seasonNumber}/external-media/import`。
- 只读取 `save_path/<TMDB规范名>/Sxx` 的直接视频文件，标准 stem 为 `E###`。
- 每个候选必须唯一命中本地已验证的 TMDB Episode snapshot；完成来源固定为 `external_import`。
- 不移动/删除媒体，不进入 `Other`，不生成 sidecar、NFO 或来源 alias；响应不返回绝对路径。

## 自动化验收

- `ExternalMediaImportStoreTests`：3 项通过。覆盖唯一文件补录、已有记录幂等、全库/季度范围、默认无副作用、未知 EP、非标准命名、空文件、同 EP 多视频、字幕及 `Other` 排除。
- `AnimeLibraryApiTests.ExplicitExternalMediaImportUpdatesCanonicalProgressAndReturnsRelativeAudit`：通过。验证 API 补录后季度进度即时更新、来源为 `external_import`、响应只含相对路径及全库重复扫描幂等。
- `StaticWebUiTests`：164 项通过，包含全库/季度两个按钮、API 路径、结果样式和跳过原因资源。
- `npm run web:test`：19 项通过；TypeScript strict 构建、静态可访问性和共享前端协议测试通过。
- `dotnet build AnimeGoNet.slnx --no-restore -p:UseSharedCompilation=false -nodeReuse:false`：通过，0 warning / 0 error。
- `dotnet test AnimeGoNet.slnx --no-restore`：全解决方案 1689/1689 通过。
- `dotnet publish ... -c Release -r win-x64 -p:PublishAot=true --self-contained true`：NativeAOT 通过；最新产物以 TestSpace 的独立 data/download/save 路径运行在 `http://127.0.0.1:6180/`。
- 实际 WebUI 验证已确认全库按钮、单季度按钮和“默认不扫描”说明可见；验收未点击扫描按钮，没有修改现有媒体完成记录，也未启动真实 qB/RSS 流程。
