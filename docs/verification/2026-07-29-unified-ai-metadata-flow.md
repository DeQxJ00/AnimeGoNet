# Unified AI metadata flow verification

Date: 2026-07-29

## Scope

This module replaces the former season-AI and post-season EP-AI business split with one task-level metadata flow:

- canonical configuration key `ai_use_metadata_match`, disabled by default;
- one shared request contract, resolver, Prompt and TMDB validator for Series, Season and all video Episodes;
- at most one semantic AI attempt per download task across the season and Episode stages;
- deterministic Series/Season success may defer the task's first AI call to unresolved Episodes, while any earlier successful or failed AI attempt suppresses a later call;
- audit strategy `ai_metadata`; historical `ai_season` and `ai_episode` attempts remain readable as prior attempts;
- legacy deployment/API/private configuration fields enable the unified switch only when the canonical field is absent; an explicit canonical value wins;
- WebUI exposes and writes one switch while compatibility response fields mirror the canonical value.

The older verification notes for separate season and Episode switches remain historical evidence only; this document supersedes their product-flow description.

## Automated evidence

Commands completed successfully:

```text
npm run web:check
npm run web:build
node --check src/AnimeGoNet.App/wwwroot/app.js
dotnet build AnimeGoNet.slnx --no-restore
dotnet test AnimeGoNet.slnx --no-build --verbosity minimal
```

Results:

- Core: 209 passed;
- Data: 100 passed;
- App: 277 passed;
- total: 586 passed, 0 failed, 0 skipped;
- build: 0 warnings, 0 errors.

Focused coverage includes:

- one task prompt contains only video files and is validated as a complete Series/Season/Episode result;
- no-video input is not applicable and does not call the matcher;
- a successful season-stage AI result seeds all Episodes and is not called again;
- a failed season-stage AI attempt followed by P2/P1 deterministic fallback remains recorded and suppresses Episode-stage AI;
- deterministic season plus unresolved Episode can make the task's first and only AI call with Series/Season locked;
- different Series/Season, missing Episode, malformed response and matcher failures cannot rewrite validated metadata;
- cross-season tasks keep per-file seasons and one task-level call;
- canonical deployment key, both legacy aliases, canonical-over-legacy precedence, private-file migration and legacy API update compatibility;
- WebUI contains one `configuration-ai-metadata` control and no separate season/episode controls.

## NativeAOT evidence

The following completed on the local `win-x64` host:

```text
dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -o artifacts/native-smoke-unified-ai
./eng/smoke-native.ps1 -Executable artifacts/native-smoke-unified-ai/AnimeGoNet.App.exe
```

The native process passed `/ping`, schema v22/SQLite initialization, `native_aot=true`, qBittorrent capability, secure ingest rejection and static WebUI checks.
