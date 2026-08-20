# Mikan 文件名 EP 与可信偏移缓存

## 1. 作用范围

本功能只适用于来源类型明确为 `mikan` 的 RSS 任务。可信偏移缓存是主程序在 AI 调用前执行的本地短路功能，不属于独立 AI 测试程序，测试程序不得提供缓存开关、缓存状态、`mikanid/groupid` 输入或缓存命中模拟。

独立 AI 测试程序在 Torrent 上传后显示本地 `file_episode_candidate`，但发送给 AI 的 Prompt/请求不包含该字段。勾选“`Mikan RSS 来源`”只启用 AI 返回后的本地偏移预览；模型输入和响应都没有 `episode_offset`。

其他来源即使标题格式相似，也不能启用本功能。人工 `mikanid` 作品规则仍是最高优先级，命中人工 TMDB Series/Season/EP Offset 时不学习也不使用自动可信缓存。

## 2. 文件名解析

默认解析器以 AnimeGo `develop` 分支的 `assets/plugin/filter/Auto_Bangumi/raw_parser.py` 为兼容基线，完整移植为 NativeAOT 友好的 C#，不运行 Python。兼容解析层必须保留上游正则和字段语义，不能直接加入上游没有的年份保护、歧义拒绝或 `E04/EP04` 扩展；如后续确需安全收窄，必须建立独立候选策略层并分别测试。解析结果保留标题、季度、集号、字幕组文本、发布组、分辨率和来源等本地字段，不送入 AI。

`file_episode_candidate` 必须由后端使用规范化后的 Torrent 内部 basename 重新计算。API、WebUI、导入清单或插件传入的同名字段不能覆盖程序计算值。独立的 `FileEpisodeCandidateResolver` 读取兼容解析结果，再拒绝年份占位、非正数和明显的非正片/歧义结果；只有通过该安全层才形成候选。特别篇、Menu、PV、NCOP、NCED、Logo 等不能为了生成偏移而额外编造普通集号。

当前实现严格以 SourceProfile 的 `adapter` 判断作用域：只有精确的 `mikan` 才运行候选安全层并写入 `file_episode_candidate`；U2、TTG 或自定义非 Mikan profile 即使文件名完全相同也固定不写。兼容解析仍可保留上游会产生的原始结果（例如年份或末尾分辨率数字），但这些值只用于差分证明，不会绕过安全层进入可信 offset 学习。

## 3. AI 后本地处理

AI 请求中的每个视频文件始终只有：

```json
{
  "name": "[Group] Show [04].mkv",
  "size_bytes": 123
}
```

`file_episode_candidate` 和 `episode_offset` 均不出现在 AI Prompt、请求或响应中。主程序完成响应结构校验以及 TMDB Series、普通 Season、真实 Episode 二次验证后，读取本地候选并按以下方向计算：

```text
episode_offset = TMDB Episode Number - file_episode_candidate
TMDB Episode Number = file_episode_candidate + episode_offset
```

本地计算只读取已验证成功、属于普通季度且有 `file_episode_candidate` 的文件。一个任务内这些文件必须得到同一个偏移并且属于同一普通 TMDB Season，正数、零和负数都合法，满足时才形成缓存学习证据。没有候选、偏移不一致或跨季度时不形成证据，但不因此否定已经验证成功的逐文件 TMDB 映射。已经升级为 Trusted 的缓存命中走第 7 节的 AI 前本地短路。

AI 契约对所有来源保持一致，不包含文件名候选或偏移。Mikan 来源标志及候选不参与 Prompt/request identity；相同实际 AI 输入可以安全复用请求缓存，本地后处理仍按当前任务状态独立执行。

## 4. Mikan 字幕组作用域

Mikan URL `https://mikanani.me/Home/Bangumi/3951#583` 中：

- `mikanid=3951`；
- `groupid=583`，表示 Mikan 字幕组 ID。

自动可信缓存键严格为 `(mikanid, groupid)`。二者都必须是正整数；缺失或非法时不读、不写可信缓存。不同作品或不同字幕组不得共享证据，即使最后对应同一个 TMDB Series。

## 5. 配置与默认值

```yaml
metadata:
  mikan_trusted_offset_cache_enabled: false
  mikan_trusted_offset_required_episodes: 3
```

默认关闭。关闭时既不读取缓存跳过 AI，也不写入或累计样本。所需可信样本数可配置为 1～100，默认 3；这里计数的是不同的 `file_episode_candidate`，也就是从 Torrent 视频文件名解析出的 EP，同一个文件名 EP 重复出现不增加计数。设置为 1 会让一次已验证映射立即成为可信，适合明确理解风险的测试场景。

## 6. 证据和状态模型

每条成功证据至少保存：

- `mikanid`、`groupid`；
- 已由 TMDB 验证且大于 0 的 `tmdb_id`、大于等于 1 的普通 `season`；
- `file_episode_candidate`；
- 有符号 `episode_offset`；
- 对应解析运行 ID、Prompt 版本、验证时间和 TMDB 数据版本/响应时间。

缓存签名为 `(tmdb_id, season, episode_offset)`。没有有效 `tmdb_id` 或普通 `season` 的证据不得入库、不得升级为 Trusted，也不得用于 AI 短路。状态为：

- `Learning`：一致证据尚未达到当前配置的可信次数；
- `Trusted`：不同文件名 EP 的一致证据已达到当前配置的可信次数；
- `ConflictReset`：发现已验证冲突，旧证据归档后从当前新证据重新学习；
- `Disabled`：配置关闭，不读取和写入。

同一 `file_episode_candidate` 重复出现不增加计数。`tmdb_id`、`season` 或 `episode_offset` 任一不同都视为签名冲突：未可信时清空当前连续样本并以本次成功证据作为 `1/设定次数`；已可信时立即撤销可信状态、记录旧/新签名和原因，再以本次成功证据重新开始。不能使用多数投票，也不能静默保留旧缓存。

SQLite 必须以 `(mikanid, groupid, file_episode_candidate)` 建立证据唯一约束，并在同一事务中完成去重、冲突归档和状态升级，防止并发任务把重复 EP 计为多次。

## 7. 缓存命中与 AI 旁路

处理顺序固定为：

1. 应用最高优先级人工作品规则；
2. 确认来源为 Mikan、缓存开关开启且 `mikanid/groupid` 有效；
3. 查询 `(mikanid, groupid)` 的 `Trusted` 缓存；
4. 校验 Trusted 记录本身包含 `tmdb_id > 0`、`season >= 1` 和有符号 `episode_offset`；无效记录视为未命中并进入正常流程；
5. 所有需要整理的普通视频都必须有 `file_episode_candidate`，且计算结果 `candidate + offset > 0`；含无法归类的普通视频或歧义普通文件时整个任务不旁路；
6. 主程序不调用 AI，也不为本次缓存命中逐集调用 TMDB，直接构造 `{tmdb_id, season, episode=file_episode_candidate+episode_offset}` 本地映射；
7. 采用映射并记录 Episode 获取方式 `TrustedMikanGroupOffsetCache`、缓存签名和计算过程，本任务 AI 请求数为 0；可明确识别的特别篇、Menu、PV、NCOP、NCED、Logo 等仍按既定 `Other` 规则处理，不参与偏移计算；
8. 缺少候选、计算结果非正数、缓存字段无效、签名冲突或普通文件存在歧义时，不使用部分推导值，转回正常 AI/确定性流程。

可信状态来自达到当前设定次数的不同文件名 EP 的成功 TMDB 验证，因此后续命中不因“新 Episode 尚未出现在 TMDB”而逐集联网，也不会仅凭一次本地推导撤销可信。只有后续正常 AI/TMDB 流程成功产生不同的已验证签名时，才执行冲突重置。调高阈值后，证据数不足的新旧记录立即按 Learning 处理且不再命中；不需要删除历史证据。

主程序现已在自动元数据 worker 中执行这条旁路。命中前必须同时找到本地已验证过的 `anime_series + anime_seasons` canonical projection；投影缺失时记录 `trusted_offset_projection_missing` 并完整回退，不能联网补一半后仍称为缓存命中。命中后的本地 Episode 不伪造 `tmdb_episode_id`，但仍使用 `(tmdb_id, season, episode)` 参与全局 claim/completion 去重。Episode worker 完成正常 TMDB 验证后，只对同一任务内签名一致、具有正整数本地候选的正片写学习证据；跨季度、候选缺失、偏移不一致以及缓存推导出来的结果不会自我强化。

## 8. WebUI 与审计

正式 WebUI 至少显示：

- 是否为 Mikan RSS 来源；
- `mikanid`、`groupid` 和缓存键；
- 缓存开关（默认关闭）；
- 当前签名、不同文件名 EP 样本进度 `0/设定次数`～`设定次数/设定次数`；
- `Learning/Trusted/ConflictReset/Disabled`；
- 本次是命中、未命中、本地计算条件不满足还是签名冲突；
- AI 是否被跳过，以及命中后生成的本地 TMDB Series/Season/Episode 映射。

独立 AI 测试程序不显示、不配置也不持久化可信缓存；它只验证 Mikan 条件 Prompt、文件名候选、模型逐文件映射以及 AI 返回后的本地偏移计算结果。

删除缓存只删除自动证据/状态，不删除下载完成记录、人工规则或媒体文件。API Key、passkey、Torrent URL、announce 和 Cookie 不得进入证据表或日志。

管理端点为 `GET /api/v1/mikan/trusted-offsets`（支持可选 `mikanid/groupid` 正整数过滤）以及 `DELETE /api/v1/mikan/trusted-offsets/{mikanid}/{groupid}`。列表按候选签名显示 `Learning/Trusted/ConflictReset`、当前不同文件名 EP 数和当前配置门槛；删除在一个 SQLite 事务内仅清理目标键的 `mikan_offset_evidence` 与 `mikan_trusted_offsets`。静态 WebUI 使用同一端点展示进度，删除前明确提示不会影响人工规则、完成记录和媒体文件。

可信 Offset 黑名单独立保存在 `mikan_trusted_offset_blacklist`，支持三种范围：整个 `mikanid`、整个 `groupid`、精确 `(mikanid,groupid)`。任意范围命中后，自动流程不读取可信 Offset，也不累计观察证据或写入缓存。新增黑名单会在同一事务内清理受影响范围已有的 `mikan_offset_evidence` 与 `mikan_trusted_offsets`，避免解除黑名单后旧缓存立即恢复；移出后只能从新证据重新学习。管理端点为 `GET/POST/DELETE /api/v1/mikan/trusted-offset-blacklist`，WebUI 在可信 EP Offset 页面提供范围选择、ID输入和逐条移除。

## 9. 验收重点

1. 所有来源的 AI Prompt、请求和响应都没有文件名候选或偏移字段。
2. Mikan 候选由后端重新计算，调用方伪造无效。
3. AI 返回后本地计算的正、零、负偏移均通过；未验证 Episode 不参与计算，多个偏移或跨季度只禁止缓存学习，不否定已验证的文件映射。
4. 同键相同签名的不同文件名 EP 达到配置次数后升级为可信；重复 EP 不计数，默认门槛为 3。
5. 不同 `mikanid/groupid` 隔离；未达到当前配置次数不命中。
6. 学习期冲突重置，可信后冲突立即撤销并保存审计。
7. 默认关闭且关闭时零读写；多文件中的普通视频缺候选或存在歧义时不旁路，明确识别的附属文件按 `Other` 处理。
8. Trusted 记录必须包含有效 `tmdb_id`、普通 `season` 和偏移；命中后本地计算且不产生 AI/TMDB Episode 请求。字段无效或计算结果非正数时回退正常流程。
9. 可信缓存单元、集成、API 和 WebUI 测试只属于主程序；AI 测试程序中不存在该功能的配置、状态和测试替身。
