# 2026-08-08 旧 Go 缓存迁移验收

## 范围

- 固定上游 `develop@c7475dfc55a374cd0dd08821bf17125dab1e3145` 的 `pkg/cache/bolt.go` wire format：JSON key、8-byte little-endian absolute Unix TTL、JSON value。
- 固定 `bolt` 的 `bangumi/hash2entity/mikan/name2hash/themoviedb` 与 `bolt_sub/bangumi_sub`，不解析未知 bucket 的业务含义。
- .NET 主程序不引用 bbolt；只有可选 Go 离线工具读取 `.db`，输出 schema-v1 JSON。

## 自动验证

- Go exporter tests：真实 bbolt 文件、已知/未知 bucket、raw JSON 与 TTL、损坏 value 整体失败、目标文件不覆盖。
- Data importer tests：六 bucket、JSON array/string key、object/array value、永久/未来/过期 TTL、跨 namespace/未知 bucket、重复 key、未知 JSON 字段、零部分写入。
- 相同内容但不同导出时间/外层空白得到相同内容 SHA-256；第二次导入只更新审计 `repeat_count`，且不会覆盖首次导入后写入的新缓存。
- schema v38→v39 定向迁移、严格审计约束、并发首次初始化、历史篡改/future schema fail-closed 都纳入现有 SQLite 套件。

## 当前结果

- `go test ./...`（显式 `GOOS=windows GOARCH=amd64`，避免宿主已有 Android 交叉编译环境）：3/3 passed。
- Data 定向 migration/import/reliability：40/40 passed。
- `eng/smoke-legacy-data-migration.ps1`：schema v39 首次启动、三条旧目录 sidecar 索引、首次/重复 cache 导入和主程序重启后六 bucket 保留均 passed；隔离临时目录已回收。
- `AnimeGoNet.LegacyCacheImporter` win-x64 NativeAOT publish：passed，0 AOT/trim warnings。
- 最终 Release build：0 warnings / 0 errors；完整 .NET：1428/1428；WebUI type-check + DOM/client tests：14/14；Go exporter：3/3。
- 同一最终 win-x64 NativeAOT publish 目录包含主程序与导入器；first-start、legacy YAML upgrade、AI metadata、legacy cache + directory-sidecar 四组发布 smoke 全部 passed，临时进程和目录均已回收。
