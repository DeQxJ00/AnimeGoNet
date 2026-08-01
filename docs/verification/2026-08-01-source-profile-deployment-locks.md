# SourceProfile deployment-lock verification — 2026-08-01

## Behavior

Deployment configuration is authoritative for persisted SourceProfiles, not
only for their first SQLite seed. The application detects canonical
`sources__<id>__category|dynamic_tag_template|mikan_identity_cookie` environment
keys and their `--sources:<id>:<field>` command-line equivalents. Legacy
`ANIMEGO_CATEGORY`, `ANIMEGO_TAG` and `ANIMEGO_MIKAN_COOKIE` target the default
`mikan` profile.

After seed creation on every startup, configured values are reapplied with one
conditional SQL update. A changed row increments its revision once; an
identical restart is idempotent. Explicit empty tag or Cookie values clear the
persisted optional field. API responses expose only the locked field, source
kind and controlling key names. They never expose the Cookie or command-line
value. An update that changes a locked field fails with
`source_profile_field_locked`; an update preserving locked values can still
change unrelated profile fields.

The static TypeScript UI disables category, dynamic-tag and Cookie controls as
applicable, displays controlling key names, and keeps the Cookie write-only.

## Automated evidence

Targeted Release tests passed 5/5: one SQLite persistence/idempotency test and
four configuration/API tests. The strict TypeScript check, deterministic build
and five Node protocol/security tests passed. The complete Release solution
suite passed 1319/1319:

```text
AnimeGo.Plugin.Abstractions.Tests   12
AnimeGo.Plugin.Sdk.Tests            16
AnimeGo.PluginTool.Tests            23
AnimeGoNet.Core.Tests              330
AnimeGoNet.Data.Tests              177
AnimeGoNet.App.Tests               761
```

The `win-x64` Release NativeAOT publish completed native-code generation with
no trim/AOT warnings. The exact executable passed both first-start and
legacy-YAML-upgrade modes of `eng/smoke-native.ps1`, including schema v36,
SQLite initialization, OpenAPI, static WebUI, WebSocket and process cleanup.

The exact repository secret scan found no local TMDB test key, qB API key or
portable qB endpoint. Tests used fake transports and disposable paths; they did
not connect to TestSpace or create a real Torrent task.
