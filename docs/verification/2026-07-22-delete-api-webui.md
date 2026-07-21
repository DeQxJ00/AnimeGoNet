# 删除中心 API / WebUI（2026-07-22）

## API

- `GET /api/v1/delete/tasks/{taskId}/preview`：返回任务标题/状态、计划指纹和四类分组精确目标。
- `POST /api/v1/delete/tasks/{taskId}`：接收预览指纹及四个独立布尔值，成功返回 `202 Accepted`、状态 URL 和 execution ID。
- `GET /api/v1/delete/executions/{executionId}`：返回执行状态、失败码、尝试次数和逐项目标状态。

空选择返回 400，不存在返回 404，过期指纹和同任务活动计划返回 409。所有端点沿用 `/api` Access-Key 中间件；响应不包含 qB 密码、Cookie、passkey 或 Torrent URL。

## WebUI

下载任务卡片只提供“删除…”入口。对话框加载服务端预览后按业务记录、qB 任务、下载源文件、媒体库文件分别列出数量和精确显示值；没有目标的选择不可勾选，至少选择一类后确认按钮才启用。确认只创建持久化 execution，不在浏览器中直接执行文件操作。

## 验收

```powershell
dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj --no-restore
node --check src/AnimeGoNet.App/wwwroot/app.js
```

结果：115/115 App 测试通过，JavaScript 语法检查通过。本机隔离数据目录启动 `http://127.0.0.1:6180/` 后，status 返回 schema v12，主页 HTTP 200 且包含删除对话框。浏览器自动化连接在本轮不可用，因此未把视觉点击声明为已通过；没有创建真实删除 execution，也未访问 qB/TestSpace。

全量 Release 回归为 Core 99 + Data 50 + App 115 = 264 项通过；`win-x64`、`.NET 10`、`PublishAot=true` 发布成功，0 warning/0 error。
