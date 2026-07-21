# Download organization path snapshot verification

Date: 2026-07-22

## Scope

- SQLite schema v9 adds nullable `download_root_path` and `save_root_path` columns for backward-compatible upgrades.
- Every newly confirmed qB dispatch writes both absolute paths into the same transaction that creates the download job and removes the staged lifecycle row.
- The download root is the selected named qB instance path; the save root is the AnimeGoNet library root. They are captured values, so later configuration/profile edits cannot redirect an in-flight task.
- Relative roots are rejected before the lifecycle transaction.

## Automated evidence

- `SchemaMigrationTests.DownloadJobsIncludeImmutablePathSnapshots`
- `StagedTorrentDispatcherTests.AddsPausedTorrentConfirmsHashAndCommitsDownloadJobBeforeDeletingStage`
- `StagedTorrentDispatcherTests.RelativeOrganizationRootRejectsDispatchTransactionAndKeepsStage`
- Full solution: Core 87, Data 44, App 97; total 228 passed.

Win-x64 NativeAOT publish generated native code and the schema v9 binary smoke passed. The tests use temporary paths and fake clients; no portable qBittorrent task or local TestSpace content is touched.
