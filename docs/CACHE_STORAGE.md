# SQLite JSON Cache

AnimeGoNet 使用 SQLite schema v22 的 `cache_buckets` 与 `cache_entries` 取代 Go/bbolt。新程序不直接解析旧 `.bolt` 二进制；旧数据需要由可选导出器转为 JSON 后写入。

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
