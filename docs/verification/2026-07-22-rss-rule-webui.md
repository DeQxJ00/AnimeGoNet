# Mikan RSS 规则 WebUI（2026-07-22）

## 功能

主页新增无框架 TypeScript 规则管理区：

- 显示当前 revision、旧 MikanTool 过滤开关和批次优选开关（本阶段只读）；
- whitelist/blacklist 具名数组可新增、删除、上下移动、启停，并编辑稳定 ID、展示名和逗号分隔 values；
- priority group 可新增、删除、上下移动；组内数组同样支持完整编辑和排序；
- 保存提交完整 `expected_revision` 快照；409 冲突明确要求重新载入，不在客户端静默覆盖；
- 批次预览按每行一个 title，显式输入 mikanid、来源类型和来源 EP，展示名单拒绝、winner/loser、原因和实际执行组。

所有服务端返回内容只以 `textContent`/表单 value 渲染，不作为 HTML 注入。页面不接收 passkey、Cookie、qB 凭据，也不抓取 Torrent。

## 验收

```powershell
npm run web:check
npm run web:build
node --check src/AnimeGoNet.App/wwwroot/app.js
dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj --no-restore
```

TypeScript strict 检查、确定性编译和 JavaScript 语法检查通过，App 119 项通过。静态页面契约测试确认规则容器、保存 revision 和服务端 preview 入口随主程序发布。浏览器自动化连接仍不可用，因此未将视觉点击 E2E 标为完成。

全量 Release 回归为 Core 104 + Data 53 + App 119 = 276 项通过；`win-x64`、`.NET 10`、`PublishAot=true` 发布成功，0 warning/0 error。
