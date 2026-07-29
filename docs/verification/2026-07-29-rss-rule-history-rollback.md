# RSS 有序规则历史与回滚验证

## 实现

- schema v25 新增 RSS 规则关系型快照：规则根、优先级组、具名数组和 lowercase values 均按 `source_profile_id + revision` 隔离。
- v24 → v25 迁移会把每个来源当前 revision 的完整顺序与值复制为第一份历史快照。
- 每次 `MikanRssRuleStore.SaveAsync` 在同一事务创建新 revision 快照；失败不会留下半份历史。
- `ListSnapshotsAsync` 倒序分页读取摘要；`RollbackAsync` 读取目标 revision 并通过正常保存路径创建新 revision。
- GET 规则 API 返回快照摘要，新增 rollback API 使用 `expected_revision` 防止覆盖并发修改。
- TypeScript WebUI 可选择历史 revision 并在明确确认后回滚；页面说明历史不会删除。

## 验收

针对性测试覆盖新库初始化快照、v24 数据迁移、顺序和值保留、快照倒序上限、目标不存在、stale revision、回滚创建新 revision、API 稳定错误以及静态 WebUI 入口。

## 本次结果

- `npm run web:check` / `npm run web:build`：通过，TypeScript strict 无错误且编译产物同步。
- `dotnet test AnimeGoNet.slnx --no-restore`：729 通过、0 失败、0 跳过（Plugin 11、Core 229、Data 118、App 371）。
- `win-x64` Release NativeAOT publish：通过，无 trim/AOT 警告。
- `eng/smoke-native.ps1 -ExpectedSchemaVersion 25`：通过 `/ping`、schema v25、SQLite 初始化、NativeAOT、qB capability、静态 WebUI 和安全 ingest 拒绝；隔离进程与临时目录已回收。
