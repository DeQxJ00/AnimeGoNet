# Mikan legacy filter audit — 2026-07-22

## Scope

Schema v16 makes the legacy MikanTool stage explicit and auditable before the newer RSS blacklist/whitelist/priority stage.

- Every batch snapshots `legacy_filter_revision` and `legacy_filter_enabled`.
- Every original candidate stores filter state, stable reason, matched `Filiter` scope/key, and positive page `mikanid/groupid` when available.
- `RejectedByLegacyFilter` and `FilterEvaluationFailed` are first-class decision kinds; neither candidate enters priority competition or can acquire a winner lease.
- `Accepted`, `SkippedByConfiguration`, and pre-integration `NotEvaluated` candidates remain eligible for the existing ordered rules.
- The batch fingerprint includes the filter snapshot and every audit field, so different filter revisions or outcomes cannot alias the same idempotency key.

## Migration

The migration adds batch snapshot columns and rebuilds `mikan_rss_batch_entries` plus its child decision-group table to expand strict checks without weakening them. Existing rows are retained as `NotEvaluated/LegacyFilterNotRecorded`; winner/effect/lease/ingest foreign-key invariants remain in force.

## Verification

```powershell
dotnet test AnimeGoNet.slnx -c Release --no-restore
dotnet publish src\AnimeGoNet.App\AnimeGoNet.App.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true --no-restore -o artifacts\mikan-filter-audit-win-x64
```

Observed totals: Core 168, Data 63, App 143, total 374/374. The win-x64 NativeAOT publish completed without trimming or AOT warnings. A production-migration replay created schema 15, inserted a batch/winner/decision-group, upgraded it to schema 16, retained every row, returned no `foreign_key_check` rows, and passed `integrity_check`.

Safe Episode-page fetching, per-batch URL caching, engine execution, and API contract tests remain the next independent module.
