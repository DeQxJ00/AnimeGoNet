# Legacy YAML upgrade verification — 2026-07-30

## Upstream baseline and .NET boundary

The `wetor/AnimeGo develop` baseline:

- recognizes 1.1.0, 1.2.0, 1.3.0, 1.4.0, 1.4.1, 1.5.0, 1.5.1,
  1.5.2, 1.6.0, 1.6.1, 1.6.2, 1.7.0 and 1.7.1;
- enables a same-directory timestamped backup by default;
- exposes `--backup` and `ANIMEGO_CONFIG_BACKUP`;
- rewrites through every intermediate Go model and exits for a manual restart;
- also writes Python plugin assets and, for one historical transition, performs
  Bolt/directory-database side effects.

AnimeGoNet keeps the version/backup semantics but does not execute Python,
JavaScript, old plugin assets or legacy database side effects during
configuration parsing. It maps only fields with a defined .NET owner:
paths, qBittorrent, Mikan file strategy/category/seeding, Access Key,
TMDB/Bangumi endpoints and proxy, TMDB key, request timeout, deterministic
season-failure switches and database refresh Cron.

The upstream dynamic `setting.tag` expression is not copied to
`SourceProfile.tags`, because the former is a template and the latter is
currently a static list. Mikan Cookie likewise remains in the exact backup
until its dedicated source-secret model exists.

## Persistence guarantees

- Parsing, version recognition and known scalar validation happen before any
  write.
- The original bytes are written with `CreateNew` beside the source as
  `animego-<version>-<UTC yyyyMMddHHmmss>[-NNN].yaml`; collisions increment and
  never overwrite evidence.
- Unix backup and replacement files are created with mode `0600`.
- The canonical file is fully flushed to a unique same-directory temp and then
  replaces the old path.
- A second start sees canonical 1.7.1 and creates no additional backup.
- `--backup=false` / `ANIMEGO_CONFIG_BACKUP=false` intentionally disables only
  the backup, matching upstream control semantics.
- Transmission or any explicit non-qBittorrent legacy client is not rewritten
  or backed up; the existing unsupported-downloader diagnostic continues to
  block all download workers.

## Tests

Targeted App tests passed 24/24. They cover all 13 recognized versions,
default backup byte identity/name, canonical field mapping, idempotency,
backup-disabled application startup, invalid-value no-write behavior,
Transmission no-rewrite behavior, dynamic-tag refusal, and the five-RID
workflow contract.

The complete Release suite passed:

```text
AnimeGo.Plugin.Abstractions.Tests   11
AnimeGoNet.Core.Tests              264
AnimeGoNet.Data.Tests              152
AnimeGoNet.App.Tests               505
total                              932 passed, 0 failed, 0 skipped
```

## NativeAOT

The latest source was published for `win-x64` with `.NET 10`,
`PublishAot=true` and completed `Generating native code`.

The exact native executable then passed two isolated executions of
`eng/smoke-native.ps1`:

1. `first-start`: generated safe canonical YAML and completed the existing API,
   SQLite schema 30, static WebUI, WebSocket, qB capability and secure-ingest
   checks.
2. `legacy-yaml-upgrade`: started from a safe 1.6.1 fixture, proved the backup
   SHA-256 matched the original bytes, proved the active file was canonical
   1.7.1, proved the dynamic tag template was absent, and completed the same
   runtime checks.

`.github/workflows/animegonet-native-aot.yml` now invokes both modes for
`win-x64`, `win-arm64`, `linux-x64`, `linux-arm64` and `osx-arm64`. Non-Windows
executions retain the existing SIGTERM/cleanup assertion.

No TestSpace file, qB profile, local credential, TMDB key, passkey or real
legacy configuration was used. All migration inputs were disposable fixtures
containing placeholder values.
