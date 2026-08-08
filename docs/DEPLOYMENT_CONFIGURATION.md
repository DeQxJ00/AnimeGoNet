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

## 主程序命令行

固定上游 `cmd/animego` 的四个开关全部保留，并同时接受 Go 风格单短横线和现代双横线：

```text
-config <path> / --config <path>
-debug[=true|false] / --debug[=true|false]
-web[=true|false] / --web[=true|false]
-backup[=true|false] / --backup[=true|false]
```

裸 bool 开关等价于 `=true`。`debug=true` 同时启用宿主和
`data_path/logs/animego.log` 的 Debug 级别；默认只记录 Information 及以上。
`web=false` 不绑定任何 TCP 端口，但仍启动已启用的后台 worker，适合只运行调度、
下载和整理的 headless 实例。`-h`、`-help` 或 `--help` 只打印帮助并以 0 退出，
不会创建 YAML、SQLite 或运行目录。非法 bool 值在任何运行目录写入前拒绝启动。

对应环境别名为 `ANIMEGO_CONFIG`、`ANIMEGO_DEBUG`、`ANIMEGO_WEB`、
`ANIMEGO_CONFIG_BACKUP`。AnimeGoNet 的统一覆盖约定仍是命令行高于环境变量；这是
为全部配置键保持一致的已记录差异，而不是复制上游在解析 flag 后再强制读取环境变量
的偶然顺序。

## 覆盖优先级

最终优先级从高到低为：

1. 命令行参数
2. 环境变量
3. WebUI 私有覆盖文件
4. 部署 YAML
5. 编译期安全默认值

同一逻辑字段存在旧扁平键与规范嵌套键时，先比较配置 Provider 层级，再比较同一
Provider 内的兼容别名。因此更高层的 `--data_path`、
`--downloaders:bt:base_url` 或 `--sources:mikan:category` 可以覆盖较低层的
`ANIMEGO_DATA_PATH`、`ANIMEGO_CLIENT_URL` 或 `ANIMEGO_CATEGORY`；旧别名不会因
代码中的排列顺序越过命令行层。显式空的可空字段也只屏蔽更低层值，不会意外回落。

命令行和环境变量锁定的应用字段在 WebUI 中显示为只读；下载器命令行或环境变量
字段也会在私有下载器覆盖应用后重新生效，私有文件不能盖过部署锁。

应用配置的 `editable.locked_fields` 同时保留 `environment_variables` 兼容字段，
并返回 `command_line_arguments`、统一的 `controlling_keys` 以及
`environment` / `command_line` / `environment_and_command_line` 来源。命令行只
投影参数名，不投影 `=` 后的 URL 或 secret。当前全部可编辑字段均参与部署锁：
TMDB/Bangumi 连接与重试、四档季度失败链、统一 AI 开关/超时、Bangumi 完全兜底、
可信 offset 缓存、Torrent HTTP/容量/redirect/staging 以及数据更新设置。锁定值
在读取 `application.private.json` 后重新应用；保存其他字段不会把部署值固化到
私有文件。

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

来源部署锁按 SourceProfile ID 和字段独立计算，当前支持 `category`、
`dynamic_tag_template`、`mikan_identity_cookie`。规范键示例：

```text
sources__mikan__category=Anime
sources__mikan__dynamic_tag_template={year}-{quarter_name}
sources__mikan__mikan_identity_cookie=...
--sources:u2:category=PT
```

上游扁平变量 `ANIMEGO_CATEGORY`、`ANIMEGO_TAG`、`ANIMEGO_MIKAN_COOKIE` 分别
控制默认 `mikan` 来源的上述三个字段。环境或命令行值会在每次启动、SQLite seed
之后重新应用，因此 WebUI 曾保存的旧值不能在重启后盖过部署值。显式空
`ANIMEGO_TAG` / `ANIMEGO_MIKAN_COOKIE` 分别表示关闭动态 Tag / 清除 Cookie；
Cookie 值从不进入响应、日志或控制键投影。

`GET /api/v1/sources` 和单项 API 通过 `locked_fields` 只返回字段、来源和控制键名。
WebUI 禁用对应输入；API 若实际改变锁定值则返回
`400 source_profile_field_locked`，但保持锁定值不变时仍可保存该来源的下载器、
规则开关等未锁字段。

环境变量的嵌套键使用 .NET 双下划线格式，例如：

```text
downloaders__bt__base_url=http://127.0.0.1:8080/
downloaders__bt__username=admin
downloaders__bt__password=...
downloaders__bt__download_path=E:\AnimeGoNet\download
```

兼容的扁平变量包括 `data_path`、`download_path`、`save_path`、
`tmdb_base_url`、`tmdb_proxy_url`、`tmdb_api_key`、`tmdb_cache_hour`、
`ANIMEGO_THEMOVIEDB_KEY`、`bangumi_base_url`、`bangumi_proxy_url`、
`ANIMEGO_CLIENT_URL/USERNAME/PASSWORD/DOWNLOAD_PATH` 和
`ANIMEGO_CATEGORY`、`ANIMEGO_TAG`、`ANIMEGO_MIKAN_COOKIE`、
`ANIMEGO_PROXY_URL`、`ANIMEGO_WEB_HOST`、
`ANIMEGO_WEB_PORT`。标准 ASP.NET Core
`--urls` / `ASPNETCORE_URLS` 覆盖 `web.host` / `web.port`；推荐新部署优先使用
规范嵌套键。

旧 `ANIMEGO_PROXY_URL` 同时覆盖 TMDB 和 Bangumi 代理，以保留上游“一个全局
代理”的语义；显式空值同时关闭两者。`tmdb_proxy_url` / `bangumi_proxy_url` 专用
变量优先于旧全局变量。旧全局变量存在时，WebUI 将两个独立代理字段都标记为环境
锁并拒绝私有覆盖。

原生默认监听 `127.0.0.1:7991`，不会默认暴露到局域网。Docker 默认监听
`0.0.0.0:7991`，并继续强制要求非空 Access Key。host 只接受 DNS 名或 IP 地址，
port 必须在 0～65535；`0` 只用于由操作系统分配临时测试端口。

三路径和 Web 监听不属于 WebUI 可编辑配置，因此不产生表单锁；`/api/v1/status`
始终显示最终生效的路径。它们仍严格遵循命令行→环境→YAML 顺序。

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
  host: 127.0.0.1
  port: 7991
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
    duplicate_notification_enabled: true

metadata:
  tmdb:
    base_url: https://api.themoviedb.org/
    proxy_url: ''
    api_key: ''
    read_access_token: ''
    language: zh-CN
    timeout_seconds: 30
    retry_count: 3
    retry_wait_seconds: 5
    cache_hours: 336
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

`sources.<id>.duplicate_notification_enabled` 默认 `true`，只控制发现重复时是否向
脱敏应用日志/WebSocket 写事件；不会改变全局 TMDB Episode 去重、RSS 早停或
下载门禁。它在首次创建 SourceProfile 时作为初始值，之后可在 WebUI 修改并随
profile revision 固化到新任务路由快照。

部署 YAML 的 `sources.<id>.display_name`、`rss_feed_url`、
`rss_schedule_enabled` 和 `rss_schedule_cron` 是首次创建 SourceProfile 的 seed；已有
SQLite profile 不会在每次重启时被 seed 覆盖，后续修改使用来源管理 API/WebUI。
`rss_feed_url` 可带 passkey query，但必须是无 userinfo/fragment 的 HTTP(S) URL，Host
必须同时列入 `allowed_torrent_hosts`。旧 AnimeGo `setting.feed.mikan` 及
`plugin.feed` 的 `name/__name__`、`url/__url__`、`cron/__cron__`、`enable` 会迁移到
这些字段；上游把 1.3 及更早 feed 升级为默认关闭，因此该路径只保留 URL/Cron，不会
擅自启用网络任务。配置值首次落入 SQLite 后属于敏感 `data_path` 数据。

`dynamic_tag_template` 留空即关闭；默认 Mikan 值与上游一致。支持 `{year}`、
`{quarter}`、`{quarter_index}`、`{quarter_name}`、`{ep}`、`{week}`、
`{week_name}`，逗号分隔多个 qB tag。模板在任务创建时随 SourceProfile revision
冻结，只在 TMDB 元数据确认后的暂停下载准备阶段展开；不会把 passkey、凭据或
未展开模板发送到 qB。

`metadata.tmdb.cache_hours` 只缓存已经通过协议与父子身份校验的 TMDB
Search/Series/Season/Episode 成功响应，默认 `336` 小时（14 天），范围为大于 0
且不超过 8760 小时。权威 404、网络、认证、限流、服务、协议和取消失败不会写入
缓存；到期条目惰性删除。旧配置
`advanced.cache.themoviedb_cache_hour` 与扁平键 `tmdb_cache_hour` 会迁移到同一字段。
缓存位于 SQLite `bolt/themoviedb` bucket，可在 WebUI 缓存页按 opaque 标识精确
删除；缓存键和值都不包含 API key 或 Bearer token，原始搜索词只参与键摘要而不单独落库。

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
整理。`/data` 必须单独持久化。只启动 AnimeGoNet、连接已有或远程 qB 的完整
双实例 Compose、环境变量、验收和故障诊断见
[EXTERNAL_QBITTORRENT.md](EXTERNAL_QBITTORRENT.md)。

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
