# External plugin runtime API and WebUI (2026-08-01)

## Scope

The existing status response now drives a static TypeScript external-plugin panel.
Valid packages show safe manifest metadata and stopped/starting/ready/backoff/
auto-disabled state. Invalid packages show only their directory basename, stable
validation code and safe message. DOM nodes use `textContent`; no package path,
entry point, plugin data path, stderr, environment or config is returned.

`POST /api/v1/plugins/{id}/reset` accepts only canonical stable IDs, returns 404
for undiscovered IDs and uses the common Access-Key mutation boundary. Reset
disposes an old session and clears backoff/auto-disable without starting a process
or modifying package files. The button is only rendered for a runtime with failure
evidence.

## Tests

- TypeScript 7 strict check and deterministic build passed.
- API tests cover explicit runtime arrays, stable missing/invalid ID errors and
  Access-Key rejection.
- Static Kestrel asset tests verify the panel, responsive style, reset endpoint and
  user-facing recovery label are present in compiled production assets.

Targeted API/static WebUI result: 93/93 passed, 0 skipped.

- An isolated local Kestrel process returned `pong`, HTTP 200 for `/` and
  `/app.js`, explicit empty package/error/runtime arrays, and the compiled
  `external-plugin-list` plus reset-endpoint markers. The exact process and
  temporary data root were removed after the check.
- Full Release result: 1168/1168 passed, 0 skipped (plugin abstractions 11,
  core 324, data 173, app 660).
- `win-x64` NativeAOT publish succeeded. First-start and legacy-YAML-upgrade
  smoke both passed with schema 36, including manifest/runtime projection,
  reset API, compiled WebUI markers and plugin-data isolation. The published
  executable had zero live processes after both runs.
