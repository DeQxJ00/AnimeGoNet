# 旧 AnimeGo 数据迁移

本流程只迁移上游 Go AnimeGo 的已知缓存和已经存在于媒体目录的 JSON sidecar。AnimeGoNet 不在 .NET 进程中读取 Bolt，也不执行旧 Python/JavaScript 插件。

## 迁移前

1. 停止旧 AnimeGo 与 AnimeGoNet，确保导出时的两个 Bolt 文件和导入时的 SQLite 都没有业务写入。
2. 备份旧 `data/cache/bolt.db`、`data/cache/bolt_sub.db`、整个媒体库，以及新 `data_path/animegonet.db`。旧部署修改过 `data_path` 时，以旧配置中的实际路径为准。
3. JSON 导出包可能包含旧缓存 key/value；把它按凭据文件处理，不提交 Git、不上传 issue，迁移完成并确认后再自行安全删除。

## 1. 导出已知 Bolt bucket

在 `tools/legacy-cache-exporter` 目录运行：

```powershell
go run . --bolt "D:\old-animego\data\cache\bolt.db" --bolt-sub "D:\old-animego\data\cache\bolt_sub.db" --output "D:\migration\animego-cache.json"
```

两个输入可只提供一个，但 `--output` 必须是尚不存在的文件。工具以只读方式打开旧库，只识别：

| 旧库 | bucket |
|---|---|
| `bolt.db` | `bangumi`、`mikan`、`themoviedb`、`hash2entity`、`name2hash` |
| `bolt_sub.db` | `bangumi_sub` |

输出成功时 stderr 只显示 bucket/entry/未知 bucket 数量，不打印 key、value、URL、Cookie、passkey 或 API key。损坏的已知 entry 会让导出整体失败，输出文件不会发布。

## 2. 导入 AnimeGoNet SQLite

先用目标配置启动 AnimeGoNet 一次，让它创建并迁移 `data_path/animegonet.db`，随后停止主程序。正式 NativeAOT artifact 已附带同 RID 的导入器；在解压目录运行：

```powershell
.\AnimeGoNet.LegacyCacheImporter.exe --data-path "D:\AnimeGoNet\data" --input "D:\migration\animego-cache.json"
```

Linux/macOS 使用同名无 `.exe` 文件。源码开发环境也可在仓库根目录运行：

```powershell
dotnet run --project tools\AnimeGoNet.LegacyCacheImporter -- --data-path "D:\AnimeGoNet\data" --input "D:\migration\animego-cache.json"
```

成功报告只包含 `status`、内容 SHA-256、上游 commit、bucket/entry/过期计数、导入时间和重复次数，不返回原始 key/value。再次导入相同语义包会返回 `status=already_imported` 并增加 `repeat_count`，不会把后来由 AnimeGoNet 更新的 entry 恢复成旧值。

导入是整包事务：任何验证或 SQLite 写入失败都不会留下部分 bucket、entry 或成功审计。

## 3. 迁移媒体目录 JSON

上游的 `anime.a_json`、`anime.s_json` 和 `*.e_json` 属于媒体目录，而不是 Bolt。保留媒体文件与这些 sidecar 的原有相对位置，把整个作品目录放到 AnimeGoNet 配置的 `save_path`。启动时和每日目录刷新会扫描它们并重建 SQLite 索引；不要把旧 `data_path`、Bolt 文件或插件目录混入 `save_path`。

目录扫描拒绝项可在 WebUI 的目录数据库状态中查看。先对副本演练；确认作品、季度、Episode 数量和拒绝项符合预期后，再切换 Jellyfin 等消费者。

## 验收与回滚

- 启动 AnimeGoNet，确认状态中的 schema 至少为 v39，缓存浏览页只显示 opaque ID 和条目计数。
- 触发一次目录刷新，核对作品/季度/EP 数量；旧缓存只是加速数据，不代替规范 TMDB 完成记录。
- 保留旧程序与旧数据只读副本，直到新程序完成一次 RSS→qBittorrent→TMDB→整理闭环。
- 需要回滚时停止 AnimeGoNet，恢复迁移前备份的 `animegonet.db` 和媒体目录。不要尝试把 SQLite 反向写回 Bolt。
