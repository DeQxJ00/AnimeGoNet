# SQLite 存储可靠性矩阵

本文记录首版 SQLite 存储的自动验收边界。数据库使用显式 SQL、外键、WAL、busy timeout 和短事务；“已验证”只表示下列可重复测试覆盖，不等同于特定硬件/文件系统的断电持久性认证。

| 风险 | 实现门禁 | 自动证据 |
|---|---|---|
| 多实例同时首次启动 | 每个版本使用 immediate 写事务；锁内重新检查版本 | `SchemaReliabilityTests.ConcurrentFirstStartSerializesAndRecordsEachMigrationOnce`：8 个独立数据库实例并发，最终恰好 v1～v38 各一条，`PRAGMA integrity_check=ok` |
| migration 执行到一半失败 | DDL 与 migration history 同一事务 | `SchemaReliabilityTests.FailedMigrationRollsBackItsDdlAndCanResumeAfterRepair`：故障版本不留表、不留记录；修复后从上一成功版本续跑 |
| 历史被改名、挖空或来自新版应用 | 历史必须是编译期 migration 的精确前缀；新版数据库拒绝降级启动 | `SchemaReliabilityTests.InvalidOrNewerMigrationHistoryFailsClosed`：稳定错误码且异常不回显数据库中的篡改值 |
| schema 约束退化 | FK、唯一键、状态与阶段触发器必须拒绝非法组合 | `SchemaConstraintTests`、`SchemaMigrationTests` |
| KV 缓存并发、TTL 与重开 | upsert/batch 为事务，绝对到期时间，损坏/过期隔离 | `SqliteJsonCacheStoreTests` |
| RSS winner 与同集竞争 | immediate 事务复查 alias/claim，批次与任务审计持久化 | `MikanRssBatchStoreTests`、`CompletionRecordStoreTests` |
| 元数据 Run/Stage/Attempt 引用错配 | FK/触发器限制证据必须来自同一解析运行及正确阶段 | `MetadataResolutionStoreTests` |
| 下载准备、整理与崩溃重启 | 有时限租约、不可变逐文件计划、完成单位和 cleanup 分阶段持久化 | `DownloadPreparationStoreTests`、`MediaOrganizationStoreTests` |
| 数据包导入/回滚中断 | staging 完整验证后单事务切换 active/previous | `DataPackageStoreTests`、`BangumiArchiveStoreTests` |
| 删除执行部分失败 | 预览指纹冻结不可变目标，逐项状态可重试，业务记录最后删除 | `DeletePlanStoreTests` |
| 目录 sidecar 损坏或原子写残留 | 单文件隔离、临时文件不参与扫描、刷新事务替换索引 | `DirectoryDatabaseTests` |

## 明确不宣称

- 自动测试没有模拟突然断电、存储控制器写缓存丢失或损坏扇区，因此不宣称跨所有硬件的 power-loss/fsync 认证。
- WAL 不能代替备份。`data_path` 内数据库和相关备份仍需作为敏感持久数据纳入部署备份策略。
- 默认 CI 不连接用户的本机 qBittorrent；该真实环境只由显式 integration smoke 使用，且不把 profile、凭据或下载内容写入 Git。
