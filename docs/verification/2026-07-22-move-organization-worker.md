# Mikan move organization worker verification

Date: 2026-07-22

## Connected flow

1. Claim a completed `move` download with the captured download/save roots.
2. Connect to the immutable qB instance and pause the task again.
3. Persist deterministic per-file source/target plans and run the recoverable safe mover.
4. Atomically write series-root `tvshow.nfo` with verified TMDB ID and available Bangumi ID.
5. Transactionally insert episode completion records and complete episode claims only after every file/NFO succeeds.
6. Enter a separate cleanup claim; call qB delete with `deleteFiles=false`; only then mark the task `organized`.

Failures produce stable codes, best-effort pause the task, and release the exact stage with a retry time. A crash after a file move, NFO commit, completion transaction, or qB cleanup is handled by the idempotent next stage.

## Evidence

- `MediaOrganizationProcessorTests.MoveWritesNfoAndCompletionBeforeSafeDownloaderCleanup` uses a real disposable filesystem and fake qB registry. It verifies the canonical target bytes, XML-readable TMDB/Bangumi NFO, intermediate cleanup state, completion count, source removal, and `deleteFiles=false`.
- Existing `SafeFileMoverTests` and `MediaOrganizationStoreTests` cover cross-volume verification, conflicts, path escape, stage leases, retry timing, and completion gating.
- Static WebUI assets expose `organizing_cleanup` and `organized` labels.
- Full solution: Core 93, Data 47, App 105; total 245 passed.
- Release build completed with 0 warnings/0 errors; win-x64 NativeAOT generation and schema v10 binary smoke passed.

No portable qBittorrent process or TestSpace path was contacted. Real qB/Docker E2E remains explicitly gated on a disposable Torrent fixture and cleanup plan.
