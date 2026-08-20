# 通知中心与输入源导航验收

日期：2026-08-20

## 范围

- Mikan 五项功能归入“输入源 / Mikan”三级菜单；手动设置路由仍为 `#/sources/mikan-ingest`。
- 新增通知中心、Bark 详细配置、通用 Webhook、Discord、Slack、Telegram、Server酱和 PushPlus。
- 新增通知事件、发送记录和 SQLite schema v54。
- 可信 EP Offset 黑名单保持在“输入源 / Mikan / 可信 Offset”中。

## 自动验证

- `npm run web:check`：TypeScript strict 类型检查通过。
- `npm run web:test`：确定性生成 `wwwroot/app.js`，35/35 静态 WebUI 测试通过；随后 `node --check` 通过。
- App 定向测试：18/18 通过，覆盖通知发送器、通知 API、工作区导航和外部插件配置回填。
- Data 定向测试：32/32 通过，覆盖通知持久化、事件触发和 schema 迁移。
- Release solution build：0 warnings / 0 errors。
- win-x64 NativeAOT 发布成功；schema v54 隔离 first-start smoke 通过。smoke 使用专用合成插件 AccessKey，不读取本机真实凭据。

## 浏览器实测

在本机 `http://127.0.0.1:6180/` 实际加载编译后的静态 WebUI：

- 一级菜单不再出现独立的“Mikan 手动设置”。
- 打开“输入源”后二级菜单仅显示“输入源管理”和“Mikan”；点击 Mikan 后展开“手动设置、人工规则、可信 Offset、候选规则、五级过滤”。
- 点击“手动设置”后进入 `#/sources/mikan-ingest`，单 Torrent 和 RSS 手动功能可见。
- “通知”显示“通知渠道 / 发送记录”二级菜单；Bark 设备 Key、group、sound、icon、URL、level、badge、copy 和自动复制字段可见。

本轮未配置真实通知渠道，也未发送真实 Bark/Webhook；真实网络投递由用户填写目标后点击“发送测试”显式验收。
