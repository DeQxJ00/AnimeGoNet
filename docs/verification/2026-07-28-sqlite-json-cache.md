# SQLite JSON cache 与 Bolt 兼容 API（2026-07-28）

## 实现

- schema v22 新增严格表 `cache_buckets`、`cache_entries`、复合外键和部分 TTL 索引。
- `SqliteJsonCacheStore` 提供 bucket、单项/批量 JSON upsert、读取、列举、幂等删除和全局过期清理。
- `bolt` 与 `bolt_sub` 命名空间隔离；兼容 API 允许读取两者，但只允许删除 `bolt`。
- `/api/bolt*` 使用 source-generated JSON 返回旧 `code/msg/data` envelope，不引入反射序列化。

## 定向验收

- Data 定向 20/20：其中缓存行为 7 项，另含现有 schema/migration 套件 13 项；覆盖同 bucket 多连接并发写入。
- App 4/4：bucket/key 排序、对象 JSON、绝对 Unix TTL、幂等删除、缺失 code 300、archive 只读和 Access-Key。
- 所有 SQLite 测试均使用系统临时目录，不读取 TestSpace、旧 `.bolt`、qBittorrent profile 或任何本地凭据。

## 完整门禁

- `dotnet test AnimeGoNet.slnx --no-restore`：559/559 通过（Core 204、Data 99、App 256）。
- `dotnet publish ... -r win-x64 --self-contained true /p:PublishAot=true --no-restore`：通过，无 trim/AOT warning。
- `eng/smoke-native.ps1`：发布后的原生进程返回 schema v22、`native_aot=true`，`/api/bolt?type=bucket` 返回兼容 envelope；进程和隔离临时目录已回收。
