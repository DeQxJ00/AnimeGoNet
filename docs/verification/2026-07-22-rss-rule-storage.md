# Mikan RSS 规则持久化（2026-07-22）

## 范围

schema v13 新增 `mikan_rss_rule_sets`、`mikan_rss_priority_groups`、`mikan_rss_match_arrays`、`mikan_rss_match_values`：

- whitelist、blacklist 和 priority array 使用统一强类型结构，但 scope/group FK 约束防止混放；
- group、array、value 都有显式 position，数据库唯一索引保持用户排序；
- array ID 在同一 profile 全局唯一，group ID 独立唯一；ID 规范为 lowercase ASCII slug；
- 匹配 value 保存前 trim + invariant lowercase + 去重，SQLite CHECK 再拒绝非小写英文；展示 name 只 trim，不参与匹配；
- 全快照保存要求 `expected_revision`，失败事务不改变现有规则；
- Mikan 默认 720p blacklist 和四个 priority group 只在首次启动写入，重启不会覆盖用户编辑。

本模块只关闭规则持久化边界。`/api/rss` XML 获取/解析、来源 EP 解析和批次 loser 门禁仍未完成，运行能力不得据此宣称 RSS 流水线已闭环。

## 验收

```powershell
dotnet test tests/AnimeGoNet.Core.Tests/AnimeGoNet.Core.Tests.csproj --no-restore
dotnet test tests/AnimeGoNet.Data.Tests/AnimeGoNet.Data.Tests.csproj --no-restore
```

Core 104 项、Data 53 项通过。新增测试覆盖 ID/value 规范化、跨 scope 重复 ID 拒绝、默认初始化幂等、编辑后重启不覆盖、顺序/禁用状态 round-trip、revision 冲突和数据库 lowercase 约束。

全量 Release 回归为 Core 104 + Data 53 + App 115 = 272 项通过；`win-x64`、`.NET 10`、`PublishAot=true` 发布成功，0 warning/0 error。
