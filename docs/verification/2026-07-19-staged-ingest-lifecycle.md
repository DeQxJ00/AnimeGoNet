# Staged ingest lifecycle verification — 2026-07-19

## Scope

- `/api/v1/ingest` and legacy `/api/download/manager` now invoke the secure Torrent staging service while the authenticated request and secret URL are still in memory.
- SQLite schema v3 adds `staged_torrents`; the transaction writes the ingest task, safe relative file projection, padding disposition, info-hash, total size, random staging leaf name, and expiry together.
- The response exposes status `staged`, info-hash, file count, route identity, and the pre-existing irreversible URL fingerprint. It never exposes the staging path, URL path/query, announce, or passkey.
- Per-item staging policy failures remain batch-local and return stable failure codes without secret-bearing exception text.
- Startup recovery changes expired staged tasks to `failed/staging_expired`, removes their lifecycle rows, deletes the exact safe staging leaf, and separately removes orphan files by TTL.

## Automated evidence

```text
dotnet test AnimeGoNet.slnx --no-restore
Core: 34 passed
Data: 12 passed
App: 32 passed

dotnet build AnimeGoNet.slnx -c Release --no-restore
0 warnings, 0 errors

dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 -p:PublishAot=true
Generating native code

eng/smoke-native.ps1 -Executable artifacts/staged-ingest-aot-win-x64/AnimeGoNet.App.exe
Native smoke passed (schema v3, secure ingest rejection, static WebUI)
```

Tests verify the atomic staged/task/file projection, padding handling, expiry transition, cleanup file name, staged API response, Mikan/U2 downloader routing, legacy envelope, per-item security rejection, and absence of the submitted passkey URL in response/persisted route data.

## Deferred boundary

This increment does not submit to qBittorrent. The next worker must atomically claim a staged row, re-check completion/episode claims, open the staged file, connect to the task's immutable downloader ID, add it paused, confirm qB receipt/info-hash, create the download job, and only then delete the lifecycle row and staging file. Any pre-confirmation failure must leave a retryable staged record or an explicit safe failure classification.
