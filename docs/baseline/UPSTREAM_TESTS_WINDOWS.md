# 上游 Windows 测试基线

- 基线：`upstream/develop@c7475dfc55a374cd0dd08821bf17125dab1e3145`
- 日期：2026-07-19
- 主机：Windows x64，Go 1.22.10
- 上游 `Test*` 数：178

用户全局 Go 目标为 `GOOS=android`、`GOARCH=arm64`，直接运行 `go test ./...` 会生成无法由 Windows 执行的 Android ARM64 测试二进制。基线命令必须局部覆盖目标，不能修改用户全局设置：

```powershell
$env:GOOS='windows'
$env:GOARCH='amd64'
$env:CGO_ENABLED='0'
go test ./...
```

首次并行执行中，除 `third_party/qbapi` 的一次无诊断并行编译失败外，其余包通过；随后单独执行 `go test -count=1 -v ./third_party/qbapi` 通过（真实 qBittorrent 用例按上游门禁跳过）。串行 `go test -p 1 ./...` 消除了该波动，但 `internal/pkg/request` 因本机安全策略禁止绑定上游硬编码的 `127.0.0.1:8080` 而失败：

```text
listen tcp 127.0.0.1:8080: bind: An attempt was made to access a socket in a way forbidden by its access permissions.
```

除该环境白名单项外，串行全量包通过或按上游测试门禁跳过。AnimeGoNet 测试必须使用操作系统分配的临时端口，不能复制固定 8080 的测试耦合。

移植测试不得依赖用户全局 GOOS/GOARCH，并应优先复用 `test/testdata`、`internal/pkg/torrent/testdata` 与插件资源中的 fixture。
