# Source RSS scheduling verification

## Scope

- schema v36 stores a write-only Mikan RSS URL, enabled flag, six-field Cron and durable last-run audit on each SourceProfile.
- `mikan-rss-ingest-schedule` is a compile-time built-in C# plugin. Schedule registrations contain only source ID and revision; the RSS URL/passkey is fetched from SQLite at execution and never appears in API responses, scheduler snapshots or plugin failures.
- Startup registers every enabled source schedule and recovers interrupted `running` audits. Source CRUD hot-adds, replaces or removes the task; background-worker-disabled mode preserves configuration without registering a fake task.
- Execution rejects stale revisions before network access, atomically prevents overlapping runs, uses the existing protected RSS fetch and Mikan rule/dedup/ingest pipeline, and persists success batch or stable failure code.
- Static WebUI exposes write-only URL/explicit clear, Cron, enable switch, registered/next-run state, last status/failure and batch without repopulating the URL.

## Automated evidence

- `SourceRssSchedulePolicyTests`: URL/Cron normalization and unsafe/unsupported configuration rejection.
- `SourceProfileStoreAdminTests` and `SchemaMigrationTests`: v35→v36 preservation/defaults/index/constraints, versioned secret storage, overlap gate, failure audit, revision reset and interrupted-run recovery.
- `SourceProfileApiTests`: write-only create/list/get/update/clear, omission preservation, invalid adapter/Cron rejection and WebUI markers.
- `SourceRssScheduleTests`: compile-time plugin success/failure, no secret echo, successful batch FK, overlap bypass, stale revision zero network, manager startup/replace/remove and real HostedService/API hot application.

All transports and databases in these tests are isolated fakes/temporary files. No user RSS, passkey, Torrent or qBittorrent task was accessed.

## Release gate

- `npm run web:check` and `npm run web:build` passed.
- Release solution build passed with zero warnings and zero errors.
- The complete suite passed 1071/1071: Plugin Abstractions 11, Core 324, Data 171, App 565.
- `win-x64` NativeAOT publish completed native code generation without trim/AOT warnings.
- The exact published executable passed isolated first-start and legacy-YAML-upgrade smoke modes at schema v36.
