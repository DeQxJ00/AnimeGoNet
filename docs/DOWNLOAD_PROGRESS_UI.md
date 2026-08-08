# 下载进度 Web UI

首版下载页面的目标是让用户判断“Torrent 是否正常下载”和“AnimeGoNet 为什么尚未整理完成”，不复刻 qBittorrent Web UI。唯一下载器为 qBittorrent，但页面同时支持多个命名实例。

## 1. 两层状态

页面必须同时展示两个相互独立的状态，不能把 qBittorrent `progress=100%` 直接显示成 AnimeGoNet 已完成：

```text
qBittorrent：取元数据 → 排队 → 下载 → 下载完成
AnimeGoNet ：等待解析 → TMDB匹配 → 等待下载 → 移动/整理 → 字幕 → NFO/数据库 → 完成
```

下载完成记录仍只在下载、文件策略、重命名、必要 NFO 和目录数据库全部成功后原子写入。qB 100% 但 `move`、NFO 或数据库失败时，作品库 EP 仍是未下载完成，任务页面显示明确的整理失败阶段。

下载器规范状态至少包括 `Metadata`、`Queued`、`Downloading`、`Paused`、`Stalled`、`Checking`、`Downloaded`、`Error`、`Offline`。adapter 显式映射 qB 的 `metaDL/forcedMetaDL/queuedDL/downloading/forcedDL/stalledDL/pausedDL/stoppedDL/checkingDL/checkingResumeData/moving/error/missingFiles/unknown` 等版本差异，未知值保留原始状态并显示 `Unknown`，不能当作完成。

业务阶段至少包括 `Ingested`、`Filtering`、`ResolvingMetadata`、`WaitingDownload`、`Downloading`、`Organizing`、`Moving`、`Renaming`、`BindingSubtitles`、`WritingNfo`、`Persisting`、`Completed`、`Failed`、`Cancelled`、`Suppressed`。

## 2. 仪表盘和列表

仪表盘展示：

- 活动下载、暂停、卡住、等待整理、整理失败和完成任务数量。
- 所有已连接 qB 实例的下载/上传速度合计；离线实例单独计数，不把旧速度加入合计。
- 最近失败和最后一次成功同步时间。

下载任务列表至少展示：

- 任务/RSS标题、最终动画名称（取得前显示来源标题）、来源、mikanid 和任务 ID。
- qBittorrent 实例 ID/显示名、category/tag。
- qB规范状态、AnimeGoNet业务阶段和进度条。
- 下载百分比（显示到0.1%）、已下载/选中总容量、下载/上传速度、ETA、Seeds/Peers。
- 不可变做种目标（`0=不要求`、`-1=无限`、正数为分钟）、持久化 waiting/seeding/completed、qB 累计做种时长、正数目标百分比和首次完成时间；做种完成状态不得因重启或较旧快照倒退。
- 创建时间、下载完成时间、整理完成时间、最后更新时间。
- 当前错误摘要、重试次数、同步是否过期。

支持按活动/暂停/失败/完成、来源、qB实例和业务阶段筛选，支持标题搜索和分页。默认先显示活动与失败任务，再按最后更新时间降序；完成历史可单独筛选。

## 3. 任务详情

详情页包括：

- 顶部总进度和上述全部传输指标。
- 状态时间线：每次下载器状态、业务阶段、错误和重试的发生时间。
- 文件表：Torrent相对路径、容量、文件级百分比、qB priority、wanted/unwanted、对应来源EP和最终TMDB EP、字幕绑定结果。
- 元数据摘要：TMDB Series/Season/Episode取得阶段、验证状态和失败原因。
- 路由快照：source/profile revision、qB实例、文件策略和路径；带passkey的Torrent URL仍只显示不可逆指纹。

多文件任务的总容量和进度仅按 wanted/priority非零文件计算；unwanted重复EP及其字幕不进入分母。磁力链接尚未取得metadata时使用不确定进度，不显示伪造的总容量或ETA。ETA未知、无限或速度为0时返回`null`并显示“未知”。

Mikan默认`move`时，下载完成后依次持久化并显示 `rename_planning → media_transfer → subtitle_transfer（适用时）→ nfo_write → directory_index → cleanup_downloader → completed`。`rename_planning` 只读取已持久化的 Torrent 相对路径并生成/复核不可变目标计划；`media_transfer` 和 `subtitle_transfer` 按逐文件 operation 计数，重试时从 SQLite 中已完成 operation 续算；NFO 按 Series、目录数据库/索引按 Season 分组计数。成功后显示“已移动到媒体库，不做种”。同卷原子移动可以很快，但仍必须记录阶段；跨卷复制完成并校验一个 operation 后才增加单位进度，不能把 qB 下载百分比倒退。

整理阶段、已完成单位和总单位属于 SQLite 权威状态，不由 WebUI 根据文件数量临时估算。失败释放租约时保留当前阶段和计数，页面同时显示稳定失败码与下次重试时间。文件工作完成后原子切换到 `cleanup_downloader`；即使 `link/link_delete` 的清理租约过期，也只能恢复下载器清理，不得重新领取媒体移动阶段。最终业务完成必须同时满足 `organization_state=completed` 与 `phase=completed, 1/1`。

## 4. 首版操作边界

列表和详情首版允许：

- 暂停、恢复 qBittorrent 任务；命令幂等并在下一次同步确认实际状态。
- 对失败的 AnimeGoNet 业务阶段执行安全重试；沿用原路由快照，除非用户显式取消并重新导入。
- 跳转到四类删除中心并生成影响预览。

下载页不提供直接删除按钮，不绕过删除中心。首版不实现 Tracker/Peer明细、piece分块图、单任务限速、强制汇报、修改Tracker、顺序下载、强制重校验或qB全局设置；这些功能使用qB自带Web UI。

## 5. 同步和缓存

后台只有一个按实例隔离的 qB 状态同步器，浏览器不能直接访问 qB凭据或为每个标签页重复轮询 qB：

- 任一实例有活动/过渡任务时，目标同步间隔约2秒；完全空闲时约10秒。实际间隔可在安全范围配置并加入抖动，避免多实例同时突发请求。
- 同一实例只允许一个在途同步；慢请求不叠加，超时进入实例熔断但不影响其他实例。
- 熔断器位于所有 qB 操作共用的实例协调器：网络、超时、I/O 或认证失败后立即开启，首次等待 2 秒，连续半开失败按 2/4/8… 秒指数增长并封顶 120 秒；调用方主动取消不计失败。Web 的显式连接测试可绕过等待窗执行一次受串行锁保护的探测，成功即复位。
- 同步结果写入版本化 `DownloaderTaskSnapshot`；关键状态/错误/阶段持久化到SQLite，瞬时速度可只保留最新值。
- Web通过版本化查询API读取AnimeGoNet缓存；活动页面约2秒刷新，页面隐藏或无活动任务时降到约10秒，卸载后停止。
- 进程重启后按qB实例+Torrent hash重新关联任务；关联完成前显示“正在恢复”，不能创建重复下载。

qB离线、认证失败或超时时，页面保留最后快照，显示`Offline/Stale`和快照时间；不得把百分比、容量或速度伪造成0。恢复连接后清除stale并继续原任务。

下载器列表 API 额外返回运行期 `circuit_state=closed|open|half_open`、连续失败次数和可空的下次尝试 UTC；此状态按命名实例隔离且不持久化凭据或异常正文。进程重启后熔断状态从关闭开始，SQLite 中最后一次连接失败与最后成功时间仍保留。

## 6. API和安全

列表/详情DTO使用source-generated JSON context，并返回结构化枚举、字节数和UTC时间，不让前端解析日志文案。至少提供列表、详情、暂停、恢复和业务重试的版本化API；写操作校验任务revision避免对已替换任务执行陈旧命令。

API、日志、DOM和导出数据不得包含qB密码、Cookie、完整Torrent URL、passkey、announce或宿主绝对敏感路径。普通列表只显示Torrent相对路径；需要诊断绝对路径时必须经过认证、脱敏和明确操作。

当前实现的管理契约为：

- `GET /api/v1/downloads`：接受 `page`、`page_size`、`search`、qB `state`、AnimeGoNet `business_status`、`downloader_id` 和 `source`，筛选与分页在 SQLite 查询中执行。默认把失败、活动/暂停、历史完成依次排序，再按更新时间稳定排序。
- `GET /api/v1/downloads/{jobId}`：返回下载/业务两层状态、准备与整理阶段、文件列表和审计时间线。文件列表按 qB 文件 index 优先、规范化相对路径其次，将实时快照与持久化分配合并；尚未写入分配表的 qB 文件仍以 `unassigned` 显示。
- `POST /api/v1/downloads/{jobId}/pause`、`/resume`、`/retry`：请求体携带 `expected_revision`。暂停/恢复调用任务不可变路由绑定的 qB 实例；业务重试只清理允许重试阶段的安全失败码和下次执行时间，不改写路由快照。

schema v24 的 `download_job_events` 保存调度确认、qB 状态变化、快照缺失、暂停/恢复、业务重试和安全失败码；schema v33 另在做种状态变化时记录 `seeding_state` 事件，并保存不可变目标、单调累计秒数和首次完成时间。schema v34 保存元数据动态 tag 的实际值、`pending/applied/skipped/not_configured`、稳定失败码和 `dynamic_tag` 事件。异常正文、URL和凭据不进入该表。详情读取 qB 失败时仍返回 `200` 和 SQLite 快照，并把 `file_snapshot_state` 标记为 `unavailable`；控制命令则返回稳定的安全错误和 `503`，不回显上游异常正文。

静态 TypeScript 页面提供服务端筛选/翻页、做种目标/累计时间/完成门禁、动态 tag 状态/实际值/跳过原因、文件级百分比与 priority/wanted、准备/整理失败、时间线、暂停/恢复、业务重试和四类删除预览入口。生成的 `wwwroot/app.js` 必须由 `npm run web:build` 产生，并由 `npm run web:check` 校验类型。

列表响应的 `summary` 始终是未套用当前筛选的全局下载仪表盘，返回任务总数、活动/暂停/失败/stale、等待整理/已完成、准备/整理失败、离线实例、最近安全失败码和最后一次下载器成功时间。汇总下载速度只计算运行快照标为已连接且任务非 stale 的实例；离线实例的历史速度不得加入。

## 7. 验收边界

至少验证：

1. qB 0～100%与业务阶段独立；100%后move/NFO失败时不得显示业务完成。
2. metadata未知、排队、下载、暂停、stalled、checking、missing files、qB离线和未知状态映射。
3. 单文件/多文件的wanted总容量、百分比、速度、ETA和文件priority正确。
4. `bt`/`pt`并行同步隔离；一个离线/超时不影响另一个，速度合计不包含stale值。
5. 活动/空闲/隐藏页面刷新节奏，无重复在途请求和浏览器直连qB。
6. 重启后按实例+hash恢复，快照过期明确显示且不重复添加Torrent。
7. 暂停/恢复幂等、revision冲突、安全业务重试和删除中心跳转。
8. 密钥、passkey、announce和完整敏感路径不出现在API、日志、DOM和截图。
9. NativeAOT发布目录和Docker `linux/amd64`、`linux/arm64`运行进度页Playwright E2E。
