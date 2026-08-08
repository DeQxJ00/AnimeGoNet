# SQLite JSON Cache

AnimeGoNet 使用 SQLite schema v22 的 `cache_buckets` 与 `cache_entries` 取代 Go/bbolt。新程序不直接解析旧 `.bolt` 二进制；旧数据由仓库内的只读 Go 导出器转为 schema-v1 JSON，再由独立 .NET 导入器写入。

## 数据边界

- `database_name` 只允许 `bolt` 和 `bolt_sub`，两者的 bucket/key 完全隔离。
- bucket 可为空并独立存在；entry 主键为 `(database_name, bucket_name, key)`。
- value 必须是有效 JSON，最大 8 MiB UTF-8；存储层不使用运行时类型反射。
- TTL 为绝对 UTC 过期时间。写入的 TTL 大于零时以同一批次时间计算；零、负数或省略表示不过期。
- `GetJsonAsync` 与 `ListKeysAsync` 会在事务内先删除已经到期的行；`PurgeExpiredAsync` 用于批量清理。
- batch 在打开写事务前验证全部 key/value，因此任一 JSON 无效时不会创建 bucket 或写入部分数据；重复 key 保留批次中最后一项。

## 兼容 API

以下端点保持上游 HTTP 200 + `code/msg/data` envelope，并受统一 Access-Key 中间件保护：

- `GET /api/bolt?db=bolt&type=bucket`
- `GET /api/bolt?db=bolt&type=key&bucket={bucket}`
- `GET /api/bolt/value?db=bolt&bucket={bucket}&key={key}`
- `DELETE /api/bolt/value?db=bolt&bucket={bucket}&key={key}`

`db` 缺省为 `bolt`。读取支持 `bolt_sub`；与上游一致，删除只允许 `bolt`。返回的 `ttl` 是绝对 Unix 秒，永久值为 `0`。删除不存在的 key 仍成功，以保持幂等。

兼容删除只操作 cache entry，不级联删除 AnimeGoNet 的规范作品、下载或完成记录。缓存 value 不应写入 passkey、Cookie、下载器密码或 API key。

## 现代安全浏览 API

本地管理页不调用会返回原始 key/value 的兼容读取接口，而使用 Access-Key 保护的：

- `GET /api/v1/cache/buckets?database=bolt|bolt_sub`
- `GET /api/v1/cache/entries?database=...&bucket_id=...&page=1&page_size=25`
- `DELETE /api/v1/cache/entries/{entry_id}`

响应只包含 bucket/key 的不可逆 SHA-256 ID、条目数、`value_json` UTF-8 字节数、过期和更新时间。原始 bucket、key、JSON value、SQLite 文件路径及配置凭据都不会进入浏览器响应或 DOM。页面因此是受限缓存视图，不是任意 SQL/业务表浏览器。

删除请求体携带 database、opaque bucket ID 和删除 token。token 绑定当前原始 key、value、TTL 与更新时间；服务端在单个 SQLite 事务内重新解析 ID、固定时间比较 token，并使用全部原值做条件删除。条目在列表读取后发生任何变化会返回 `cache_entry_changed`，不存在返回 `cache_entry_not_found`。只有 `bolt` 可删除；`bolt_sub` 固定返回 `cache_namespace_read_only`。所有删除都只影响一条 `cache_entries` 记录，不级联业务表、文件或下载器任务。

## 旧 Go 缓存迁移

完整操作步骤见 [LEGACY_DATA_MIGRATION.md](LEGACY_DATA_MIGRATION.md)。迁移边界固定如下：

- `bolt.db` 只导出 `bangumi`、`mikan`、`themoviedb`、`hash2entity`、`name2hash`；`bolt_sub.db` 只导出 `bangumi_sub`。其他 bucket 计数后忽略，不把未知插件数据伪装成内置数据。
- Go key 的原始 JSON 和 value 的 JSON payload 保持不变；value 前 8 字节的小端绝对 Unix TTL 转为 SQLite 绝对 UTC。导入时已经过期的 entry 只计入报告，不写 SQLite。
- JSON 包最大 64 MiB、最多 50000 entries、key 最大 4096 UTF-8 bytes、单 value 最大 8 MiB。未知字段、重复 database/bucket/key、跨 namespace bucket、损坏 JSON 或非法 TTL 都会在写事务前拒绝整包。
- schema v39 的 `legacy_cache_imports` 只记录内容 SHA-256、固定格式版本、上游 commit、计数和时间，不记录 bucket/key/value。首次导入把全部 bucket/entry 与审计行放在同一个 IMMEDIATE 事务；相同语义包再次导入只增加 `repeat_count`，不会覆盖导入后产生的新缓存值。
- 导出器只读打开 Bolt，使用新临时文件原子发布 JSON，并拒绝覆盖已有输出；导入器只接受已经存在的 AnimeGoNet `data_path/animegonet.db`，避免路径写错时创建一套假数据。
