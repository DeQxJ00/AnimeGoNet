# AnimeGoNet 部署配置

AnimeGoNet 的部署配置真相源是 YAML，默认位于
`data_path/animego.yaml`。首次启动使用 `CreateNew` 原子创建带注释的
`version: 1.7.1` 配置；不会覆盖已存在文件。Unix 新文件权限为 `0600`，编码为无
BOM UTF-8。

业务状态、规则、动画、下载和整理记录保存在 SQLite。WebUI 写入
`data_path/config/*.private.json`，不会展示或改写原始 YAML，避免丢失注释或把
secret 回显到浏览器。

## 选择配置文件

按以下顺序选择配置文件：

1. `ANIMEGO_CONFIG`
2. `--config <path>` 或 `--config=<path>`
3. 有效 `data_path` 下的 `animego.yaml`

相对路径以应用发布目录 `AppContext.BaseDirectory` 为基准，不依赖当前工作目录。
配置文件必须是 1～1048576 字节的严格 UTF-8、单文档 YAML mapping；最大深度 32、
最多 4096 个节点。重复键、非标量 mapping key、多个文档和不支持的版本会使启动
失败。错误不回显配置值。

## 覆盖优先级

最终优先级从高到低为：

1. 命令行参数
2. 环境变量
3. WebUI 私有覆盖文件
4. 部署 YAML
5. 编译期安全默认值

命令行和环境变量锁定的应用字段在 WebUI 中显示为只读；下载器命令行或环境变量
字段也会在私有下载器覆盖应用后重新生效，私有文件不能盖过部署锁。

下载器部署锁按实例和字段独立计算，支持 `type`、`base_url`、`username`、
`password`、`download_path`、`enabled`。`GET /api/v1/downloaders` 的每个实例
通过 `locked_fields` 返回字段、来源和控制键名；只返回环境变量/命令行参数名，
绝不返回对应值。WebUI 会逐字段禁用编辑并显示这些来源。保存同一实例的其他未锁
字段时，API 不会把环境变量或命令行中的用户名、密码复制到
`data_path/config/downloaders.private.json`。若请求确实改变锁字段，则返回
`400 downloader_field_locked`，并保持全局配置 revision 不变。

规范控制键示例：

```text
downloaders__bt__base_url
downloaders__bt__username
downloaders__bt__password
downloaders__bt__download_path
--downloaders:bt:enabled=true
```

兼容的旧 `ANIMEGO_CLIENT*` 控制键只锁定 `bt` 实例的对应字段。命令行和值同时
存在时，`locked_fields.source` 为 `environment_and_command_line`；WebUI 私有
覆盖始终低于两者。

环境变量的嵌套键使用 .NET 双下划线格式，例如：

```text
downloaders__bt__base_url=http://127.0.0.1:8080/
downloaders__bt__username=admin
downloaders__bt__password=...
downloaders__bt__download_path=E:\AnimeGoNet\download
```

兼容的扁平变量包括 `data_path`、`download_path`、`save_path`、
`tmdb_base_url`、`tmdb_proxy_url`、`tmdb_api_key`、
`ANIMEGO_THEMOVIEDB_KEY`、`bangumi_base_url`、`bangumi_proxy_url`、
`ANIMEGO_CLIENT_URL/USERNAME/PASSWORD/DOWNLOAD_PATH` 和
`ANIMEGO_CATEGORY`。推荐新部署优先使用规范嵌套键。

不要把密码、Access Key、TMDB/AI key、Cookie、passkey Torrent URL 或真实配置
文件提交到 Git。仓库和 CI 只使用空 secret、fake transport 或隔离测试凭据。

## 规范 YAML

```yaml
version: 1.7.1

paths:
  data_path: 'E:\AnimeGoNet\data'
  download_path: 'E:\AnimeGoNet\download'
  save_path: 'E:\AnimeGoNet\library'

web:
  access_key: ''
  background_workers_enabled: true

downloaders:
  bt:
    type: qbittorrent
    base_url: http://127.0.0.1:8080/
    username: ''
    password: ''
    download_path: 'E:\AnimeGoNet\download'
    enabled: true

sources:
  mikan:
    adapter: mikan
    downloader_id: bt
    file_strategy: move
    allowed_torrent_hosts:
      - mikanani.me
    category: animegonet
    tags: []
    dynamic_tag_template: '{year}年{quarter}月新番'
    seeding_time_minutes: 0
    rss_filter_enabled: true
    rss_priority_enabled: true

metadata:
  tmdb:
    base_url: https://api.themoviedb.org/
    proxy_url: ''
    api_key: ''
    read_access_token: ''
    language: zh-CN
    timeout_seconds: 30
  bangumi:
    base_url: https://api.bgm.tv/
    proxy_url: ''
    timeout_seconds: 30
  season_failure:
    skip: false
    backtrace: false
    use_title_season: false
    use_first_season: false
  tmdb_failure_use_bangumi: false
  mikan_trusted_offset_cache_enabled: false
  ai:
    provider: openai_compatible
    base_url: ''
    api_key: ''
    model: ''
    use_metadata_match: false
    timeout_seconds: 600
    retry_count: 2
    use_bangumi_pubdate_first: true

torrent_fetch:
  timeout_seconds: 30
  max_response_bytes: 16777216
  max_redirects: 3
  staging_ttl_seconds: 900

schedule:
  refresh_database_cron: '0 0 6 * * *'

data_update:
  enabled: false
  cron: '0 0 4 * * ?'
  manifest_url: ''
  auto_download: true
  auto_import: true
  keep_versions: 2
  timeout_seconds: 300
```

`downloaders` 和 `sources` 是按 ID 命名的 mapping。不同来源通过
`downloader_id` 绑定不同 qBittorrent 实例。所有下载器 `download_path` 必须位于
全局 `paths.download_path` 内。Mikan 默认整理语义固定为 `move`，因此做种分钟
必须为 0。

`dynamic_tag_template` 留空即关闭；默认 Mikan 值与上游一致。支持 `{year}`、
`{quarter}`、`{quarter_index}`、`{quarter_name}`、`{ep}`、`{week}`、
`{week_name}`，逗号分隔多个 qB tag。模板在任务创建时随 SourceProfile revision
冻结，只在 TMDB 元数据确认后的暂停下载准备阶段展开；不会把 passkey、凭据或
未展开模板发送到 qB。

季度失败链按 P4 → P3 → P2 → P1 逐级执行；四项均默认关闭。P3 需要 bgmid，并按
Bangumi 前作名字和开播日期重新验证 TMDB Series+Season；P2 只从统一导入任务
title 本地解析季度，P1 本地固定 S01，P2/P1 不调用 TMDB Season 验证。AI 是独立的
一次任务级 Series/Season/Episode 流程，默认关闭，结果必须再经 TMDB 验证，HTTP
默认超时 600 秒。

## Docker 路径契约

官方容器固定：

```yaml
paths:
  data_path: /data
  download_path: /download/incomplete
  save_path: /download/anime
downloaders:
  bt:
    download_path: /download/incomplete/bt
  pt:
    download_path: /download/incomplete/pt
```

AnimeGoNet 和 qBittorrent 必须把同一个宿主共享父目录映射到相同容器路径
`/download`。不要分别映射成不同的容器内名称；否则 qB 返回的文件路径无法安全
整理。`/data` 必须单独持久化。

## 本机 TestSpace

本机显式集成测试使用：

```text
data_path     = E:\WorkSpaceAI\AnimeGoNet\TestSpace\animegonet_data
download_path = E:\WorkSpaceAI\AnimeGoNet\TestSpace\download_temp
save_path     = E:\WorkSpaceAI\AnimeGoNet\TestSpace\jellyfin_data
```

本地 qBittorrent 只使用
`E:\WorkSpaceAI\AnimeGoNet\TestSpace\qbittorrent\qbittorrent.exe` 及其隔离
profile。二进制、profile、下载内容、Cookie、凭据、passkey 和生成的 YAML/SQLite
均不得提交。真实投递必须通过显式 integration 参数，并使用可识别 category/tag、
暂停任务、安全本地 tracker 和 finally 清理；默认单元测试与 CI 不启动本机 qB。

## 旧版配置

当前识别 `1.1.0`～`1.7.1`。旧 `setting:`/`advanced:` qBittorrent 配置会把
路径、连接凭据、Mikan 文件策略/category/做种时间、Access Key、TMDB key、
TMDB/Bangumi redirect、全局代理、请求超时、季度失败开关和刷新 Cron 映射到
规范键，并显式补入所有新增安全默认值。

默认升级顺序：

1. 严格解析原文件、验证版本和将要迁移的布尔/数字/文件策略；失败时原文件不变。
2. 以 `CreateNew` 在同目录写入原字节备份：
   `animego-<旧版本>-<UTC yyyyMMddHHmmss>[-NNN].yaml`。已存在名称永不覆盖，
   Unix 权限为 `0600`。
3. 在同目录写完并 flush 唯一临时文件，然后原子替换原路径。
4. 新文件固定 `version: 1.7.1`；再次启动不重复升级或备份。

和上游一致，备份默认开启。仅在已经自行制作等价备份时才可用
`--backup=false` 或 `ANIMEGO_CONFIG_BACKUP=false` 关闭。

旧 `setting.client.client` 为 Transmission 或其他非 qBittorrent 值时不会备份或
重写，下载 worker 继续 fail closed，WebUI 显示迁移诊断。必须由用户明确配置
qBittorrent，不能自动猜测。

Python/JavaScript 插件、旧缓存/日志路径和旧 Bolt 数据副作用不会进入新主程序；
它们只保留在原字节备份中。旧 `setting.tag` 是动态模板，会迁移到
`sources.mikan.dynamic_tag_template`；它不会进入静态 `SourceProfile.tags`，也不会
在 Torrent 初次投递时以未展开字面量发送。模板会随任务路由快照冻结，在元数据
确认后的下载准备阶段才渲染并写入 qB。
