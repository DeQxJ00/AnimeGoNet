# Media organization progress verification

## Scope

- schema v37 stores the authoritative media-organization phase, completed units and total units on each download job.
- The worker reports rename planning, media transfer, associated-subtitle transfer, NFO writes, directory database/index writes and downloader cleanup. File-transfer retries derive their initial count from completed immutable file operations.
- State/progress triggers reject contradictory `not_required`, `cleanup` and `completed` combinations. Migration backfills existing completed and cleanup jobs without inventing progress for pending work.
- Expired cleanup leases are recovered from the durable `cleanup_downloader` phase. This closes the prior `link`/`link_delete` gap where task status alone could return a cleanup job to file work.
- Download detail API and the static TypeScript WebUI expose the stable phase code, localized label, unit count and accessible progress element. Preparation progress remains explicitly unavailable instead of being fabricated.

## Automated evidence

- `SchemaMigrationTests` and `SchemaConstraintTests` cover v36→v37 backfill and invalid state/progress combinations.
- `MediaOrganizationStoreTests` cover progress validation/preservation, success transitions and expired `link` cleanup lease recovery.
- `MediaOrganizationProcessorTests` cover final cleanup/completed progress and the durable `directory_index` failure position.
- `DownloadManagementApiTests` cover nullable preparation fields and the organization progress JSON contract.
- `StaticWebUiTests` and Node WebUI tests cover the compiled phase UI marker, progress styling and existing accessibility/state contracts.

## Release gate

- `npm run web:test`: 13/13 passed.
- Complete .NET suite: 1383/1383 passed (Plugin Abstractions 13, Plugin SDK 16, Core 339, Plugin Tool 23, Data 195, App 797).
- Scoped `dotnet format --verify-no-changes` passed for every changed C# file.
- `win-x64` NativeAOT restore/publish completed native code generation with no trim/AOT warnings.
- The exact published executable passed isolated first-start and legacy-YAML-upgrade smoke modes at schema v37.
- The same executable passed the native AI metadata smoke fixture at schema v37.

Tests use temporary SQLite databases, fake download clients and temporary filesystem roots. No user qBittorrent task, Torrent URL, credential, passkey or media file was accessed.

Parity deviation: fine-grained durable organization progress is an AnimeGoNet feature and has no AnimeGo `develop` equivalent. The existing safe cleanup deviation remains unchanged: qBittorrent is called with `deleteFiles=false`; source files are handled only by root-bounded file operations.
