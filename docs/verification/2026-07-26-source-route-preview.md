# SourceProfile 路由预览（2026-07-26）

- 新增 `POST /api/v1/sources/{id}/route-preview`，复用 `IngestCommandNormalizer` 的 Mikan/U2 字段规则。
- 返回 SourceProfile revision、adapter、下载器及启用状态、download/save path、文件策略、两个 RSS 开关和规则 revision。
- 预览不抓 Torrent、不写数据库任务、不连接下载器；无效输入返回逐项错误而非创建失败任务。
- 修复统一导入把自定义 SourceProfile ID 误当 adapter 的问题：现在先按 profile ID 取配置，再按其编译期 adapter 校验，最终任务仍保存自定义来源 ID。
- 静态 TypeScript 来源编辑器提供作品 ID、mikanid/bgmid/anidbid/imdbid 输入和安全预览结果。

定向测试覆盖自定义 `u2-anime` 的预览与实际 staging 路由一致、Mikan 缺身份字段、预览零任务副作用和静态页面契约。

TypeScript strict/build 和生成 JavaScript 语法检查通过。完整 Release 回归为 Core 168 + Data 66 + App 164，共 398/398 通过；win-x64 NativeAOT 发布 0 warning/0 error。
