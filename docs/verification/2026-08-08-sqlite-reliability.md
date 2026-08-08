# 2026-08-08 SQLite 迁移与恢复验收

## 变更

- migration 定义在执行前校验版本连续、名称唯一、SQL 非空。
- 已应用历史必须是当前编译期迁移的精确前缀。
- 每个版本使用 SQLite immediate 事务，锁内重查后原子提交 DDL 与 history。
- 历史异常和数据库版本过新使用不泄露内容的稳定错误码 fail closed。

## 专项结果

```text
dotnet test tests/AnimeGoNet.Data.Tests/AnimeGoNet.Data.Tests.csproj \
  -c Release --no-restore \
  --filter FullyQualifiedName~SchemaReliabilityTests

Passed: 5, Failed: 0, Skipped: 0
```

覆盖 8 个独立连接并发首次启动、故障 DDL 回滚与修复续跑、迁移名称篡改、历史缺口和 future schema。

## 完整门禁

```text
dotnet test AnimeGoNet.slnx -c Release --no-restore
Passed: 1410, Failed: 0, Skipped: 0

npm run web:test
Passed: 14, Failed: 0

dotnet build AnimeGoNet.slnx -c Release --no-restore
0 warnings, 0 errors

dotnet publish ... -r win-x64 -p:PublishAot=true
Generating native code: passed

eng/smoke-native.ps1
first-start schema v38: passed
legacy-yaml-upgrade schema v38: passed

eng/smoke-native-metadata.ps1
Native AI metadata smoke: passed
```
