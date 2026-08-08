# 上游领域模型、枚举与错误映射

基准固定为 `wetor/AnimeGo@c7475dfc55a374cd0dd08821bf17125dab1e3145`。机器可读清单位于
[`baseline/UPSTREAM_DOMAIN_CONTRACTS.psv`](baseline/UPSTREAM_DOMAIN_CONTRACTS.psv)，逐个覆盖上游
`internal/models`、`internal/constant`、`internal/exceptions` 和 `pkg/exceptions` 的所有 Go 文件及导出类型。

## 判定规则

- `preserved`：可观察值或解析契约保持一致，但不要求保留 Go 类型名。
- `replaced`：业务语义保留，改用适合 SQLite、Minimal API 和 NativeAOT 的闭合 DTO、状态机或稳定错误码。
- `excluded`：旧实现依赖反射/Python/进程内 callback 等已明确不进入新架构的机制；清单必须给出替代边界和理由。
- 每行至少有一个真实目标文件。契约测试会验证上游目录没有未登记文件、每个导出 Go 类型都被逐名登记、目标文件存在，且固定提交未被悄悄更换。

## 关键拆分

旧 `AnimeEntity` 同时混合 Mikan、Bangumi、TMDB、Torrent 和整理状态。AnimeGoNet 将其拆为来源证据、
TMDB Series/Season/Episode 权威身份、逐文件候选、解析运行/尝试、下载作业和作品库记录。这样人工覆盖、
失败回退、`Other`、去重与 WebUI 进度都不会互相冒充。

旧 `DownloadStatus`、`RenameTask` 和 callback/channel 状态被替换为 SQLite 作业、租约、事件和逐文件结果；
配置变化或进程重启不会丢失实际使用的下载/保存根目录和失败阶段。

旧 `ExistError`、`NotFoundError`、`ParseFailedError` 的跨包装识别由
`IStableError`/`StableErrorSemantic` 承担。正常的重复与不存在仍优先作为显式查询/claim 结果，不用异常控制流程；
RSS、Torrent、Mikan HTML、Data manifest 和 Cron 等结构解析异常统一公开 `ParseFailed` 语义与安全稳定码。

## NativeAOT 边界

`models.Object` 的 `map[string]any`、Python 插件类型、WaitGroup/channel/callback、MD5 插件资产复制不做机械移植。
JSON 边界使用闭合 DTO 和 source-generated context；插件使用编译期 C# 接口/manifest；并发与恢复由宿主、取消令牌、
SQLite 租约和后台 worker 管理。
