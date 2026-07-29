# AnimeGoNetData hot configuration verification — 2026-07-29

## Scope

- Expose all seven `data_update` fields through the redacted configuration API
  and static WebUI editor.
- Persist them in the private application override with revision concurrency and
  environment-variable field locks.
- Apply the runtime policy and six-field Cron schedule without restarting.
- Keep manual data update operations available when scheduling is disabled.

## Runtime contract

- `DataUpdateRuntimeState` is the single synchronized snapshot read by the
  service, scheduled plugin, status API and scheduler.
- `DataUpdateScheduleManager` serializes changes. It removes and replaces the
  stable `animegonet-data-update` registration; an invalid replacement restores
  the previous registration and does not publish the candidate runtime state.
- `background_workers=false` publishes the runtime policy for manual APIs but
  intentionally registers no schedule.
- A write containing only data update changes advances
  `applied_configuration_revision`. A mixed write hot-applies data update while
  retaining `restart_required=true` for the other fields.
- Environment-backed fields are projected in `locked_fields`, disabled in the
  editor and rejected by the API if changed.

## Automated evidence

- `DataUpdateScheduleManagerTests`: enable, replace, disable, worker-disabled and
  invalid-Cron rollback.
- `DataUpdateSchedulePluginTests`: action mapping is re-read after a runtime
  policy update.
- `DataUpdateServiceTests`: the next manual operation uses the hot-reloaded
  manifest URL and timeout snapshot.
- `ConfigurationApiTests`: redacted projection, hot-applied revision, mixed
  restart semantics, environment lock, reset-to-deployment and production
  WebUI assets.
- `npm run web:check` and the generated production JavaScript build passed.
- Full solution tests passed: Plugin Abstractions 11, Core 263, Data 146 and App
  428, for 848/848 total.
- `win-x64` Release NativeAOT publish completed `Generating native code` without
  trim/AOT warnings. `eng/smoke-native.ps1` then passed `/ping`, schema v29,
  SQLite initialization, `native_aot=true`, secure ingest rejection,
  qBittorrent capability and static WebUI checks.
- A separate JIT preview used disposable data/download/save directories and
  `background_workers_enabled=false`. Browser inspection confirmed the runtime
  card reports `即时热重排`; the dialog exposes all seven visible, enabled
  controls with defaults `false`, `0 0 4 * * ?`, empty manifest, `true`,
  `true`, `2`, `300`; its save action remained enabled and the console had no
  errors. No form was submitted. The tab, exact preview process and temporary
  directory were removed afterward.

No live qBittorrent task or real Torrent is created by this module.
