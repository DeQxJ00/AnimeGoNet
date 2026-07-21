# 可审计四类删除计划（2026-07-22）

## 范围

本阶段只实现删除前的只读预览和持久化计划，不执行 qBittorrent 或文件系统删除。

- 业务记录：按任务文件已验证的 `(TMDB Series, Season, Episode)` 精确解析 `completion_records.id`。
- 下载器任务：冻结命名下载器实例和小写 info hash；不包含 qB 凭据。
- 下载源文件：只取已经持久化的 `file_operations.source_path`，同时保存任务接收 qB 时捕获的 `download_root_path`。
- 媒体库文件：只取已经持久化的 `file_operations.target_path`，同时保存任务接收 qB 时捕获的 `save_root_path`。

四个选择彼此独立。预览目标按稳定顺序计算 SHA-256；确认创建计划时在同一 SQLite 事务重新读取并校验指纹，拒绝过期预览。同一任务只能存在一个 `pending/executing` 计划。

## 验收

```powershell
dotnet test tests/AnimeGoNet.Data.Tests/AnimeGoNet.Data.Tests.csproj --no-restore
```

结果：50/50 通过。测试覆盖四类目标分离、根目录快照、选择性冻结、空选择/过期指纹拒绝，以及单任务活动计划唯一约束。测试数据库和路径均位于临时目录；未访问本机 qBittorrent，也未删除真实文件。

## 后续执行边界

执行器必须只消费 `delete_execution_items`，不得执行临时重新发现的路径。qB 任务删除必须固定 `deleteFiles=false`；源文件和媒体文件由独立路径约束执行器处理。业务完成记录只在所选外部动作成功后删除，并同步释放对应已完成 Episode claim，使该单集可重新导入而不影响其他 EP。
