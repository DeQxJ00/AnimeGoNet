# 总览匹配与整理数量提示验证

## 行为

- 总览“运行状态”增加 `Other 待处理`、`匹配错误`、`等待人工审核`三个数量提示。
- 数量复用 `/api/v1/metadata/tasks` 返回的全库 `attention` 聚合，不受当前任务列表分页和筛选影响。
- 总览可见时每 5 秒刷新；请求失败以 `!` 明确显示不可用，不把旧值伪装成实时值。
- 点击任一数量会进入“任务中心 / 匹配与整理”，清空冲突筛选并应用对应条件。

## 验收

- `npm run web:test`：28/28 通过，覆盖三个按钮、直接导航、筛选入口和紧凑总览样式。
- `dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj -c Release --filter "FullyQualifiedName~MetadataTaskFilterApiTests" --no-restore`：12/12 通过。
- win-x64 NativeAOT 发布成功，并使用现有本机 `data_path` 在 6180 启动；状态 API 确认
  `native_aot=true`、schema v51。
- 浏览器实际点击“Other 待处理”后进入 `#/tasks/metadata`，文件状态同步选择“含 Other”，
  任务中心对应计数卡为选中状态。当前本机三个聚合计数均为 0。
