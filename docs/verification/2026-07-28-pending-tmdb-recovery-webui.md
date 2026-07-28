# 待补全 TMDB 人工恢复 WebUI（2026-07-28）

## 行为

- 待补全详情增加“人工 TMDB 恢复”表单。
- 一个 TMDB Series ID 作用于本次提交；每条安全候选分别填写 Season/Episode。
- 只有正整数可提交；单一已确认季度会自动填入 Season，普通整数来源集号会自动填入 Episode，小数或无法确认的集号保持空白。
- 提交前显示确认框，提交期间禁用按钮并显示 TMDB 验证状态。
- 成功结果显示恢复数量和 `DuplicateAfterResolution` 数量，并刷新待补全与元数据任务。
- API 错误使用安全消息显示，不在 DOM 中注入 HTML。
- 表单打开时暂停十秒自动轮询，避免填写中的映射被卡片重建清空；手动刷新仍明确丢弃当前表单并重新读取。

## 自动验收

```text
npm run web:check
npm run web:build
node --check src\AnimeGoNet.App\wwwroot\app.js
dotnet test tests\AnimeGoNet.App.Tests\AnimeGoNet.App.Tests.csproj --no-restore --filter "FullyQualifiedName~StaticWebUi|FullyQualifiedName~PendingTmdb"
```

结果：TypeScript 类型检查、静态 JS 语法检查通过；联合测试 27/27 通过。

全解决方案 538/538 通过（Core 199、Data 89、App 250）；win-x64 NativeAOT 发布通过。

## 浏览器验收

使用隔离临时 `data_path/download_path/save_path` 启动本地服务，并只向临时 SQLite 写入一条可丢弃 fallback fixture。实际页面验证：

- 待补全卡片与恢复入口可见；
- 详情显示去重风险、恢复说明与确认动作；
- Season 自动填充为 2，普通来源 Episode 自动填充为 1；
- 布局在当前窗口宽度下无横向溢出，输入和按钮层级清晰；
- 控制台 error/warning 为 0。

预览进程和临时目录已清理，未连接 `TestSpace` qBittorrent。
