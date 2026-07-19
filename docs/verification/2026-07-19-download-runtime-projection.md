# Download runtime projection verification — 2026-07-19

## Scope

- SQLite schema v5 stores canonical qBittorrent state, progress, byte counts, speed, ETA, seeds, peers, snapshot time, stale flag and a monotonic revision on each `download_job`.
- `downloader_runtime_state` stores per-instance connectivity, stable failure code and last successful poll without persisting credentials or source/download paths.
- A per-instance coordinator serializes dispatch and polling operations for the same qBittorrent instance while allowing different named instances to progress independently.
- The background synchronizer polls every two seconds while jobs are active and every ten seconds while idle. One instance failing does not block another; its last snapshot remains visible as stale and can recover on a later successful poll.
- `GET /api/v1/downloads` exposes the canonical downloader and AnimeGoNet business states. The static TypeScript/HTML/CSS UI renders progress and stale/offline status with text-only DOM APIs.
- qBittorrent reporting 100% maps the ingest task to `downloaded`, not to the final completed-record state. Media matching, organizing and NFO/database completion remain separate later stages.

## Automated evidence

```text
dotnet test AnimeGoNet.slnx --no-restore
Core: 34 passed
Data: 17 passed
App: 38 passed

dotnet build AnimeGoNet.slnx -c Release --no-restore
0 warnings, 0 errors

dotnet restore src/AnimeGoNet.App/AnimeGoNet.App.csproj --runtime win-x64 \
  --source "$env:USERPROFILE/.nuget/packages" -p:NuGetAudit=false -p:PublishAot=true
dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj --configuration Release \
  --runtime win-x64 --self-contained true --no-restore \
  --output artifacts/download-runtime-aot-win-x64 -p:PublishAot=true
Generating native code

eng/smoke-native.ps1 -Executable artifacts/download-runtime-aot-win-x64/AnimeGoNet.App.exe
Native smoke passed (schema v5, secure ingest rejection, static WebUI)
```

Tests cover qB JSON seeds/peers mapping, healthy progress persistence, error-to-downloading recovery, offline stale preservation, independent healthy/failing instances, per-instance single-in-flight coordination, typed downloads API output, and the absence of passkeys/tokens/absolute paths from the response.

The in-app browser's local-URL security policy rejected automated visual navigation to `127.0.0.1`; no workaround was used. Kestrel API/static-resource tests and the NativeAOT HTTP smoke remain the automated UI evidence for this module. A manual browser E2E pass stays open in the main TODO.

## Safety boundary

Unit/API tests use fake `IDownloadClient` implementations. The NativeAOT smoke sets `background_workers_enabled=false`, uses a random loopback port and disposable directories, and does not log in to or create tasks in the user's portable qBittorrent sandbox. Real Torrent operations remain explicit integration tests only.
