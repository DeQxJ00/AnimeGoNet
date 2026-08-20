# 通知与 Webhook

AnimeGoNet 的通知中心使用 SQLite 持久化渠道、业务事件和发送记录。业务状态变更先写入 `notification_events`，后台 Worker 再发送；通知服务不可用不会回滚导入、匹配、下载或整理。

## 事件

- `metadata_failed`：Series、Season 或 Episode 元数据流程最终失败。
- `metadata_other`：元数据完成，但至少一个视频进入 Other。
- `download_failed`：下载任务进入错误状态。
- `download_completed`：下载完成，可能仍在等待整理。
- `organization_completed`：文件整理、NFO和清理流程完成。
- `review_required`：Other重新适配完成，等待人工审核。
- `test`：WebUI显式测试；不受渠道启用状态和事件勾选限制。

每个渠道独立选择事件。发送失败记录在 `notification_deliveries`，包括 HTTP状态、`notification_timeout`、`notification_connection_failed`、`notification_configuration_invalid` 或 `notification_http_<status>`，不会自动伪装成业务失败。

## Bark（重点）

默认服务地址为 `https://api.day.app`，程序使用 JSON POST `https://api.day.app/push`。自建 Bark Server 可填写自己的 HTTP/HTTPS 根地址；如果地址已经以 `/push` 结尾则不会重复拼接。

必填项：

- 设备 Key：Bark App 注册得到的 Key，发送时放入 `device_key`。
- 服务/API地址：官方或自建 Bark Server。

可选项：

- `group`：通知分组，建议使用 `AnimeGoNet`。
- `sound`：Bark铃声名，例如 `birdsong`。
- `icon`：通知图标的 HTTPS URL。
- `url`：点击通知后打开的地址。
- `level`：`active`、`timeSensitive`、`passive` 或 `critical`。
- `badge`：应用角标数字。
- `copy`：复制到剪贴板的文本。
- 自动复制：发送 `autoCopy=1`，通常与 `copy` 一起使用。

WebUI会直接回填设备 Key，方便本地使用。导出配置、浏览器访问权限和数据目录权限需要由部署者自行保护。

## 其他渠道

- 通用 Webhook：POST JSON；支持额外请求头 JSON和正文模板。占位符 `{{event_json}}`、`{{title_json}}`、`{{body_json}}`、`{{task_id_json}}` 会替换为合法 JSON字面量。禁止覆盖 `Host` 和 `Content-Length`。
- Discord：填写 Incoming Webhook完整 URL，使用 `content` 发送标题和正文。
- Slack：填写 Incoming Webhook完整 URL，使用 `text` 发送。
- Telegram：服务地址默认 `https://api.telegram.org`，填写 Bot Token 和 Chat ID，调用 `sendMessage`。
- Server酱：服务地址默认 `https://sctapi.ftqq.com`，填写 SendKey，使用表单 `title/desp`。
- PushPlus：服务地址默认 `https://www.pushplus.plus/send`，填写 Token；`topic` 可空。

上述请求统一服从 AnimeGoNet全局选择性 HTTP代理设置，并进入“外部 HTTP”运行日志。单次请求超时为30秒，响应摘要最多保存约2 KiB。

## API

- `GET/POST /api/v1/notifications/channels`
- `PUT/DELETE /api/v1/notifications/channels/{id}`
- `POST /api/v1/notifications/channels/{id}/test`
- `GET /api/v1/notifications/deliveries?limit=100`

测试发送使用真实网络，但不会创建或修改下载任务。删除渠道后历史发送记录保留渠道名称、类型和结果。
