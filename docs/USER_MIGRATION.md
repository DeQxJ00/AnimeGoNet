# AnimeGo / AnimeGoNet 用户迁移手册

本手册用于把旧 Go AnimeGo 或较早的 AnimeGoNet 部署迁移到当前 .NET 10
主程序。迁移始终以“停机、完整备份、隔离演练、验收后切换”为顺序；不要让旧程序和
AnimeGoNet 同时写同一个下载目录、媒体目录或数据库。

## 1. 先确认迁移类型

| 当前状态 | 需要执行 |
|---|---|
| 首次安装 AnimeGoNet | 创建独立 `data_path`，配置共享下载路径与 qBittorrent，再建立来源 |
| 旧 AnimeGo YAML 1.1.0～1.7.1，下载器为 qBittorrent | 让 AnimeGoNet 自动备份并原子升级 YAML，再检查迁移报告 |
| 旧 YAML 使用 Transmission | 人工改为 qBittorrent；程序会 fail closed，不会静默改成默认实例 |
| 需要保留旧 Bolt 缓存 | 按 [旧缓存与媒体数据迁移](LEGACY_DATA_MIGRATION.md)先导出 JSON，再离线导入 SQLite |
| 已在使用 AnimeGoNet | 备份完整 `data_path`，替换程序文件后启动；SQLite migration 自动按顺序执行 |

Python/JavaScript 插件不会迁移或执行。内置功能已经由编译期 C# 实现替换；第三方扩展
只能使用 [外部 C# 插件运维手册](PLUGIN_OPERATIONS.md)所述的进程包。

## 2. 停机与不可变备份

1. 停止旧 AnimeGo、AnimeGoNet、RSS 定时调用方和会自动提交 Torrent 的浏览器脚本。
2. 等待 qBittorrent 中正在写入的测试任务结束或保持暂停，记录每个来源绑定的实例。
3. 复制旧部署 YAML、旧 `data/cache`、AnimeGoNet 的完整 `data_path`、qBittorrent profile
   和媒体库 sidecar。不要只复制 `animegonet.db` 而遗漏同目录私有配置与备份。
4. 对备份生成 SHA-256 清单并保存到备份介质；清单和备份不得提交 Git。

`data_path` 是敏感数据：它可能包含自动 Mikan RSS URL、SQLite、下载器私有覆盖、外部
插件变量、缓存和日志。备份应使用与密码库相同的访问控制。

## 3. 在隔离目录演练

不要先改正式目录。把待迁移 YAML 复制到隔离目录，并使用新的三路径启动：

```powershell
dotnet run --project src/AnimeGoNet.App -- `
  --config E:\AnimeGoNet-Migration\animego.yaml `
  --data_path E:\AnimeGoNet-Migration\data `
  --download_path E:\AnimeGoNet-Migration\download `
  --save_path E:\AnimeGoNet-Migration\library `
  --web=false
```

默认 `--backup=true`。旧 qBittorrent YAML 在完整解析通过后，原始字节会先写入同目录的
版本化备份，再以规范 1.7.1 原子替换。不要在正式迁移时使用 `--backup=false`；该选项只
适合已经有外部不可变备份的受控演练。

启动失败时先读稳定错误码，不要反复修改原件。未知字段、伪版本、Transmission、路径
越界或无效下载器会在写备份/替换前或下载功能启用前 fail closed。

## 4. 核对生成配置和来源路由

迁移后的 YAML 至少应明确：

- `data_path`、`download_path`、`save_path`；
- 每个 `downloaders.<id>` 的 qBittorrent WebUI 地址、用户名、密码、下载路径和启用状态；
- 每个 `sources.<id>` 的 adapter、下载器绑定、文件策略、Torrent Host 白名单和规则开关；
- Mikan/TMDB/Bangumi/图片 API 地址、唯一全局代理 URL 与域名列表，以及默认关闭的高风险 fallback/AI 选项；
- Mikan 的默认 `file_strategy=move`。

不同来源可以绑定不同 qBittorrent 实例。任务创建时会冻结来源 revision 和下载器路由，
迁移后修改 profile 不会偷偷改写已有任务。容器或跨主机 qB 必须先按
[外部 qBittorrent 路径映射](EXTERNAL_QBITTORRENT.md)验证两端看到的是同一份文件。

旧 Mikan feed 的名称、私密 URL、Cron 和 enable 可迁入 SourceProfile/SQLite。私密 RSS
URL 只写，不会在 API、日志或 WebUI 中回显；因此迁移后应检查“已配置”状态，而不是寻找
原文。

## 5. 导入可选旧数据

Bolt 缓存不是规范业务数据库。需要时按
[LEGACY_DATA_MIGRATION.md](LEGACY_DATA_MIGRATION.md)使用独立 Go 导出器生成 JSON，再在
主程序停止时运行同 RID 的 `AnimeGoNet.LegacyCacheImporter`。同一语义包重复导入是幂等
的，不会覆盖 AnimeGoNet 后续更新的数据。

旧媒体目录中的 `anime.a_json`、`anime.s_json`、`*.e_json` 应与媒体保持原相对位置，放入
新的 `save_path` 后通过目录数据库刷新重建索引。不要把 Bolt、旧插件目录或旧 `data_path`
混入媒体库。

## 6. 切换前验收

1. 启动时没有 migration、schema 或 legacy downloader 阻断错误。
2. `GET /ping` 返回成功；带 Access Key 请求 `GET /api/v1/status`，检查
   `database_schema_version`、`native_aot`、来源、下载器和外部插件诊断。
3. WebUI 中逐一执行下载器连接/路径探测，确认路径映射后再启用后台 worker。
4. 核对 Mikan 来源路由预览进入预期 qB 实例并使用预期文件策略。U2 首版暂缓，
   不应为新安装创建默认来源；历史自定义 profile 只作为未来兼容数据保留。
5. 只用明确、合法、可清理的测试 Torrent 完成一次导入；不得借迁移测试触碰私人任务。
6. 核对 TMDB Series/Season/Episode、整理目标、NFO、字幕语言后缀和完成记录。

## 7. 切换与回滚

验收通过后停止演练实例，把正式路径/凭据应用到正式部署，再只启动一个 AnimeGoNet。
保留旧程序和备份为只读，至少直到完成一次正式 RSS→qBittorrent→TMDB→整理闭环。

需要回滚时：停止 AnimeGoNet，恢复同一时间点的完整 `data_path`、部署 YAML、qB profile
和被迁移的媒体目录，再启动与该备份 schema 匹配的程序版本。旧二进制不能直接打开已由
新版迁移的 SQLite；二进制回滚必须同时恢复升级前数据库。不要把 SQLite 反向写回 Bolt。
