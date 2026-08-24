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
全局 `download_path` / `save_path`、Mikan 地址、TMDB API/图片地址、TMDB/Bangumi 连接与重试、四档季度失败链、统一 AI 开关/超时/推理程度/正式 Prompt、Bangumi 完全兜底、
可信 offset 缓存、Torrent HTTP/容量/redirect/staging 以及数据更新设置。锁定值
在读取 `application.private.json` 后重新应用；保存其他字段不会把部署值固化到
私有文件。

WebUI 的“设置与备份 → 目录与路径”可直接修改全局 `download_path` 和
`save_path`，保存前按候选配置执行绝对路径和目录边界校验，写入私有 revision 并
备份旧 revision，重启后生效。每个下载器实例的 `download_path` 仍必须落在新的
全局下载根目录内。`data_path` 决定当前 SQLite、私有配置和备份文件所在位置；同页
可单独修改部署 YAML 的 `paths.data_path` 并创建部署配置备份，但不会自动复制或
删除旧目录中的任何内容，必须在停机迁移完整数据目录后再重启。

应用覆盖按 `paths`、`network`、`ai` 三个分区提供预览和写入端点：
`/api/v1/config/sections/{section}`。服务端先以最新 revision 重建完整候选，再只合并
该分区拥有的字段，因此一个页面不会把其他页面的旧表单值写回；revision 冲突仍
要求重新载入。原 `/api/v1/config` 全量接口保留兼容。

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
`mikan_base_url`、`tmdb_base_url`、`tmdb_image_base_url`、`tmdb_api_key`、`tmdb_cache_hour`、
`ANIMEGO_THEMOVIEDB_KEY`、`bangumi_base_url`、`outbound_proxy_url`、
`outbound_proxy_hosts`、`ANIMEGO_OUTBOUND_PROXY_URL`、`ANIMEGO_OUTBOUND_PROXY_HOSTS`、
`ai_base_url`、`ai_api_key`、`ai_model`、`ai_reasoning_effort`、`ai_prompt_template`、`ai_tmdb_mcp_url`、`ai_bangumi_mcp_url`、
`ANIMEGO_CLIENT_URL/USERNAME/PASSWORD/DOWNLOAD_PATH` 和
`ANIMEGO_CATEGORY`、`ANIMEGO_TAG`、`ANIMEGO_MIKAN_COOKIE`、
`ANIMEGO_INNER_PLUGIN_MIKAN_ACCESS_KEY`、`ANIMEGO_PLUGIN_ACCESS_KEY`、
`ANIMEGO_WEB_ACCESS_KEY`（旧环境别名）、
`ANIMEGO_WEBUI_ACCESS_KEY`、`ANIMEGO_WEB_HOST`、
`ANIMEGO_WEB_PORT`。标准 ASP.NET Core
`--urls` / `ASPNETCORE_URLS` 覆盖 `web.host` / `web.port`；推荐新部署优先使用
规范嵌套键。

代理只有一个规范模型：`outbound_proxy.url` 配合 `outbound_proxy.hosts`。
扁平或环境变量的 hosts 使用逗号、分号或换行分隔；YAML 使用列表。匹配支持精确
域名和 `*.example.com`，通配符只匹配子域名、不匹配 apex，比较不区分大小写但保存
与部署模型统一规范为小写。未命中域名不继承系统代理，保持直连。程序从未投入正式
运行，因此按所有者确认直接移除了 `tmdb_proxy_url`、`bangumi_proxy_url` 与
`ANIMEGO_PROXY_URL`，这些旧键不会生效。

原生默认监听 `127.0.0.1:7991`，不会默认暴露到局域网。Docker 默认监听
`0.0.0.0:7991`，并继续强制要求非空 Access Key。host 只接受 DNS 名或 IP 地址，
port 必须在 0～65535；`0` 只用于由操作系统分配临时测试端口。

`data_path` 与 Web 监听通过部署 YAML 编辑，不属于应用私有覆盖字段，因此不产生
应用表单锁；`download_path` / `save_path` 继续参与应用部署锁。`/api/v1/status`
始终显示最终生效的路径；命令行和环境变量仍高于 YAML，页面保存 YAML 不会伪装成
已经覆盖更高优先级的运行参数。

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
  # WebUI 管理接口与实时日志的独立密钥；默认留空，可直接打开本机页面。
  webui_access_key: ''
  background_workers_enabled: true

# AnimeGoHelper (Mikan)、兼容插件接口与统一导入 API。
inner_plugin_mikan:
  access_key: '123456'

outbound_proxy:
  url: ''
  hosts: []

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
  mikan:
    base_url: https://mikanani.me/
    episode_identity_cache_hours: 8760
    bangumi_identity_cache_hours: 8760
  tmdb:
    base_url: https://api.themoviedb.org/
    image_base_url: https://image.tmdb.org/t/p/
    api_key: ''
    read_access_token: ''
    language: zh-CN
    timeout_seconds: 30
    retry_count: 3
    retry_wait_seconds: 5
    cache_hours: 144
  bangumi:
    base_url: https://api.bgm.tv/
    timeout_seconds: 30
  season_failure:
    skip: false
    backtrace: false
    use_title_season: false
    use_first_season: false
  tmdb_failure_use_bangumi: false
  write_bangumi_id_when_tmdb_matched: false
  mikan_trusted_offset_cache_enabled: false
  mikan_trusted_offset_required_episodes: 3
  ai:
    provider: openai_compatible
    base_url: ''
    api_key: ''
    model: ''
    # Multiline production Prompt is best managed through WebUI; empty uses the built-in template.
    prompt_template: ''
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

`metadata.mikan.base_url` 必须是以 `/` 结尾、无路径前缀的 HTTP(S) origin；程序把
RSS、作品页和带 passkey 的 Torrent URL 的 scheme/host/port 替换为该 origin，同时
原样保留 path/query。配置为内网反向代理（例如 `http://mikan.local/`）时，只信任该
明确 host 可以解析到私网地址；其它允许 host、redirect 和 Torrent 地址仍执行原有
SSRF 公网地址门禁。

`metadata.mikan.episode_identity_cache_hours` 控制 Episode URL → `mikanid/groupid`；
`metadata.mikan.bangumi_identity_cache_hours` 控制 `mikanid→bgmid`。两项默认均为
`8760` 小时（1 年），允许 `0` 表示永久，最大 87600 小时（10 年）；只缓存成功且 ID
完整的结果，失败不做 negative cache。对应 bucket 为 `bolt/mikan_episode_identity` 和
`bolt/mikan_bangumi_identity`，均可在 WebUI“系统缓存”中逐项检查和删除。WebUI 修改后
需重启，部署 YAML、环境变量或命令行显式设置时页面只读。

`metadata.tmdb.image_base_url` 必须是以 `/` 结尾的 HTTP(S) base。官方默认值包含
`/t/p/`；若反向代理根目录保持 TMDB 图片路径结构，应写成
`http://image.tmdb.local/t/p/`。Mikan、TMDB API、TMDB 图片和 Bangumi API 四个
地址都能在 WebUI 应用配置中修改，保存后需重启生效。

`outbound_proxy.url` 只接受无凭据、无 path/query/fragment 的 `http://`、`https://`
或 `socks5://` origin。`hosts` 非空时必须同时配置 URL。该策略覆盖 Mikan RSS 与
Torrent、TMDB/Bangumi、封面、AI/MCP 和 AnimeGoNetData；qBittorrent 实例连接和
编译期固定的 AniDB 参考查询明确直连。Torrent 走代理前仍逐跳检查来源 host
allowlist、DNS 地址、redirect 和 HTTPS downgrade；forward proxy 自行解析目标，
因此只有直连分支承诺连接钉在预先校验的 IP。

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
Search/Series/Season/Episode 成功响应，默认 `144` 小时（6 天），范围为大于 0
且不超过 8760 小时。权威 404、网络、认证、限流、服务、协议和取消失败不会写入
缓存；单集 `air_date` 为空的 Episode 响应不缓存，包含任一无 `air_date` Episode 的
完整 Season 响应也不缓存，已有此类旧缓存会在读取时删除并在线刷新。到期条目惰性删除。旧配置
`advanced.cache.themoviedb_cache_hour` 与扁平键 `tmdb_cache_hour` 会迁移到同一字段。
缓存位于 SQLite `bolt/themoviedb` bucket，可在 WebUI 缓存页按 opaque 标识精确
删除；缓存键和值都不包含 API key 或 Bearer token，原始搜索词只参与键摘要而不单独落库。

解析过程中若已命中的缓存无法提供当前必需的权威对象，主程序会只针对缺失层在线强制
刷新一次：搜索结果为空时刷新 Search，Series 详情没有可匹配季度或季度 endpoint 为空
时刷新 Series/Season，已确认季度的快照没有目标 EP 或 Episode endpoint 为空时刷新
Season/Episode。有效刷新结果覆盖原条目并重新计算 TTL；空、身份不符、无 Episode
日期及网络失败结果不会覆盖已有成功缓存。刷新一次后仍缺失才继续既有失败分类或 AI
流程，不循环重试，也不清除其他 TMDB 缓存。

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
