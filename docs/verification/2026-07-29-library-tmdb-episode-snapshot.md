# 作品库 TMDB Episode snapshot（2026-07-29）

## 业务边界

作品库不能根据 `episode_count` 猜造 `1..N` 网格。本增量固定以下流程：

1. TMDB Series details 中的季度摘要只用于名称/日期候选选择。
2. P4/P3 联合匹配选中普通季度后，再请求官方 Season endpoint。
3. 响应必须保持 Series/Season 身份一致，Episode ID 与 Episode Number 各自唯一，且只接受正普通 Episode。
4. Series、Season、完整 Episode snapshot 与任务季度投影在同一 SQLite 事务写入。
5. “待补全 TMDB”人工恢复使用同一 snapshot writer；多个 fallback 合并到同一季度不会产生重复 Episode。

`TmdbSeason.Episodes=null` 只表示调用方没有取得完整 snapshot，允许 P2/P1 本地季度例外继续工作，但不得据此生成作品库 EP 网格。非空 snapshot 的数量必须与 Season `episode_count` 相同。

如果一个 TMDB Episode ID 已绑定到另一规范 Series/Season/Episode，写入前返回稳定业务冲突并回滚，不把 SQLite 唯一约束细节暴露到上层。

## 验证

- TMDB Season endpoint 映射完整 Episode 数组，并拒绝重复 ID/集号。
- 联合 Series/Season resolver 断言成功时确实调用 Season endpoint，而不是直接接受 Series details 摘要。
- 正常季度完成精确保存 Episode ID、Number、Name 与 Air Date。
- 待补全恢复保存完整 12 集 snapshot，重复 fallback 仍只有 12 行。
- 既有 Episode ID 冲突测试继续得到稳定 `InvalidOperationException`，事务保持原样。
- 完整 Release 回归：621/621 通过（插件 11、Core 215、Data 102、App 293）。
- win-x64 `PublishAot=true` 完成 `Generating native code`，0 warning / 0 error；原生进程通过 schema v23、SQLite、静态 WebUI、安全 ingest 与 NativeAOT capability smoke。
