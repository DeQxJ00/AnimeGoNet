# 已保存 Mikan RSS 手动触发验证

日期：2026-08-13

## 范围

- `POST /api/v1/sources/{source_profile_id}/rss/run` 使用 SourceProfile 已保存的 RSS URL。
- 自动调度关闭时仍允许显式执行；来源必须启用、adapter 必须为 Mikan 且 RSS URL 已保存。
- 手动执行与 Cron 共用来源级 `running` 门禁和最近运行 batch/失败审计。
- WebUI 在“Mikan 手动设置 / 导入任务”同时保留临时 URL 入口，并增加“执行已保存 RSS”。
- API、页面结果和错误不回显保存 URL 中的 passkey。

## 自动验证

- TypeScript `web:check` 与 `web:build`：通过。
- `SourceProfileStoreAdminTests`：12/12，通过自动调度关闭时的手动门禁与审计用例。
- `ManualSubmissionApiTests`、`SourceProfileApiTests`、`StaticWebUiTests`：184/184；保存 URL 的真实生产读取/解析/批次链由可注入 HTTP transport 验证，缺少保存 URL 时零网络请求。
- 前端 Node 测试：19/19。
- win-x64 NativeAOT 发布：通过，输出到 `artifacts/mybangumi-win-x64-native-v46-manual-rss`（Git 忽略）。
- 本机 NativeAOT WebUI：`http://127.0.0.1:6180/#/mikan/ingest`；浏览器确认“执行已保存 RSS”可见且在当前已配置 Mikan 来源下启用，说明明确“不要求开启自动调度”。验收未点击按钮，未访问私人 RSS、Torrent 或 qBittorrent。

该验证不使用用户私人 RSS，也不连接 qBittorrent；真实 RSS 与下载仍只由用户在 WebUI 中显式触发。
