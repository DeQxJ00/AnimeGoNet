# 下载器实例管理投影与连接测试（2026-07-26）

- `GET /api/v1/downloaders` 从部署配置投影全部命名实例，返回类型、安全 Base URL、下载路径、启用状态、凭据是否配置、SourceProfile/导入任务/下载任务计数和最近健康状态；不返回用户名或密码。
- `POST /api/v1/downloaders/{id}/test` 对已启用实例执行 qBittorrent Cookie 登录和任务列表读取，返回耗时、任务数及稳定失败码，并更新 `downloader_runtime_state`。
- 认证、网络/HTTP和超时失败使用脱敏消息；未知实例 404，停用实例 409。
- 静态 TypeScript 页面展示多实例状态、路径、引用和任务计数，并提供显式“测试连接”按钮。

定向 API/静态页面测试覆盖成功、认证失败、未知实例、状态持久化和密码负向断言。真实本机 qB 环境仍只由显式 LocalIntegration 测试使用，本模块测试全部使用 fake。

完整 Release 回归为 Core 168 + Data 66 + App 162，共 396/396 通过。TypeScript strict/build 和生成 JavaScript 语法检查通过。win-x64 NativeAOT 发布为 0 warning/0 error；首次 restore 只因 NuGet 漏洞数据端点超时失败，随后在锁定依赖不变且 `NuGetAudit=false` 下重新生成资产并完成原生编译。
