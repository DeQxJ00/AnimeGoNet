# SourceProfile 静态 WebUI 验证（2026-07-26）

## 页面能力

- 首页“输入源”区域读取 `/api/v1/sources`，以列表展示 adapter、下载器、文件策略、revision、任务数和 RSS batch 数。
- 原生表单支持 Mikan/U2/TTG 创建、下载器绑定、四种文件策略、Host 白名单、启停和两个 RSS 开关。
- 编辑请求自动提交当前 `expected_revision`；冲突不覆盖服务端状态并提示重新选择来源。
- 默认 Mikan 的删除按钮禁用；其他来源删除仍由服务端执行 revision 与引用保护。
- `move` 始终显示移动后无法继续做种、仅影响新任务的提示。
- 所有动态内容通过 `textContent`/DOM 节点写入，不使用 `innerHTML`；页面无 Vue/React 等运行时框架。

## 验证

- `npm run web:check`：TypeScript 7 strict 检查通过。
- `npm run web:build` 后 `node --check src/AnimeGoNet.App/wwwroot/app.js` 通过，生成文件已提交。
- `SourceProfileApiTests` 的静态页面契约覆盖容器、表单、下载器/Host 字段、move 提示、revision 和 API 路径，来源 API/页面定向测试 8/8 通过。
- 完整 Release 回归：Core 168 + Data 66 + App 158，共 392/392 通过。
- win-x64 NativeAOT publish：0 warning/0 error；发布二进制 smoke 返回 `native_aot=true`，并同时提供来源表单 HTML、编译后的 `loadSources` 脚本和来源 API。
- 本机浏览器控制连接无法初始化，因此未把视觉点击和窄屏截图 E2E 标为完成；发布页面仍由 Kestrel contract 与 NativeAOT smoke 验证。
