# AnimeGoNetData API and WebUI verification — 2026-07-29

Scope: authenticated manual data-update control plane and the static
TypeScript/HTML/CSS status panel.

## Contract

- `GET /api/v1/data-update` exposes only safe policy, active/previous,
  installed/downloaded versions and operation audit data.
- Manual check, download, download+import, delayed local import and rollback
  reuse the same application services as scheduled work.
- `data_update.enabled=false` disables scheduling only; manual operations remain
  available.
- Manifest and asset URLs, package directories, credentials and response bodies
  from upstream services are never returned by the API.
- Missing manifest configuration is `400 data_manifest_url_missing`; concurrent
  work and unavailable rollback are conflicts; an unknown downloaded version is
  not found.
- The UI disables online actions when no manifest is configured and disables
  rollback when there is no previous version. It renders all remote and stored
  values through `textContent`.

## Automated evidence

`DataUpdateApiTests` cover the empty status projection, a manual check while
scheduling is disabled, persisted transfer status, missing-manifest handling
and unavailable rollback. `StaticWebUiTests` assert the generated assets contain
the API routes, controls, delayed-import action and responsive styling. The
targeted API/static-asset run passed 62/62 tests.

```text
dotnet test AnimeGoNet.slnx --no-restore
Plugin abstractions: 11 passed
Core:                263 passed
Data:                146 passed
App:                 406 passed
Total:               826 passed, 0 failed, 0 skipped

npm run web:check
npm run web:build
node --check src/AnimeGoNet.App/wwwroot/app.js
All passed.
```

The `win-x64` Release publish completed `Generating native code` with no
warnings. `eng/smoke-native.ps1 -ExpectedSchemaVersion 29` started the published
executable in disposable directories and verified `/ping`, schema v29,
`native_aot=true`, SQLite initialization, the static WebUI, qB capability and
secure ingest rejection.

## Browser evidence

An isolated JIT instance using disposable data/download/save directories was
opened at `http://127.0.0.1:6193/`. It reported schema v29 and rendered:

- `定时更新关闭（手动可用） · manifest 未配置 · 保留 2 版`;
- no active or previous version and empty installed/downloaded lists;
- refresh enabled;
- check, download, download+import and rollback disabled.

The browser console contained no entries. No external data endpoint,
qBittorrent task, Torrent, TMDB key or private test data was used.
