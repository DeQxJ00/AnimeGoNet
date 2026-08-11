# 上游 Go Linux 基线验证 — 2026-08-11

## 结论

固定的 `wetor/AnimeGo develop@c7475dfc55a374cd0dd08821bf17125dab1e3145`
已在 Ubuntu 24.04 x86_64 CT 的官方 `golang:1.22.10-bookworm` 容器内完成串行
Linux amd64 基线测试，结果为通过、退出码 0。

脱敏报告保存在本机非 Git 测试目录：

`TestSpace/animegonet_data/upstream-go-linux/20260811-1786421162841-409414/report-official/`

## 固定输入和执行参数

- 上游仓库通过本机干净 Git bundle 传入；未跟踪文件不进入 bundle。
- 脚本在执行前要求实际 HEAD 精确等于 `c7475dfc55a374cd0dd08821bf17125dab1e3145`。
- 容器为 `docker.io/library/golang:1.22.10-bookworm`，拉取摘要
  `sha256:71ea1e48de3984ff13485d05cdde09a46eb7bd5d4ea8573310f54747755164c9`，
  运行平台为 linux/amd64。
- Docker Hub 直连在本机网络被重置后，按所有者提供的
  `http://192.168.1.180:7074` 下载代理重新从官方地址拉取；先前镜像地址的运行已
  停止、作废并删除，没有纳入报告。
- 测试命令为
  `CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go test -p 1 -count=1 -json ./...`。

## 机器报告

`summary.json` 记录：

| 字段 | 值 |
|---|---|
| result / exit_code | `passed` / `0` |
| actual_commit | `c7475dfc55a374cd0dd08821bf17125dab1e3145` |
| go_version | `go version go1.22.10 linux/amd64` |
| event_count | `3109` |
| skip_event_count | `100` |

`events.jsonl`、`stderr.log` 和 `summary.json` 的 SHA-256 已按 `SHA256SUMS` 在取回后
重新计算并全部匹配。Skip 是上游测试自行报告的事件，未被脚本改写成成功；最终通过
以原始 `go test` 退出码为准。

## 清理

验收后已删除本次唯一远端 `/var/tmp/animego-go-baseline-<run>`、官方 Go 镜像和
本地临时 bundle。Docker daemon 的临时下载代理环境已恢复为空。报告目录保留且由
TestSpace ignore 边界保护，不提交事件流、stderr 或机器环境数据。
