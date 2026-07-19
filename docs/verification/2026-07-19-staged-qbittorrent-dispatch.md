# Staged qBittorrent dispatch verification — 2026-07-19

## Scope

- SQLite schema v4 adds a `ready/dispatching` state, unique lease token, lease expiry, attempt count, retry time, safe failure code, dispatch index, and one-download-job-per-task constraint.
- The background worker atomically claims one staged task, restores abandoned leases, and uses the immutable downloader ID and configured per-instance download path.
- It connects to qBittorrent, checks for the info-hash before add, adds missing Torrents paused with identifiable category/tags, then polls qB until the same hash is visible.
- Only confirmed receipt creates `download_jobs`, changes the ingest task to `download_queued`, removes the staged lifecycle row, and deletes the temporary Torrent.
- Network/config/staging failures retain the file and release the lease with a stable non-secret code and retry time. A crash after qB add is idempotent because the next lease checks the same hash before adding.

## Automated evidence

```text
dotnet test AnimeGoNet.slnx --no-restore
Core: 34 passed
Data: 13 passed
App: 35 passed

dotnet build AnimeGoNet.slnx -c Release --no-restore
0 warnings, 0 errors

dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 -p:PublishAot=true
Generating native code

eng/smoke-native.ps1 -Executable artifacts/qb-dispatch-aot-win-x64/AnimeGoNet.App.exe
Native smoke passed (schema v4, hosted worker startup, secure ingest rejection, static WebUI)
```

The tests cover concurrent claim exclusion, paused add fields and path routing, source/file-strategy tags, qB confirmation, atomic download-job transition, staging deletion ordering, existing-hash idempotency, and secret-free retry after a transport exception containing credentials in its message.

## Safety boundary

All dispatcher tests use an in-process fake `IDownloadClient`; default CI and this verification run do not contact the user's portable qBittorrent or create a real Torrent task. Real add/list/state/file-priority/delete testing remains an explicit local/container integration action with recognizable AnimeGoNet category/tags and cleanup.
