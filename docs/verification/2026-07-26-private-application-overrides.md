# 私密应用配置覆盖验收

日期：2026-07-26

## 持久化与启动

- 文件固定为 `data_path/config/application.private.json`，并由 `*.private.json` Git 忽略规则兜底。
- source-generated JSON，不使用运行时反射。
- 同目录随机临时文件、flush、原子替换；Unix 临时文件权限设为 `0600`。
- 全局 revision 乐观并发；revision 不一致返回 `configuration_revision_conflict`，不会覆盖其他管理员的修改。
- revision 1 之后的覆盖与恢复会先把当前文件原子备份到 `data_path/backups/application.private.revision-{20位revision}.json`；同 revision 已存在不同内容时拒绝修改当前配置。
- 启动时先加载应用覆盖，再执行统一强类型校验并创建 TMDB/Torrent 服务；当前进程保留 `applied_configuration_revision`，保存 revision 不同则明确 `restart_required=true`。

## 可写范围

- TMDB origin、语言、HTTP timeout、API key、read access token。
- 季度失败链四个独立开关。
- 一个统一的任务级 AI 元数据开关及 HTTP timeout；旧季度/EP 双键只作兼容读取。
- Bangumi 完全兜底和 Mikan 可信 offset 缓存开关。
- Torrent HTTP timeout、最大响应、最大跳转、暂存 TTL。
- 路径、Access-Key、监听地址和后台 worker 不允许通过该文件改写，继续由部署配置控制。

## 密钥三态

- 请求不传新密钥且不设置 clear：保留已有私密覆盖；没有覆盖时继续继承部署值，不复制部署 secret。
- 传入非空密钥：替换私密覆盖。
- `clear_tmdb_* = true`：明确覆盖为空，即使部署层有值也在重启后关闭对应凭据。
- 同时传密钥和 clear、换行或超长密钥、带 userinfo 的 TMDB URL 均在写文件前拒绝。
- GET/PUT/DELETE 响应均不返回密钥内容。

## 保存前预览

- `POST /api/v1/config/preview` 与 PUT 复用相同的 revision、环境锁、字段规范化和强类型校验，但不写文件、不增 revision。
- 响应只返回发生变化的字段以及 `restart`/`hot_reload` 效果；TMDB API Key/Read Token 的 before/after 只允许三态文字，不返回值。
- WebUI 必须先成功预览才允许确认保存；任一输入变化使旧预览和待提交对象失效。

## 自动验收

- `ApplicationOverrideStoreTests`：原子保存/重载/删除、revision 冲突、不可变 revision 备份、备份冲突 fail-closed、无临时残留、启动应用并验证覆盖已在客户端构造前生效。
- `ConfigurationApiTests.PrivateConfigurationUsesRevisionAndSecretTriState`：保存、待重启 revision、保留、清除、冲突、恢复部署默认和响应脱敏。
- `ConfigurationApiTests.PreviewValidatesAndReturnsRedactedEffectAwareDiffWithoutWriting`：预览的效果分类、密钥脱敏、零文件写入及 stale revision 冲突。
- `ConfigurationApiTests.OverwriteAndResetReportAndPersistPreviousRevisionBackups`：覆盖和恢复都返回并持久化旧 revision 备份。
- `ConfigurationApiTests.InvalidPrivateConfigurationDoesNotWriteSecretFile`：无效 URL 与冲突密钥输入返回 400，revision 保持 0 且文件不存在。
- 解决方案全量 434 passed（Core 169、Data 69、App 196）。
- `win-x64` NativeAOT 发布成功；隔离产物 PUT 后返回 revision 1，GET 返回 `restart_required=true`、applied revision 0。
