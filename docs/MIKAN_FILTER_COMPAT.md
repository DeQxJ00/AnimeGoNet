# MikanTool 与 AnimeGoHelper 过滤兼容

本章固定上游 `develop` 分支 `assets/plugin/filter/mikan_tool.py`、`/api/plugin/config` 和 `DeQxJ00/AnimeGoHelper` 的兼容边界。Python 运行时会被完全移除，但过滤行为和油猴脚本接口由内置 C# 实现接管。

## 1. 五级配置结构

旧配置的字段拼写错误已经成为外部契约，必须原样保留为 `Filiter0`～`Filiter4`，不能在旧 API 输出中改成 `Filter`：

| 字段 | 作用域/旧 key | 行为 |
|---|---|---|
| `Filiter0` | 全局规则 map | 对所有候选标题执行，不依赖 Mikan 页面解析 |
| `Filiter1` | `key_{mikanid}_{sub_group_id}` | 指定 Mikan 作品和字幕组组合，最具体 |
| `Filiter2` | 十进制 `mikanid` | 指定 Mikan 作品 |
| `Filiter3` | 十进制 Mikan 字幕组 ID | 指定字幕组 ID |
| `Filiter4` | 标题解析出的字幕组名称 | 按解析后的 group 文本匹配 |

`Filiter1`、`Filiter2`、`Filiter3` 的选择顺序固定为 `1 → 2 → 3`，命中前一档后不再应用后面的档位。`Filiter0` 和命中的 `1/2/3`、`Filiter4` 最终以 AND 合并，任一适用结果拒绝即丢弃该 RSS 项。

上游 `Filiter0` 对多个 map 项逐项赋值，最终只保留最后迭代项的结果，而不是 AND。这属于已观察到的兼容细节：旧配置导入必须保存 JSON 属性顺序并在 legacy 模式复现；Web UI 对多个全局项显示醒目警告和实际执行顺序，但不能静默改写旧配置。

## 2. 单条黑白名单语义

每条规则保持以下字段：

```json
{
  "is_enable_whitelist": true,
  "whitelist": ["简体", "1080P"],
  "is_enable_blacklist": true,
  "blacklist": ["合集"]
}
```

关键词使用与 Python `title.find(item) >= 0` 一致的区分大小写、按原字符的普通子串匹配，不使用正则表达式，不做 Unicode、全半角或大小写归一化：

- 白名单开启、黑名单关闭：标题包含任一白名单词才通过。
- 白名单关闭、黑名单开启：标题包含任一黑名单词即拒绝。
- 两者都开启：必须命中任一白名单词且不能命中任何黑名单词。
- 两者都关闭：通过。

空字符串按上游会匹配任意标题；Web UI 保存时必须警告，但 legacy API 不能擅自删除。列表顺序、重复项和原始字符需要往返保留。

## 3. 流水线边界

- Mikan `SourceProfile` 增加强类型开关 `mikan_rss_filter_enabled`，默认 `true` 以保持上游行为。旧 AnimeGoHelper `/api/rss` 固定解析到默认的 Mikan legacy profile，并读取该 profile 的开关。
- 总开关开启时，`POST /api/rss` 的全集和 `is_select_ep=true` 指定集都会进入 MikanTool；指定集只是先缩小 RSS 项目集合，不代表跳过过滤。
- `POST /api/download/manager` 按上游快速下载语义跳过 ordered filter，直接进入解析和下载管理器。Web UI 要明确显示“快速下载未应用 Mikan 过滤”，不能让用户误以为规则失效。
- 新 `/api/v1/ingest` 是否应用过滤由 `SourceProfile` 的显式入口策略决定并写入不可变路由快照；Mikan RSS profile 默认应用，直接 Torrent API profile 默认跳过。不能仅凭 `source=mikan` 猜测。
- `mikan_rss_filter_enabled=false` 时，RSS 仍完成认证、解析、指定集选择、输入校验和基础去重，但整个 MikanTool ordered filter 步骤记为 `SkippedByConfiguration`；随后继续 TMDB 匹配和下载流水线。关闭开关不会清空、改写或禁用单条 `Filiter0`～`Filiter4` 规则，也不影响旧配置 GET/POST。
- 开关变更只影响变更后创建的 ingest/task。已经创建或运行中的任务使用创建时保存的 SourceProfile revision 和过滤开关快照，不能在处理中途改变结果。
- 同批次候选的具名黑白名单和通用有序优先级组不属于旧 `Filiter0`～`Filiter4`，详见 [`MIKAN_RSS_PRIORITY.md`](MIKAN_RSS_PRIORITY.md)。旧 AnimeGoHelper 配置上传不能覆盖或清空新增优选规则。
- 标题解析、Mikan URL、`mikanid` 或字幕组解析异常时保持上游“该项目不进入结果”的效果，同时在新系统中持久化 `FilterEvaluationFailed` 原因，避免静默消失。
- 过滤发生在 TMDB 匹配和提交下载器之前。被过滤项不能占用 Episode claim 或完成记录。

## 4. 旧 API 与存储

以下调用保持原油猴脚本无需修改：

```http
POST /api/plugin/config
GET  /api/plugin/config?name=filter/mikan_tool.py
```

- 内部插件规范名为 `inner_plugin_mikan`；继续接受旧插件名 `filter/mikan_tool.py` 及上游允许的等价别名，保证未修改旧脚本仍可使用。
- POST 的 `data` 继续是 Base64 编码 JSON；成功/失败 HTTP 状态、响应 envelope、`name` 和消息保持兼容。
- GET 重建同构的 `Filiter0`～`Filiter4` JSON 后 Base64 返回。
- 兼容层不创建、查找或执行 `.py` 文件；旧名称只映射到内置 C# `MikanToolFilter`。

WebUI 的“插件 → 内部插件 → Web API / AnimeGoHelper (Mikan) 油猴插件”直接显示并修改部署 `web.access_key`，同时给出当前浏览器可用的 `/api` 地址和固定 `PluginName=inner_plugin_mikan`。新部署默认 AccessKey 为 `123456`；页面和油猴脚本中填写同一明文值，AnimeGoHelper 自行发送其小写 SHA-256。保存会备份部署 YAML，重启主程序后应用新的鉴权边界。

内部使用 SQLite 事务保存强类型规则、tier、legacy key、legacy order、启用状态、关键词、revision 和审计信息。一次旧 API 上传按上游语义视为完整配置替换，保存前创建可回滚快照；因为旧脚本没有 revision 字段，只能保持最后一次完整上传生效，但必须记录来源和时间。新 Web API 使用 revision/ETag，拒绝覆盖已经被油猴脚本更新的旧版本。

## 5. Web UI

配置页增加独立的“Mikan 过滤”区域：

- 按全局、作品+字幕组、作品、字幕组 ID、字幕组名称五档查看、增加、编辑、删除和启停规则。
- 页面顶部提供“AnimeGoHelper / Mikan RSS 过滤”总开关，绑定默认 Mikan SourceProfile 的 `mikan_rss_filter_enabled`。关闭前显示影响说明；关闭后保留规则编辑和导入导出能力，并显示“RSS 将跳过过滤，快速下载本来就不应用过滤”。
- 对作品字段统一显示 `mikanid`；不把它标成 Bangumi Subject ID。
- 编辑白名单/黑名单开关与关键词，显示区分大小写、普通子串语义。
- 输入样例标题、Mikan URL/`mikanid` 和字幕组后执行服务端规则预览，按真实顺序显示每一档是否适用、命中的词、最终接受/拒绝及原因。
- 支持导入/导出旧 `Filiter0`～`Filiter4` JSON，导出结果可直接被 AnimeGoHelper 使用。
- 显示最近一次配置来源（Web/AnimeGoHelper/迁移）、revision、修改时间和回滚入口；遇到 stale revision 时要求刷新而不是覆盖。
- RSS/任务详情保存并展示过滤决策摘要；关键词按普通文本转义显示，禁止作为 HTML 渲染。

现代管理端点固定操作默认 `mikan` SourceProfile，并与旧 AnimeGoHelper API 共用同一份 revision 数据：

```http
GET  /api/v1/mikan/legacy-filter
PUT  /api/v1/mikan/legacy-filter
POST /api/v1/mikan/legacy-filter/import
POST /api/v1/mikan/legacy-filter/rollback
POST /api/v1/mikan/legacy-filter/preview
```

GET 同时返回当前强类型规则、可直接交给 AnimeGoHelper 的 `legacy_json` 和最近快照。PUT、导入与回滚都要求 `expected_revision`；回滚不是覆盖或删除历史，而是从目标快照创建新的 `updated_source=rollback` revision。预览接收当前页面尚未保存的规则草稿，因此可以在持久化前验证 F0 顺序、F1/F2/F3 短路、F4 字幕组名、白黑名单实际命中词和最终结果。结构化编辑器使用 JSON 字符串数组，避免普通“每行一个”编辑器无法区分空数组与单个空字符串，也能保留重复项和原始大小写。

## 6. NativeAOT 与验证

配置 DTO 和旧 envelope 使用 source-generated `System.Text.Json` context；不得依赖反射序列化、Python 或动态代码生成。C# 实现需要对上游 Python 运行结果做相同输入差分测试，至少覆盖：

- 五档分别命中、不命中，以及 `Filiter1 > Filiter2 > Filiter3`。
- 全局与其他适用档位的 AND、多个 `Filiter0` 的 legacy 顺序行为。
- 白名单、黑名单、两者同时、两者关闭、空词、大小写和 Unicode 字符。
- 标题/Mikan/字幕组解析失败、空配置和默认配置。
- RSS 全集、指定集、快速下载跳过过滤和 SourceProfile 显式策略。
- 总开关默认开启、关闭后 RSS 跳过、重新开启后原规则恢复，以及变更不影响已有任务快照。
- 原 AnimeGoHelper 未修改脚本上传、读取、再次上传的完整往返。
- Web 与油猴脚本交替修改的 revision 冲突、快照和回滚。
- 五个 NativeAOT RID 的单元/契约测试；Docker `linux/amd64`、`linux/arm64`
  浏览器端到端入口保留，但按项目所有者要求暂不声称已验证。

原脚本浏览器门禁固定 `DeQxJ00/AnimeGoHelper@78a9d0d8` 及文件 SHA-256。CI
单独签出该仓库，Chromium 在隔离 Mikan 页执行未经改写的 `AnimeGoHelper.js`；fixture
只补齐 Tampermonkey API、Tagify/Ladda 页面全局量和确定性响应。门禁实际点击“单”“全”
以及“上传过滤配置/获取过滤配置”，并与真实 C# Kestrel 契约测试共同覆盖浏览器和服务端
两侧。详细结果见 `docs/verification/2026-08-08-animegohelper-browser-e2e.md`。
