# Downloader cleanup callback parity (2026-08-01)

## Upstream behavior and intentional deviation

AnimeGo `develop` wires `clientnotifier`'s rename-complete callback to
`downloaderSrv.Delete(hash)` in `cmd/animego/main.go`. The downstream
`internal/animego/downloader/downloader.go` implementation calls the client with
`DeleteFile:true`. AnimeGoNet does not copy that destructive coupling: qBittorrent
never owns recursive deletion of captured download or media-library paths.

The replacement is a durable two-stage boundary:

1. move/link, NFO and directory sidecars complete;
2. SQLite atomically writes completion/alias and enters `organizing_cleanup`;
3. a separate cleanup lease calls qB with `deleteFiles=false`;
4. only a successful qB response changes the task to `organized`.

## Failure and recovery fixtures

- `DownloaderCleanupCallbackRetriesWithoutDeletingOrganizedMedia` injects a fake
  qB HTTP failure after the real temporary file has moved and the completion is
  durable. The task remains `organizing_cleanup`, the media target remains present,
  and every attempted qB deletion has `DeleteFiles=false`.
- The failure opens the instance circuit breaker. A successful explicit health
  probe closes the circuit, the persistent retry time is advanced, and the next
  cleanup lease removes only the qB task. The media target and completion remain.
- `FailedCleanupCanBeReleasedAndCompletedByNextLease` proves the Data store can
  release a failed cleanup lease, honor its retry time, issue a new cleanup lease,
  and commit `organized` exactly once.

Targeted result: App 1/1 and Data 1/1 passed. The complete Release gate also
passed: build 0 warnings/0 errors; Plugin Abstractions 11/11, Core 324/324,
Data 173/173, and App 586/586 (1094/1094 total, 0 skipped).

Tests use fake qB and disposable SQLite/files only; no TestSpace instance,
Torrent URL, passkey, cookie, or user credential is accessed. Production source
is unchanged from the preceding NativeAOT-verified commit; this module adds
deterministic failure/recovery evidence and traceability documentation.
