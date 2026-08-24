# Other 文件重新适配

该功能用于已经完成整理、但一个或多个文件因 Episode 无法确认而位于
`<TMDB 动画名>/Sxx/Other/` 的任务。它不是“重置整个任务”，也不会重新下载。

## 行为边界

- 入口：任务库中状态为 `organized` 且 `Other > 0` 的任务显示“重新适配 Other”；筛选栏选择“文件状态 → 含 Other”可只显示含 Other 文件的任务。
- 预览：`GET /api/v1/metadata/tasks/{taskId}/other-readaptation/preview`。
- 执行：`POST /api/v1/metadata/tasks/{taskId}/other-readaptation`。
- 保留任务、来源证据、既有 Metadata Run、策略时间线和 AI 调用/Debug 日志。
- 保留已经确认的 TMDB Series 和普通 Season，只清除目标 Other 文件的 Episode 结果。
- 重新执行当前 Episode 确定性规则；满足现有门禁时可调用当前配置的统一 AI 流程，AI 结果仍须经 TMDB Episode 验证。
- 成功后以旧 `Other` 文件作为安全移动源，直接进入规范 Episode 路径；再次无法确认时文件原地保留在 `Other`。
- 不重建 Torrent、不重新下载、不改变下载准备状态，也不调用 qB pause/delete/cleanup。

## 人工审核与 TMDB 修正

重新适配执行状态和人工审核状态相互独立。媒体整理完成后，任务仍是业务上的
`organized`，WebUI 另外显示“重新适配待审核”；审核通过后显示“重新适配审核完成”，
并保留只读的适配前后对照、执行完成时间与审核完成时间。

待审核且最终仍为 `Other` 的单个文件可手工填写 TMDB Series ID、普通 Season 和
Episode。服务端必须通过 TMDB Series/Season/Episode 三个 endpoint 验证规范身份后
才允许写入；验证失败不修改任务、SQLite 状态或媒体文件。成功结果记为
`manual_review_override`，不冒充确定性规则或 AI 证据。

- 新 TMDB Episode 尚未完成且未被其他任务占用：重新建立 Episode claim，并使用已
  整理的 Other 文件重新进入媒体整理。独占文件按原策略 move，旧 Other 路径随移动
  消失；共享路径按 copy 语义保留共享源。
- 新 TMDB Episode 已有完成记录或被其他任务占用：文件继续保留在 Other，记录明确
  的重复原因，绝不自动删除。用户核对后仍可批准这次审核。
- “审核通过”只完成审核，不隐式删除 Other。需要删除重复或不需要的实体文件时，
  必须另走显式、可预览的删除操作。

### AI 提议更换 TMDB Series

Episode 阶段已经确认 Series/Season 后，AI 偶尔会因日文名、中文名分别命中 TMDB
中的两个重复 Series。该情况使用独立原因
`ai_tmdb_multilingual_series_conflict_review_required`，不再混入普通 AI 匹配失败：

1. 原 Series 保持不变，AI 返回值不能直接覆盖数据库、目录或 NFO；
2. 主程序忽略原 Series 约束，再对 AI 候选执行完整的 TMDB Series、普通 Season、
   Episode 身份验证；验证失败仍按真实 TMDB/网络/协议错误处理；
3. 首版仅对恰好一个正片 Episode 的任务建立变更提议，schema v60 保存原 Series/Season、
   经验证的候选 Series/Season/Episode、请求时间与接受/拒绝决定；
4. 文件先按独立失败原因整理到当前季度 Other。任务完成后，审核弹窗以表格显示原
   TMDB 与 AI 候选，提供“同意 AI 的 TMDB ID 变更”和“拒绝并保留 Other”；
5. 同意时服务端再次在线验证候选并复用人工 TMDB 修正的 claim/去重/共享文件安全
   整理；拒绝时不移动媒体。接受后的重新整理完成仍需要最后一次人工审核，拒绝则
   直接记录人工决定并允许按既有显式删除语义处理任务。

此流程不修改或追加正式 AI Prompt，只改变主程序对已验证候选的分类和人工门禁。

## 安全门禁

首版只支持任务快照中的 `move` / `wait_move`。执行前必须同时满足：

1. 任务已经 `organized`，下载整理状态已经 `completed`；
2. 没有活动中的元数据解析租约；
3. 每个 Other 文件仍存在于已记录路径，且字节数与 Torrent 清单一致；
4. 每个目标路径只被一条完成的文件操作引用；共享路径不会自动移动；
5. 文件仍具有大于零的 TMDB Series/Season。

任一门禁失败，API 返回稳定冲突响应且不修改任务、数据库文件状态或媒体文件。

## 持久化与恢复

SQLite schema v47 的 `other_file_readaptation_jobs` 按文件保存旧媒体路径、原始
`other_reason`、请求时间和完成时间。重新适配期间，媒体整理只领取这些文件，并把旧
媒体路径作为源路径。全部文件操作和完成记录提交后，任务直接恢复为 `organized`，
不会再次进入下载器清理阶段。schema v49 固化审核前的 TMDB/归类快照；schema v50
为人工修正保存独立的 `manual_review_override` 来源。schema v60 的
`ai_series_change_reviews` 保存 AI 跨 Series 候选与人工接受/拒绝审计。

## 验收

- Data tests：状态重入、TMDB Series/Season 保留、共享路径拒绝、源路径覆盖、完成后不产生 qB cleanup。
- API test：真实临时文件通过预览，执行后文件仍在原位等待 Episode worker，下载准备保持 `completed`。
- WebUI：任务卡仅在 `organized + Other` 时显示按钮，先显示文件与旧原因确认，再执行。
- 人工修正：Data tests 覆盖唯一目标重新入队和重复目标保留 Other；API test 使用 fake
  TMDB 完成三段验证并证明审核完成后仍可只读查看结果；Web contract 覆盖手工表单及
  Other 不自动删除说明。
- AI Series 变更：Core/App tests 覆盖原 Series 冲突分类与候选的 TMDB 三层再验证；
  Data tests 覆盖候选快照、审核对照和拒绝后 Other 不移动；Web contract 覆盖两个
  明确决定按钮，普通“审核通过”接口不能绕过待决定候选。
- NativeAOT：随正常 win-x64 发布 smoke 验证端点和静态 WebUI。
