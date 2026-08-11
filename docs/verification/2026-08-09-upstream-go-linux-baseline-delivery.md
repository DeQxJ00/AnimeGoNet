# Upstream Go Linux baseline delivery — 2026-08-09

> 历史状态说明：本文记录 2026-08-09 仅生成 job 时的边界。该门禁已于
> 2026-08-11 在 Ubuntu 24.04 x86_64 CT 的官方 Go 1.22.10 容器内通过；当前证据见
> `2026-08-11-upstream-go-linux-baseline.md`。

## Generated job

`.github/workflows/upstream-go-baseline.yml` defines a dedicated
`ubuntu-24.04` job inside `golang:1.22.10-bookworm`. It checks out only the
pinned `wetor/AnimeGo` commit
`c7475dfc55a374cd0dd08821bf17125dab1e3145`, with persisted checkout
credentials disabled.

`eng/capture-upstream-go-baseline.sh` fails closed when the repository or HEAD
is wrong, then runs:

```text
CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go test -p 1 -count=1 -json ./...
```

The command is serial to avoid the Windows baseline's observed transient
parallel compile failure. It never enables the upstream qB tests that are
unconditionally skipped and does not know any local qB endpoint or credential.

## Report contract

The job retains these files for 30 days even when `go test` fails:

- `events.jsonl`: the unmodified machine-readable Go test stream;
- `stderr.log`: compiler/runtime diagnostics separated from JSON events;
- `summary.json`: schema version, pass/fail, exit code, expected/actual commit,
  Go version and event/skip counts;
- `SHA256SUMS`: hashes of all three report inputs.

The script returns the original `go test` exit code after writing the report,
so an artifact cannot hide a failing baseline.

## Local verification

`UpstreamGoBaselineDeliveryContractTests` parses the workflow YAML and locks
the container, commit, credential boundary, serial command, report files,
always-upload behavior, retention and failure propagation. The focused Release
suite passed 2/2, `bash -n` accepted the capture script, and the complete .NET
solution passed 1456/1456 with zero failures and zero skips. `git diff --check`
and the whitespace formatter restricted to the new C# contract test passed.

Per the project owner's instruction, no Docker/container command was run and no
remote result is claimed. This increment is generated delivery capability and
is explicitly marked unverified until the owner runs it.
