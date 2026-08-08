# Source duplicate notification verification

## Scope

- schema v38 adds `source_profiles.duplicate_notification_enabled` with a constrained `0/1` value and defaults existing/new profiles to enabled.
- SourceProfile CRUD, route preview, deployment YAML defaults and the static TypeScript WebUI expose the setting. An omitted update value preserves the current setting.
- RSS processing uses the request's immutable SourceProfile snapshot. Direct unified ingest stores the setting in `route_snapshot_json`, and the TMDB Episode stage reads that snapshot instead of the current profile.
- RSS completion-alias hits, unavailable winner claims and canonical TMDB Series/Season/Episode duplicate decisions emit application log event `4301` when enabled. The WebSocket log receives only stable source/scope/reason values.
- Disabling the setting suppresses only the event. Completion aliases, Episode claims, per-file duplicate dispositions and the download gate remain mandatory and global.

## Automated evidence

- `SchemaMigrationTests` covers v37→v38 backfill, persisted disablement and the SQLite check constraint.
- `AnimeGoDefaultsTests`, `DeploymentYamlConfigurationTests`, `IngestTaskStoreTests`, `MetadataResolutionStoreTests` and `SourceProfileApiTests` cover defaults, YAML parsing, immutable route snapshots, metadata propagation, create/update preservation and route preview.
- `DuplicateHitNotifierTests` covers the stable event identity/message and disabled no-op behavior.
- `MikanRssIngestProcessorTests` and `EpisodeMetadataResolutionProcessorTests` connect to the real Kestrel WebSocket endpoint and verify RSS/TMDB duplicate events without exposing the Torrent URL.
- `StaticWebUiTests` and the Node WebUI suite cover the compiled control/label and existing accessibility/state contracts.

## Release gate

- `npm run web:test`: 13/13 passed.
- Complete .NET suite: 1388/1388 passed (Plugin Abstractions 13, Plugin SDK 16, Core 339, Plugin Tool 23, Data 196, App 801).
- Solution build completed with zero warnings and zero errors.
- Scoped `dotnet format --verify-no-changes` passed for every changed C# file.
- `win-x64` NativeAOT restore/publish completed native code generation with no trim/AOT warnings.
- The exact published executable passed isolated first-start and legacy-YAML-upgrade smoke modes at schema v38.
- The same executable passed the native AI metadata smoke fixture at schema v38.

Tests use temporary SQLite databases, fake download clients/transports and temporary filesystem roots. No local qBittorrent process, private task, real Torrent URL, credential, passkey or media file was accessed.

Parity note: upstream AnimeGo logs duplicate cancellation. AnimeGoNet keeps that default behavior while adding a per-source notification switch; the switch cannot weaken the global deduplication invariant.
