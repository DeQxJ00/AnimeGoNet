# 来源证据与 TMDB 规范字段串联验收

日期：2026-08-08

## 行为

- `ingest_tasks` 已保存的 source profile/revision、adapter、来源标题、item/work ID、
  Mikan/group/Bangumi/AniDB/IMDb 与发布时间，由 `MetadataResolutionStore` 在同一聚合读取快照中
  生成 `MetadataTaskSourceProjection`。
- `GET /api/v1/metadata/tasks/{taskId}` 返回独立 `source_evidence`；TMDB Series/Season/Episode
  仍只读取解析 Run、`task_files` 与规范作品库投影。
- 任意不透明 item/work ID 在离开 Data 层前按
  `animegonet-source-id\0{source}\0{kind}\0{value}` 计算 lowercase SHA-256；API/WebUI 不返回原值。
- WebUI 将来源证据置于逐文件 TMDB 对照之前，并明确说明它不是 TMDB 规范字段。

## 定向验收

- Data store：验证全部来源字段、带命名空间指纹、带时区发布时间，以及没有伪造 TMDB Series/Season。
- API：验证来源对象与指纹，同时断言原始私有 source item ID、Torrent/Mikan URL、passkey、
  RSS candidate ID、batch/Torrent 指纹均不出现在响应正文。
- WebUI：TypeScript strict build 和 Node 契约验证标题、两个指纹字段及独立视觉区块。

## 最终门禁

- 带固定上游目录的全解决方案 Release tests：1405/1405；
- WebUI tests：14/14；
- Release build：0 warning / 0 error；
- win-x64 NativeAOT：成功生成原生代码；
- 发布二进制首次启动、legacy YAML upgrade、AI metadata smoke：三项均通过，schema v38；
- changed-file format、diff、敏感值扫描和双仓库 tree：提交同步前检查。
