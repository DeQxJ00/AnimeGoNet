# qBittorrent file priority API verification

Date: 2026-07-22

## Scope

- Extend `IDownloadClient` with file-list and file-priority operations needed by the paused download gate.
- Parse qBittorrent `/api/v2/torrents/files` with source-generated JSON into stable index, normalized relative path, size, progress, priority, and wanted state.
- Call `/api/v2/torrents/filePrio` with explicit unique indexes and priority `0..7`.
- Reject malformed v1/v2 hashes, duplicate/negative indexes, empty index sets, and invalid priorities before HTTP.

## Evidence

- `QbittorrentClientTests.ListsFilesWithStableIndexesPathsAndPriorities`
- `QbittorrentClientTests.SetsFilePriorityWithExplicitIndexes`
- `QbittorrentClientTests.FileOperationsRejectUnsafeIdentityBeforeHttp`
- Existing login, torrent list/add, stop/start/delete, dispatcher, and synchronizer fake-contract tests continue to pass.

The full solution test suite and win-x64 NativeAOT smoke are required before commit. No portable qBittorrent task is created by this module; a real `filePrio` integration run remains gated on an explicit disposable Torrent fixture and cleanup plan.
