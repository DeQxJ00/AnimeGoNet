# 运行配置 Web 编辑器验收

日期：2026-07-26

## 编辑模型

- 配置面板增加“编辑覆盖”“恢复部署默认”和刷新操作。
- 表单覆盖 TMDB、安全密钥三态、季度失败链、AI/兜底/offset 与 Torrent 限制。
- 密钥输入始终为空；只显示 `inherit/configured/cleared` 状态，不把服务端 secret 放入 DOM。
- 保存携带 `expected_configuration_revision`；409 时提示刷新，不做 last-write-wins。
- 页面明确所有改动需要重启，并展示 saved/applied revision。

## 期望值与部署基线

- 应用启动时保留未应用私密覆盖的 `DeploymentConfigurationOptions`。
- GET 的 `metadata`/`torrent_fetch` 是当前进程生效值；`editable` 是“部署基线 + 当前持久化覆盖”的待应用值。
- PUT 校验同样以部署基线为底，避免在保存后未重启时把旧 applied override 再次固化。
- DELETE 后 `editable` 立即恢复真实部署基线，即使当前进程仍在使用旧 applied revision。

## 自动验收

- TypeScript `web:check` 与 `web:build` 通过，发布 `app.js` 同步生成。
- 静态 Kestrel 测试验证 dialog/form/密钥 clear 控件、保存/恢复函数、revision 字段和零 `innerHTML`。
- API 测试验证待应用密钥状态、AI 编辑值、clear 状态及删除后的 `inherit`/部署 TMDB origin。
- 启动测试验证部署基线服务与已应用私密选项彼此独立。
- 解决方案全量 434 passed（Core 169、Data 69、App 196）。
- `win-x64` NativeAOT 发布成功，editable DTO 与静态配置 dialog 均进入发布产物。
