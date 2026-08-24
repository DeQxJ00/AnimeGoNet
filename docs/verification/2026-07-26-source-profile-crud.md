# SourceProfile 版本化 CRUD 验证（2026-07-26）

## 已实现

- `GET/POST/PUT/DELETE /api/v1/sources` 管理 Mikan/U2 来源。
- 稳定小写 ID、编译期 adapter、已启用 qBittorrent 实例、四种文件策略和 Torrent Host 白名单均在写入前校验。
- 更新采用 `expected_revision` 乐观并发；adapter 不可变，历史 ingest task 固化原 revision 和 downloader ID。
- list/get 返回任务数、RSS batch 数、默认来源标志和时间；`move` 返回“不保留做种”的明确提示。
- 默认 `mikan` 不可删除；被 ingest task 或 RSS batch 引用的来源不可删除；无引用来源可按 revision 删除。
- 新来源自动取得独立的默认 RSS 规则集；Mikan adapter 同时初始化 legacy filter 配置。
- API DTO 全部登记到 source-generated `ApiJsonContext`，不依赖运行时反射序列化。

下载器连接仍由部署配置提供。本模块只允许来源绑定到现有已启用的 qBittorrent 实例；下载器 CRUD、凭据管理、连接测试和路由预览另行验收。

## 自动化验收

- `SourceProfileStoreAdminTests`：3 项，覆盖创建/更新/list、历史路由快照、stale revision、重复/缺失、引用和默认来源删除保护。
- `SourceProfileApiTests`：7 项，覆盖 API 创建、规则初始化、统一导入路由、更新提示、历史任务路由、冲突、list/get/delete 和非法 ID/adapter/downloader/策略/Host。
- 完整 Release 回归：Core 168 + Data 66 + App 157，共 391/391 通过。
- win-x64 NativeAOT publish：0 warning/0 error；发布二进制 smoke 返回 `native_aot=true`、schema 16、默认来源 `mikan`，并成功序列化来源列表。
