# Mikan RSS 同集候选优先级

本功能处理一次 RSS 拉取中同一集出现多个发布选项的情况。它是新增的批次级筛选器，与逐条执行的旧 MikanTool 相互独立，并由 Web UI 单独配置。

## 1. 开关和执行阶段

默认 Mikan SourceProfile 增加 `mikan_rss_priority_enabled`，新安装默认 `true`。关闭后不执行本章的候选黑白名单和优先级比较，但不删除配置；修改只影响新建批次，进行中任务使用创建时的 SourceProfile revision 快照。

两个开关互不控制：

1. `mikan_rss_filter_enabled`：旧 MikanTool 对每个 RSS 项执行 `Filiter0`～`Filiter4`。
2. `mikan_rss_priority_enabled`：对幸存项先应用本功能的具名黑白名单，再对同一次 RSS 中相同 `(mikanid, 来源Episode)` 的多个候选执行有序优选。

因此 MikanTool 关闭时，同集候选优选仍可单独启用。旧 AnimeGoHelper 配置 GET/POST 只管理 `Filiter0`～`Filiter4`，不能覆盖新增优选配置。

## 2. 标题规范化

分类只匹配 RSS entry title，不使用 Torrent 内部文件名替代。匹配前对标题和所有具名匹配数组的 `values[]` 执行 invariant lowercase，再做普通子串查找：

- `HEVC`、`Hevc`、`hevc` 视为相同。
- 中文字符保持原样。
- 不使用正则表达式，不自动删除点号、横线、空格或全半角差异；需要匹配 `H.265` 时必须在 `values[]` 中配置 `h.265`。
- RSS 原始标题保持不变；配置的 `values[]` 在 API 保存时规范为小写，UI 也按小写显示。具名数组的 `name` 保持用户输入，只用于展示、排序和日志，绝不参与标题匹配。

新 Web API 禁止保存空值；重复值允许导入检查但保存前要求去重。同一优先级组内不同具名数组包含相同值时，按具名数组顺序选择最先命中的分类。

## 3. 通用有序数组模型与默认预设

配置不是固定四维结构，而是一个可以为空、增减和排序的 `priority_groups[]`。每个优先级组包含一个可以增减和排序的具名匹配数组列表 `arrays[]`；一个具名匹配数组的结构固定为 `{ id, name, values[] }`，例如 `{ name: "简体", values: ["简体", "简繁日", "简日"] }`：

- `priority_groups[]` 的顺序表示组与组之间的优先级；前一组的任何差异都高于后续全部组。
- 同一组中 `arrays[]` 的顺序表示组内优先级；先出现的具名数组优先。
- 一个具名数组的 `name` 是标签，`values[]` 才是实际参与匹配的等价内容；同一个 `values[]` 内部没有额外优先级。
- 用户可以新增、删除、重命名和拖动任意优先级组或组内具名数组，也能编辑 `values[]`，不限制必须存在四组。
- 删除全部优先级组是合法配置，此时只执行黑白名单，并按稳定 tie-breaker 选择候选。

```yaml
priority_groups:
  - id: subtitle_language
    name: 字幕语言
    arrays:
      - name: 简体
        values: ["简体", "简繁日", "简日"]
      - name: 繁体
        values: ["繁体", "繁中", "简繁日", "繁日"]
  - id: subtitle_packaging
    name: 字幕封装
    arrays:
      - name: 外挂
        values: ["外挂"]
      - name: 内封
        values: ["内挂", "内封"]
      - name: 内嵌
        values: ["内嵌"]
  - id: video_codec
    name: 视频编码
    arrays:
      - name: H.265
        values: ["h265", "hevc"]
      - name: H.264
        values: ["h264", "x264"]
  - id: resolution
    name: 分辨率
    arrays:
      - name: 1080p
        values: ["1920x1080", "1080p"]
      - name: 720p
        values: ["1280x720", "720p"]
```

这四个优先级组只是初始预设，默认顺序即：

1. 简体 > 繁体 > 未识别。
2. 外挂 > 内封 > 内嵌 > 未识别。
3. H.265 > H.264 > 未识别。
4. 1080p > 720p > 未识别。

`简繁日` 同时出现在名为“简体”和“繁体”的具名数组中，按“组内最先命中的具名数组”归入简体。UI 对跨具名数组重复值显示提示和最终归类。

建议但不直接加入预设的常见值包括：简体数组的 `简中/chs/sc/zh-cn/zh-hans`，繁体数组的 `cht/tc/zh-tw/zh-hant`，外挂数组的 `external`，内封数组的 `softsub/muxed`，内嵌数组的 `硬字幕/hardsub`，H.265 数组的 `h.265/x265`，H.264 数组的 `h.264/avc`，以及分辨率乘号写法 `1920×1080/1280×720`。用户确认后可加入预设，或直接在 Web 中编辑。

`10bit`、`hi10p` 不是编码类型，不应自动放进 H.265；`ass` 不能证明外挂或内封。AV1、2160p/4K、发布来源（BD/Web/TV）、HDR、音频和 v2/v3 修正版偏好具有主观性，首版不默认添加；需要时可增加具名数组或新增整个优先级组，并由用户决定组的位置。

## 4. 具名黑白名单数组

本功能有独立的 `whitelist[]`、`blacklist[]`，同样使用具名匹配数组；`name` 只展示，`values[]` 才参与匹配。黑白名单是候选资格过滤，不是组内优先级，因此即使某个 `(mikanid, 来源Episode)` 只有一条记录也照常执行：

```yaml
whitelist: []
blacklist:
  - id: resolution-720p
    name: 720p
    enabled: true
    values: ["1280x720", "720p"]
```

执行顺序：

1. 命中任一启用黑名单项立即拒绝；黑名单优先于白名单和优先级数组排名。
2. 存在至少一个启用且 `values[]` 非空的白名单数组时，候选必须命中任意白名单数组；无有效白名单时不限制。
3. 只有通过黑白名单的候选才参与同集排名。

默认启用上述 720p 黑名单。因此默认情况下 720p 直接拒绝，不会在缺少 1080p 时自动下载；用户禁用或删除该黑名单后，720p 才作为低于 1080p 的有效候选。拒绝结果保存数组 ID、名称和实际命中的值。

## 5. 同集分组和单候选旁路

本功能只处理同一次 Mikan RSS ingest batch，分组发生在 TMDB 匹配之前。主分组键固定为：

```text
(mikanid, 来源Episode类型, 规范化来源Episode)
```

`mikanid` 必须从当前 RSS/Mikan URL 可靠解析，来源 Episode 也必须由标题解析器可靠取得。任一字段缺失、歧义或属于无法规范化的类型时，该记录不与其他项做优先级分组，直接进入后续元数据流程并保存旁路原因，不能仅凭相似标题猜测。

具名黑白名单先对 RSS 项执行。资格过滤后，每个分组按候选数量处理：

- `0` 条：该来源 Episode 没有 winner，不进入下载。
- `1` 条：直接选中，不执行任何优先级组，也不生成伪造的“优先级命中”；决策记录为 `SingleCandidateBypass`。
- `2` 条及以上：执行第 6 节的逐组淘汰算法。

被选中的 RSS item 才进入 Torrent 获取、TMDB Series/Season/Episode 匹配和下载流程。若后续多个不同来源分组收敛到同一规范 TMDB Episode，仍由全局 Episode claim/完成记录去重；本 RSS 优选不会回头重新比较，也不会用新批次的高优先级版本替换已开始或已完成下载。

## 6. 选择算法

对同一分组的候选使用逐组淘汰和即时短路，而不是先计算加权分数或强制执行全部组：

1. 按配置顺序读取第一个优先级组。
2. 在该组内按顺序检查每个具名数组，找出当前候选中命中其 `values[]` 的集合。
3. 第一个具有至少一个命中候选的具名数组生效：丢弃当前集合中未命中该具名数组的候选；同一候选同时命中本组后续数组不再处理。
4. 如果本组所有具名数组都没有命中任何当前候选，本组不淘汰任何项，继续下一组。
5. 每次淘汰后若只剩一条，立即选为 winner 并退出；后续优先级组不得再执行。
6. 全部优先级组执行完仍有多条时，按原 RSS 项目顺序选择第一项，再以稳定 source item ID/URL 指纹作为最终 tie-breaker。

使用默认四组预设时，例如：

- 简体 H.264 胜过繁体 H.265，因为字幕语言优先。
- 同为简体时，外挂 H.264 胜过内封 H.265，因为封装优先于编码。
- 前两个优先级组没有淘汰到一条时，编码组可以使 H.265 720p 胜过 H.264 1080p；但默认 720p 黑名单会先拒绝前者，所以默认实际由 H.264 1080p 获胜。

选择过程必须保存每个实际执行组的输入数量、首个命中的具名数组、命中值、淘汰项和剩余数量。短路后的组标记 `NotEvaluatedAfterWinner`，单候选旁路时所有组标记 `NotRequired`。不能由网络完成顺序、字典遍历顺序或数据库自增 ID 决定。

每个 RSS 分组只有一个 winner 进入后续 TMDB 流程。其他候选记录 `SuppressedByHigherPriority`、winner 和实际决胜优先级组/具名数组，不获取 Torrent、不调用 AI、不创建下载任务。winner 后续失败时默认按正常任务重试，不自动回到本批次提升 loser；若未来增加候选晋级，必须先确认 winner 未进入任何下载器并重新执行元数据/全局 Episode claim，首版不实现隐式晋级。

## 7. Web UI

Mikan 配置页增加“同集候选优先级”卡片：

- 独立总开关，并列显示旧 MikanTool 开关，说明两者互不控制。
- 优先级组的新增/删除/重命名/上下移动排序；每组内具名数组的新增/删除/重命名/上下移动排序和 `values[]` 编辑。上下移动必须可由键盘操作，首版不以拖拽作为唯一排序方式。
- 具名白名单/黑名单数组 CRUD 和启停，默认显示已启用的 720p 黑名单。
- 对空值、重复 ID、同具名数组重复值和跨具名数组重复值即时校验/提示；允许删除任一优先级组乃至全部组。
- 批次预览：输入多条 RSS title、mikanid 和来源 EP；服务端返回分组、名单结果、逐组输入/命中/淘汰/剩余数量、winner、未执行组和单候选旁路原因。
- 批次详情显示 SourceProfile revision、两个开关、每个 Episode 的候选表、winner 和 `SuppressedByHigherPriority`；Torrent URL 只显示不可逆指纹。
- 修改仅影响新批次；进行中批次只能显式取消后重新导入，不能原地重排。

配置属于 SQLite 中的 SourceProfile/规则业务数据，不写部署 YAML。Web 使用 revision/ETag 防止并发覆盖，每次保存创建审计和可回滚快照。

schema v25 将每个 revision 的优先级组、具名数组和 lowercase values 保存到独立关系型快照表。`GET /api/v1/rss-rules/{sourceProfileId}` 返回倒序快照摘要；`POST /api/v1/rss-rules/{sourceProfileId}/rollback` 接收 `expected_revision` 与 `target_revision`。回滚读取目标快照后按正常完整保存流程创建新 revision，旧快照不会被覆盖或删除；目标不存在返回 `rss_rule_snapshot_not_found`，并发版本过期返回 `rss_rule_revision_conflict`。

## 8. 验收重点

1. 验证优先级组和组内具名数组按配置顺序逐级淘汰；一旦剩一条立即短路，不调用后续组。覆盖新增、删除、清空和上下移动排序。
2. 验证所有默认别名、invariant lowercase、`简繁日` 重叠和未识别值。
3. 默认 720p 黑名单使只有 720p 的组不创建下载；禁用后 720p 可作为有效后备。
4. 黑名单覆盖白名单；有效白名单要求至少命中一项。
5. 同一批次相同 mikanid/来源EP 归组；组内资格过滤后只有一条时所有优先级组均不执行。不同mikanid或不同来源EP不在本阶段合并，即使之后映射到同一TMDB EP也交给全局去重。
6. loser 不获取 Torrent、不调用 AI、不创建下载；winner 后续失败不会在首版隐式晋级另一个 loser。
7. 两个开关的四种组合、批次快照、Web预览与实际执行一致。
8. 并发批次和后续 Episode claim 竞争均不产生双下载。
