# Mikan 季度 EP 自动补完验证（2026-08-22）

## 行为边界

- 动画库只从本季度已有任务取得 `source_profile_id + mikanid + groupid`，不允许页面任意指定其它作品或字幕组。
- 预览 URL 由全局 Mikan Base URL 与正整数 `mikanid/groupid` 构造；响应只返回候选标题、来源/目标 EP、大小、发布日期和状态，不返回 Torrent URL、Cookie 或 passkey。
- 已有来源 completion alias、已完成目标 TMDB EP 和非普通 EP 默认不勾选；无人工/可信 Offset 时目标 EP 保持未知，后续仍由正式 Series/Season/Episode 流程匹配并验证。
- 确认时重新读取 RSS，并校验季度 revision、来源绑定和候选 ID 未变化；之后复用正式 Mikan RSS 黑白名单、有序规则、SQLite 去重和统一导入。
- WebUI 允许恢复推荐、选择全部普通 EP 或清空；超过 12 条必须额外确认。提交 API 只创建/复用任务，不等待实际下载完成。

## 自动化验收

- `AnimeLibraryApiTests.MikanSeasonCompletionPreviewsMissingEpisodesAndConfirmsSelectedCandidate` 使用隔离 HTTP transport 验证绑定、RSS URL、来源 alias 去重、人工 Offset、目标完成状态、URL 不泄漏、确认重新拉取及正式任务 staging。
- `AnimeLibraryStoreTests` 覆盖动画季度投影与完成 alias 查询；详情 API 验证 `mikan_bindings` 和关联任务 `groupid`。
- `tests/web/accessibility-contract.test.mjs` 验证带名称的 modal、绑定选择、候选表格、季度 revision 与超过 12 条二次确认契约。
- `npm run web:test`：37/37 通过；`AnimeLibraryApiTests`：17/17 通过；`AnimeLibraryStoreTests`：13/13 通过；静态 WebUI：203/203 通过；OpenAPI 文档：4/4 通过。
- `dotnet build AnimeGoNet.slnx --no-restore -p:UseSharedCompilation=false -nodeReuse:false`：0 warning / 0 error。
- `dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 -p:PublishAot=true --self-contained true`：通过，生成 46,632,960-byte 本机可执行文件；验证后已清理临时发布目录。
- 本功能没有访问用户真实 Mikan RSS、Torrent 或 qBittorrent；真实下载必须由用户在 WebUI 明确选择候选并确认。
