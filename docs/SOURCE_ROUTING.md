# 多输入源、下载器实例与规则路由

## 1. 目标

AnimeGoNet 支持多个命名下载器实例，并按输入源选择下载器和业务规则。路由由配置/SQLite 决定，API 调用方不能通过任意 URL 或客户端类型绕过已配置的源规则。

首批示例：

| 输入源 | 元数据约束 | 下载器实例 | 典型文件策略 |
|---|---|---|---|
| `mikan` | `bgmid` 必填；从 Mikan URL/RSS 取得 `mikanid` | `bt`（qBittorrent） | `move`（确认的默认值，不做种） |
| `u2` | `anidbid` 可空；可额外传稳定 `source_work_id` | `pt`（qBittorrent） | `link` 候选，便于长期做种 |
| `ttg` | `imdbid` 可空；可额外传稳定 `source_work_id` | `pt`（qBittorrent） | `link` 候选，便于长期做种 |

名称和绑定均可在 Web UI 修改；表中的 `bt`、`pt` 不是硬编码关键字。
默认 Mikan profile 的 `file_strategy=move` 也是可修改的初始值，不是协议常量；变更只影响之后创建的任务，历史/进行中任务继续使用路由快照。Web 选择 `move` 时必须提示下载完成即移动、无法继续做种。

唯一支持的下载器类型是 `qbittorrent`，但允许任意多个命名实例。旧配置中的 `transmission` 只可被读取并显示永久 `UnsupportedDownloaderType`，不能启用、路由任务或在 Web 新建，也不得自动转换成 qBittorrent；项目路线图不包含该适配器。

## 2. 下载器实例

YAML 保存连接和部署信息，因为 URL、凭据、容器路径和超时属于部署配置：

```yaml
downloaders:
  bt:
    type: qbittorrent
    base_url: http://qb-bt:8080
    username: animego
    password: ${secret}
    download_path: /download/incomplete/bt
    enabled: true
  pt:
    type: qbittorrent
    base_url: http://qb-pt:8080
    username: animego
    password: ${secret}
    download_path: /download/incomplete/pt
    enabled: true
```

- 实例名是稳定、不区分大小写的 ID；同一种客户端可以配置多次。
- 每个实例有独立连接状态、限流、分类/tag、路径映射和错误熔断，不共享会话或缓存。
- 密码支持环境变量/secret file；Web 私有覆盖只写不回显，保存到 `data_path/config/downloaders.private.json` 并要求重启应用。该文件不属于业务 SQLite，随 data_path secret 备份策略管理，禁止提交 Git。
- 全局 Docker 根路径仍为 `download_path=/download/incomplete`、`save_path=/download/anime`，实例路径只能位于下载根目录下。两个下载器和 AnimeGoNet 必须把共同宿主父目录挂载到容器内同一个 `/download`。
- 下载任务创建时保存下载器实例 ID 和配置版本快照；之后修改源绑定不会偷偷迁移进行中的任务。

## 3. 输入源配置

SQLite 保存可由 Web 修改的业务路由。`SourceProfile` 至少包含：

- `id`、显示名、adapter 类型、启用状态、版本。
- 绑定的 `downloader_id`。
- metadata ID schema：哪些字段必填、可空及格式。
- filter/rule profile、TMDB/AI策略 profile。
- 对 Mikan RSS profile 保存 `mikan_rss_filter_enabled`，默认 `true`；AnimeGoHelper legacy `/api/rss` 使用默认 Mikan profile 的当前 revision，开关和规则随任务路由快照固化。
- 对 Mikan RSS profile 另存独立的 `mikan_rss_priority_enabled` 和版本化优选规则；新安装默认启用，在同批次按可靠 `mikanid+来源EP` 聚合重复RSS选项并逐组淘汰，规则结构见 [`MIKAN_RSS_PRIORITY.md`](MIKAN_RSS_PRIORITY.md)。
- qB category、静态附加 tags；AnimeGoNet 总会额外加入 `animegonet`、来源 ID 和文件策略三个可识别系统 tag。
- `file_strategy`：`link`、`link_delete`、`move`、`wait_move`。
- `seeding_time_minutes` 沿用上游 qB 语义：`0` 不做种、`-1` 无限做种、正数为分钟上限；`move` 必须为 `0`，因为下载完成后移动源文件。
- 依赖 TMDB/Bangumi 日期或 EP 的上游 `{year}/{quarter}/{ep}` 动态 tag 模板不能在暂停 dispatch 阶段求值；它将由后置元数据模块验证后再赋值，本阶段不会把未展开模板原样发送给 qB。
- 去重范围固定为全局媒体库，不允许 source profile 改成来源内去重；规范键为 TMDB Series/Season/Episode。profile 只可配置发现重复后的日志/通知，不可绕过完成记录。

配置优先级固定为：作品级人工规则 > 输入源 profile > 全局默认。人工规则命中失败时显式报错，不能静默落到较低层覆盖。

通用作品规则键为 `(source_id, source_work_key)`：

- Mikan 的 `source_work_key` 固定为十进制 `mikanid`。
- U2/TTG 可由调用方传稳定 `source_work_id`；缺失时可以处理单次任务，但不能自动套用作品级人工规则。
- `anidbid`、`imdbid` 是作品级元数据候选，不自动等价于站点内部作品 ID。

## 4. 通用导入契约

现有 Mikan `/api/download/manager` 的 `source + data[].torrent + data[].info` 结构提升为所有来源的统一批量契约。新增版本化入口：

```http
POST /api/v1/ingest
```

```json
{
  "source": "mikan",
  "data": [
    {
      "torrent": "https://example.invalid/personal-passkey/file.torrent",
      "info": {
        "title": "任务总标题",
        "source_item_id": null,
        "source_work_id": "3951",
        "mikan_url": "https://mikanani.me/Home/Bangumi/3951",
        "mikanid": 3951,
        "bgmid": 547888,
        "anidbid": null,
        "imdbid": null
      }
    }
  ]
}
```

`info.name` 作为旧 Mikan API 的 `title` 兼容别名，二者同时存在且不一致时拒绝请求；`info.url` 作为 `mikan_url` 兼容别名。旧 `/api/download/manager` 原结构无需修改，内部反序列化为相同的强类型 `IngestBatchCommand`。也支持 multipart 上传 `.torrent`；服务端解析文件列表和容量，调用方不能伪造最终文件清单。响应按每个 data item 返回 ingest ID、profile 版本、下载器实例、规则摘要和状态。

新 `/api/v1/ingest` 的每个 data item 必须有非空 `torrent` 和 `info.title`；批次逐项校验并返回逐项结果，某一项失败不应掩盖其他项状态，但默认不做“部分请求悄悄成功”，响应必须明确列出 accepted/rejected。

按 adapter 校验：

- `mikan`：必须有正整数 `bgmid`，并从 URL/RSS 或 `source_work_id` 得到正整数 `mikanid`。
- `u2`：`anidbid` 可空；非空时必须是正整数。
- `ttg`：`imdbid` 可空；非空时规范为小写 `tt` 加数字的 IMDb Title ID。

保留旧 `/api/rss` 和 `/api/download/manager` 的 Mikan/AnimeGoHelper 行为，内部转换为同一个 ingest command，不复制第二套流水线。旧 AnimeGoHelper 没有显式传 `bgmid` 时允许 Mikan adapter 按上游方式解析补齐；新 `/api/v1/ingest` 的 Mikan 项按产品规则要求显式提供 `bgmid`。U2/TTG 沿用相同格式，仅替换 `source` 和对应 `info.anidbid`/`info.imdbid`。

首版明确采用外部脚本/API模式：油猴、浏览器扩展或其他已登录程序取得标题、带个人 passkey 的 Torrent URL 和可选元数据 ID，再调用 AnimeGoNet。主程序不保存 U2/TTG 账号、Cookie，不执行网站登录、页面抓取、验证码或 2FA 流程。

### 4.1 Passkey Torrent URL 安全边界

Torrent URL 和下载后的 `.torrent` announce 信息都可能包含个人 passkey，必须按 secret 处理：

- `/api/v1/ingest`、旧兼容入口和 Docker 部署均要求 Access Key；反向代理到非回环地址时必须使用 HTTPS。
- 日志、异常、Web UI、API响应、审计和遥测只显示来源 ID、允许域名和不可逆 URL 指纹，不显示完整 host 后的 path/query/fragment。
- 每个 SourceProfile 配置允许的 Torrent host；只允许 HTTP(S)，每次 DNS解析和每个 redirect 都重新校验 host/IP，禁止跳到未授权地址，限制重定向次数、响应大小和下载时间。
- AnimeGoNet 立即获取 `.torrent` 字节，校验 content/bencode、info-hash、文件数量/路径/总大小后再交给命名下载器实例，不把原始 passkey URL作为普通可观察字段传播。
- 临时 `.torrent` 位于 `/data/staging`，使用仅进程可读权限，排除备份和 Web 下载；下载器确认接收后删除。若为崩溃恢复暂存，记录必须有过期时间并由启动清理任务回收。
- `.torrent` 原文和 announce URL不得发送给 AI；AI只接收任务标题、文件相对名/容量、可空元数据 ID，以及Mikan单文件日期优先分支所需的文件条目数、发布日期、文件名EP和程序门禁。

## 5. 元数据与 AI 输入

任务级 AI 输入扩展为：

```json
{
  "title": "任务总标题",
  "files": [],
  "bgmid": null,
  "anidbid": null,
  "imdbid": null,
  "torrent_file_count": 1,
  "published_at": null,
  "bgm_episode_candidate": null,
  "use_bangumi_pubdate_first": false
}
```

- 三种 ID 非空时均已与任务标题/Torrent/文件组建立作品级绑定，但只作辅助证据，不证明具体 TMDB Season/Episode。
- `imdbid` 使用 TMDB MCP 的 external ID/find 能力取得候选；候选仍须验证为 TMDB TV Series，并逐级验证 Season/Episode。
- 跨站标题、季度拆分和 Episode 编号不要求相同。
- `use_bangumi_pubdate_first` 是最终门禁，不是外部调用方可直接开启的标志。只有配置开关开启、Mikan RSS、Torrent实际文件条目数恰好为1、`bgmid/pubDate` 有效且主程序成功算出最近普通EP并写入 `bgm_episode_candidate` 时为真；否则为假并从 Prompt 裁掉整个优先区块。
- 只有 `source_type=mikan` 时，后端才在本地为文件计算可空 `file_episode_candidate`；该字段和 `episode_offset` 都不进入 AI Prompt/请求/响应。主程序在逐文件 TMDB 验证后本地计算统一偏移。`mikanid/groupid` 仅供主程序在 AI 调用前查询本地可信缓存，不发送给模型；缓存命中记录必须同时给出有效 `tmdb_id`、普通 `season` 和偏移，由主程序直接计算目标 EP，详见 [`MIKAN_EPISODE_OFFSET_CACHE.md`](MIKAN_EPISODE_OFFSET_CACHE.md)。
- 输入源、下载器凭据、Cookie、站点 token、宿主路径和 profile 内部配置不得发送给模型。

## 6. Web UI

### 当前 SourceProfile 管理 API

首版服务端已经提供下列 NativeAOT 安全的 JSON 接口：

- `GET /api/v1/sources`、`GET /api/v1/sources/{id}`；
- `POST /api/v1/sources`；
- `PUT /api/v1/sources/{id}`，请求必须带 `expected_revision`；
- `DELETE /api/v1/sources/{id}?expected_revision=...`。

创建 ID 必须已经是稳定小写 ID，adapter 只接受编译期注册的 `mikan`、`u2`、`ttg`，绑定只能指向当前部署配置中已启用的 qBittorrent 实例。Host 白名单统一转小写并校验 DNS host/`*.` 通配形式。adapter 创建后不可修改；修改下载器、文件策略、白名单或规则开关会增加 revision，只影响之后创建的任务。API 返回不可变任务和 RSS batch 引用计数；存在引用时拒绝删除，默认 `mikan` profile 始终拒绝删除。新 profile 自动初始化独立的有序 RSS 规则集，Mikan adapter 还会初始化内置 legacy filter 空配置。

下载器连接以部署级 `AnimeGoOptions.Downloaders` 为基础，可由 data_path 私有覆盖增加或替换。`GET /api/v1/downloaders` 永不返回用户名/密码；`PUT/DELETE /api/v1/downloaders/{id}` 使用全局 configuration revision 原子写入覆盖，密码字段留空时保留、`clear_password=true` 时清除。修改不会热替换正在运行的客户端，响应与 Web 明确显示 `restart_required`；重启后在配置校验和客户端注册前应用。仍被 SourceProfile、导入任务或下载任务引用的实例不能停用或移除覆盖。

`POST /api/v1/downloaders/{id}/test` 依次验证 Cookie 登录、任务列表、客户端版本和 qB 默认保存路径；响应不包含凭据。`POST /api/v1/downloaders/{id}/path-probe` 不连接 qB，只验证 AnimeGoNet 进程是否同时看见实例 `download_path` 与全局 `save_path`，并用随机隐藏临时文件实际创建一次硬链接。探测总是显式触发，结束后尽力清理；返回 `directory_missing`、`permission_denied`、`hard_link_unavailable`、`platform_not_supported` 或验证失败等稳定错误码，不回传异常细节。

所有后台 qB 操作通过同一个按实例串行协调器。每个实例拥有独立内存熔断状态；网络、超时、I/O 或认证失败开启 2 秒等待窗，后续半开失败按指数增加到最多 120 秒。熔断期间后台操作直接得到稳定的 `qbittorrent_circuit_open`，不会重复访问 qB；其他实例继续运行。显式连接测试绕过等待窗做一次人工探测，仍遵守单实例串行约束，成功后立即复位。

### 下载器页面

- 多实例 CRUD、连接测试、客户端版本、qB 默认保存路径、延迟、当前任务数和最近错误。
- 显示容器路径、宿主映射提示、显式硬链接探测结果和被哪些来源引用。当前页面已接入这些操作；真实容器间映射仍由 Compose 集成测试验收。
- 删除实例前阻止仍被 profile 或活动任务引用；支持禁用但不破坏历史任务快照。

### 输入源页面

- source profile CRUD；下载器下拉绑定。当前静态页面已实现列表、创建、编辑、启停和删除，并显示 revision、不可变引用计数、当前路由及 `move` 不做种提示；下载器候选目前来自已有来源绑定且允许输入部署配置中的稳定 ID，待下载器只读/CRUD API 完成后改为完整实例投影。
- metadata ID 必填规则、过滤/元数据 profile、分类/tag、文件策略、做种和重复命中通知。
- “路由预览”：输入模拟 title/IDs 后显示会命中哪个下载器、哪些规则以及路径。
- 修改只影响新任务；进行中任务保持原快照，可由用户显式重新路由。

当前 `POST /api/v1/sources/{id}/route-preview` 使用持久化 SourceProfile 的编译期 adapter 执行与统一导入相同的字段规范化，返回 profile/rule revision、下载器、download/save path、文件策略、category、tags、做种分钟和规则开关。预览构造内存中的安全占位 Torrent URL，不执行网络请求、不写 ingest task、不连接 qB。SourceProfile ID 与 adapter 已分离，因此 `u2-anime` 等自定义 ID 会保存为来源身份，同时使用 `u2` adapter 校验；实际 `/api/v1/ingest` 采用完全相同的分离逻辑。

### 任务/作品详情

- 显示来源、source item/work ID、`mikanid`、bgmid/anidbid/imdbid。
- 显示下载器实例、profile/规则版本、路由原因和文件策略。
- 继续显示 TMDB Series/Season/Episode 各自的取得阶段和验证结果。

## 7. 插件与 NativeAOT

新增强类型 `IInputSourceAdapter` 契约，内置 Mikan/U2/TTG adapter 通过显式 DI 注册。它负责输入校验、作品键规范化和可选 RSS 转换，不直接访问下载器或 SQLite。外部来源可使用既有独立 C# 可执行插件协议增加 `source` 类型；NativeAOT 主程序仍不动态加载 DLL。

## 8. 验证门禁

1. 同时启动两个 fake/真实 qBittorrent 实例，`mikan` 只进入 `bt`，`u2/ttg` 只进入 `pt`。
2. 修改绑定后新任务走新实例，进行中任务仍走创建时的实例。
3. 下载器离线、禁用、认证失败、路径越界和硬链接不可用均在提交前给出可操作错误。
4. Mikan 缺 bgmid 拒绝；U2/TTG 可在可选 ID 为空时仅凭标题/Torrent进入后续匹配。
5. imdbid 格式、TMDB external ID候选、Movie误命中、TV验证失败和 Episode不一致均覆盖 fixture。
6. 旧 Mikan API 与新通用 API 对同一输入生成相同 ingest command 和路由结果，AnimeGoHelper 原脚本无需修改。
7. Source profile 和 downloader 的新增 API、Web UI、NativeAOT 发布二进制及 Docker 双下载器场景全部通过。
8. Mikan 已完成的 TMDB Episode 从 U2/TTG 再次输入时同样跳过；同一多文件 Torrent 中未完成的其他 Episode 继续下载。
