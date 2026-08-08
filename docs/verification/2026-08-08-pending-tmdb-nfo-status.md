# Pending TMDB NFO rewrite status verification

## Scope

- A pending-TMDB recovery already enqueues durable `tvshow.nfo` rewrite jobs after a real TMDB Series/Season/Episode is verified. The unified metadata task detail now exposes those jobs as `nfo_rewrites`.
- A job is associated with an original task only when the task's Bangumi ID, verified TMDB Series ID and captured save root all match. Multiple task files or download rows cannot duplicate the same returned job.
- The response exposes an opaque job ID, Bangumi/TMDB IDs, `pending/writing/failed/completed`, attempt count, stable failure code, next retry, last update and completion time.
- The API and WebUI do not expose the job's save root, series directory name, NFO path, media path or lease token. There is no manual mutation endpoint; the existing durable worker remains authoritative.

## Automated evidence

- `PendingTmdbNfoRewriteStoreTests` covers task association and the persisted pending→writing→failed/retry→writing→completed lifecycle.
- `MinimalApiTests.PendingTmdbRecoveryVerifiesTmdbAndCommitsCanonicalCompletion` verifies that recovery creates a pending job, the original metadata task detail projects it, and no captured/private path is returned.
- `StaticWebUiTests` covers the compiled NFO status marker and styling. The Node WebUI suite covers deterministic TypeScript compilation plus existing DOM/accessibility contracts.

## Release gate

- `npm run web:test`: 13/13 passed.
- Complete .NET suite: 1390/1390 passed (Plugin Abstractions 13, Plugin SDK 16, Core 339, Plugin Tool 23, Data 196, App 803).
- Solution build completed with zero warnings and zero errors.
- Scoped `dotnet format --verify-no-changes` passed for every changed C# file.
- `win-x64` NativeAOT restore/publish completed native code generation with no trim/AOT warnings.
- The exact published executable passed isolated first-start and legacy-YAML-upgrade smoke modes at schema v38.
- The same executable passed the native AI metadata smoke fixture at schema v38.

Tests use temporary SQLite databases, fake download clients/transports and temporary filesystem roots. No local qBittorrent process, private task, real Torrent URL, credential, passkey or media file was accessed.
