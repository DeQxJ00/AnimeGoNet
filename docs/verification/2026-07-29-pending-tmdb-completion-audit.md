# Pending TMDB completion audit

Date: 2026-07-29

This audit closes the stale implementation markers for the `tmdbid=0` fallback
and pending-TMDB workflow. The evidence is the current executable code and
focused automated tests, not the earlier design status.

## Verified behavior

- `MetadataResolutionStore` creates, releases, and completes fallback episode
  claims transactionally. A completed scope stops a later task before download
  resume; files from one task share one claim; another episode is unaffected.
- `PendingTmdbStore` lists only `tmdb_series_id=0` entries and returns fallback
  task, completion/claim, dedup-boundary, and recovery-candidate projections.
  Its API contracts contain no TMDB episode grid, completion ratio, poster URL,
  internal scope key, or media path.
- `PendingTmdbRecoveryStore` commits the canonical Series, Season, Episode,
  completion, alias, and task-file projection in one SQLite transaction.
  Converging fallback records retain an explicit
  `DuplicateAfterResolution` result and do not trigger downloads or file
  deletion.
- The recovery API verifies the requested TMDB TV Series, Season, and every
  Episode before invoking the transaction store.
- Partial recovery remains in the pending list. The final mapped fallback
  removes the exceptional `tmdbid=0` identity and makes the canonical work
  visible through the standard library.
- Recovery queues a persistent NFO rewrite job. Lease expiry and retry state
  survive restart, and the processor atomically replaces `tvshow.nfo` inside
  the existing fallback series directory.

## Focused verification

```powershell
dotnet test tests/AnimeGoNet.Data.Tests/AnimeGoNet.Data.Tests.csproj --no-restore `
  --filter "FullyQualifiedName~PendingTmdb|FullyQualifiedName~Fallback"
# Passed: 22, Failed: 0, Skipped: 0

dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj --no-restore `
  --filter "FullyQualifiedName~PendingTmdb"
# Passed: 6, Failed: 0, Skipped: 0
```

The focused data tests include rollback on missing records or conflicting TMDB
episode identities, active canonical-claim protection, existing canonical
completion precedence, duplicate convergence, and persistent NFO rewrite
leases. The application tests include API projection isolation, online TMDB
verification, no-mutation rejection, and NFO rewrite retry behavior.
