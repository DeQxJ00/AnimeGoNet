# Multi-file organization recovery verification (2026-08-01)

## Scope

This module makes multi-file move execution deterministic and proves recovery from
a target conflict after an earlier file has already committed. Tests use disposable
source/save roots, SQLite, the real organization processor and safe mover, plus a
fake downloader. They do not access user files, qBittorrent, Torrent URLs, passkeys,
cookies, TMDB, Bangumi, or AI providers.

## Behavior

- Persisted file operations are returned in stable Torrent relative-path/file-ID
  order, independent of random operation IDs or caller plan order.
- Same-volume moves prefer atomic rename. The forced cross-volume branch copies to
  a task-owned partial, verifies expected length and SHA-256, atomically commits the
  target, and only then deletes the source.
- An existing target with different bytes produces `target_conflict` and preserves
  both source and target.
- In a two-Episode task, Episode 1 can finish before Episode 2 conflicts. The job is
  released as retryable with one `completed` and one `pending` operation, while
  completion records remain empty.
- After the external conflict is removed, retry skips the already completed first
  operation, moves only the pending second file, then atomically creates both
  Episode completions and enters the independent downloader cleanup stage.

## Targeted verification

- `MediaOrganizationStoreTests.OperationsAreReturnedInStableTorrentPathOrder`:
  1 passed.
- `MediaOrganizationProcessorTests.MultiFileConflictResumesOnlyPendingOperationInStablePathOrder`
  plus all `SafeFileMoverTests`: 6 passed.

## Release gate

- Release solution build passed with zero warnings and zero errors.
- The complete suite passed 1092/1092: Plugin Abstractions 11, Core 324,
  Data 172, App 585.
- `win-x64` NativeAOT publish generated native code. The published executable
  started on isolated port 6193 with disposable paths and background workers
  disabled; `/api/v1/status` reported `native_aot=true`, RID `win-x64`, and schema
  v36. The exact published process was identified by executable path and stopped
  after the smoke.
