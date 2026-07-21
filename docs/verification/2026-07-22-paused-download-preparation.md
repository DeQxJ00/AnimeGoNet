# Paused download preparation and episode dedup verification

Date: 2026-07-22

## Scope

- SQLite schema v8 adds a persistent `pending/preparing/completed` download-preparation state, exclusive lease, expiry recovery, attempt/retry fields, safe failure code, and per-task-file qB index/priority/wanted projection.
- Confirmed staged dispatch now enters `download_preparing`; both an existing same-hash task and a newly added task receive an explicit pause call before the durable hand-off.
- Metadata Series/Season/Episode resolution and episode claims run while qB remains paused.
- The preparation worker pauses again, requires an exact file-count/index/path/size match, sets `duplicate`/`ignored` files to priority 0 and `episode`/`other` files to priority 1, then resumes only when a wanted file exists.
- An all-duplicate task is durably marked `download_skipped_duplicate`, is never resumed, and is removed from qB only with `deleteFiles=false`. Manifest mismatch, incomplete metadata, or a post-resume SQLite failure trigger another best-effort pause before the durable lease is released for safe retry.
- Static WebUI maps the new preparation, allowed-download, and duplicate-skip business states to explicit labels.

## Automated evidence

```text
dotnet test AnimeGoNet.slnx --no-restore --verbosity quiet
Core: 87 passed
Data: 43 passed
App: 96 passed
Total: 226 passed

dotnet build AnimeGoNet.slnx -c Release --no-restore
0 warnings, 0 errors

dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 -p:PublishAot=true --no-restore
Generating native code

eng/smoke-native.ps1 -Executable artifacts/download-preparation-win-x64/AnimeGoNet.App.exe
Native smoke passed (schema v8)
```

Key tests:

- `DownloadPreparationStoreTests.ConcurrentWorkersClaimPreparingTaskOnlyOnce`
- `DownloadPreparationStoreTests.ReleasedClaimHonorsRetryTimeAndCanBeClaimedAgain`
- `DownloadPreparationStoreTests.ExpiredLeaseIsRecoveredWithoutResumingDownloader`
- `DownloadPreparationProcessorTests.MixedTorrentDisablesOnlyDuplicateAndIgnoredFilesThenResumes`
- `DownloadPreparationProcessorTests.AllDuplicateTorrentStaysStoppedAndIsRemovedWithoutDeletingFiles`
- `DownloadPreparationProcessorTests.ManifestMismatchKeepsTorrentStoppedAndSchedulesSafeRetry`
- `StagedTorrentDispatcherTests.ExistingHashIsConfirmedWithoutAddingDuplicateTorrent`

## Safety boundary

All preparation and priority tests use fake `IDownloadClient` implementations and disposable SQLite databases. The NativeAOT smoke disables background workers and uses a disposable data directory. No portable qBittorrent connection was made, no Torrent was created, and no local download/profile/credential/passkey content entered Git.

Real qB add/filePrio/resume/delete and Docker shared-path verification remain explicit integration work. They require a user-approved disposable Torrent input, recognizable AnimeGoNet category/tag, a predeclared expected file manifest, and a cleanup procedure that removes only test-owned qB objects while preserving downloaded files unless the test explicitly owns them.
