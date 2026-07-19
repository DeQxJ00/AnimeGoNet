# TMDB title and season parity verification — 2026-07-19

## Baseline and behavior

The implementation is a direct behavioral port of:

- `upstream/develop:pkg/utils/name.go`
- `upstream/develop:pkg/utils/name_test.go`
- `upstream/develop:internal/animego/anisource/themoviedb/themoviedb.go`
- `upstream/develop:internal/constant/anisource.go`

`TmdbTitleHeuristics` preserves all four ordered suffix-removal regular expressions. `TmdbSeriesResolver` intentionally retries the current title once per upstream step even when a regular expression does not match, then applies the next transformation. A single candidate wins; multiple candidates first prefer `original_name == original input`, otherwise the original UTF-8 byte-based longest-common-substring algorithm selects the first maximum at the upstream `0.75` threshold.

`TmdbSeasonSelector` excludes Season 0 and the exact upstream `Specials` label, selects the smallest absolute air-date difference and accepts exactly 90 days. For strict parity, a missing source or TMDB air date retains the upstream zero-difference behavior. A later safety policy may reject missing-date ambiguity, but that must be a documented layer above this compatibility selector rather than an untracked change to the baseline.

## Automated evidence

```text
dotnet test AnimeGoNet.slnx --no-restore
Core: 65 passed
Data: 17 passed
App: 51 passed

dotnet build AnimeGoNet.slnx -c Release --no-restore
0 warnings, 0 errors

dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj --configuration Release \
  --runtime win-x64 --self-contained true --no-restore \
  --output artifacts/tmdb-title-season-aot-win-x64 -p:PublishAot=true
Generating native code

eng/smoke-native.ps1 -Executable artifacts/tmdb-title-season-aot-win-x64/AnimeGoNet.App.exe
Native smoke passed (schema v5, secure ingest rejection, static WebUI)
```

Fixtures cover the upstream Chinese/Japanese/English suffix cases, non-matches, Unicode similarity, exact-original-name priority, similarity acceptance/rejection, repeated search sequence, client failure propagation, Specials exclusion, nearest season, 90/91-day boundary, missing-date parity and Specials-only failure. No network or credential is used.
