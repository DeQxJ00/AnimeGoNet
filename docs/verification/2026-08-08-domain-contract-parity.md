# 领域模型、枚举与错误契约验收

日期：2026-08-08
上游：`wetor/AnimeGo@c7475dfc55a374cd0dd08821bf17125dab1e3145`

## 范围

- `docs/baseline/UPSTREAM_DOMAIN_CONTRACTS.psv` 逐个登记固定上游
  `internal/models`、`internal/constant`、`internal/exceptions`、`pkg/exceptions`
  的全部 Go 文件与导出类型。
- 每个单元明确 `preserved`、`replaced` 或 `excluded`，并指向实际存在的 C# 目标；
  反射 `map[string]any`、Python、callback/channel 等例外记录 NativeAOT 替代边界。
- 新增 `IStableError` 与 flags 型 `StableErrorSemantic`，保留 Go marker error 跨包装异常查询语义。
  RSS、Torrent、Mikan HTML、Data manifest 和 Cron 结构错误均公开 `ParseFailed`。
- 重复、未找到等正常业务分支继续使用显式 query/claim 结果，不退回异常控制流程。

## 自动验收

`UpstreamDomainContractTests` 在配置 `ANIMEGO_UPSTREAM_REPO` 时先验证独立上游仓库 HEAD，
再比较四个目录的实际 `.go` 文件集合与每个文件的导出 `type` 集合。新增、删除或漏映射都会失败。
无上游目录时仍验证清单格式、处置类型、目标路径存在和固定提交头。

`StableErrorCodeTests` 验证直接/包装异常的稳定码和 flags 语义、所有已接入结构解析异常、
无稳定契约的普通异常，以及第三方无效稳定码 fail closed。

最终提交前执行：

- 领域/错误定向测试：12/12；
- 带固定上游目录的全解决方案 Release tests：1404/1404；
- WebUI tests：13/13；Release build：0 warning / 0 error；
- win-x64 NativeAOT restore/publish：成功生成原生代码；
- 发布原生程序首次启动、legacy YAML 与 AI metadata smoke：三项均通过，schema v38；
- changed-file format、diff、敏感值与工作树检查：提交前通过。
