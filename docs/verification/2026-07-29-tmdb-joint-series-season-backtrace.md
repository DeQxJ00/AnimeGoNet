# TMDB joint Series/Season and P3 verification

Date: 2026-07-29

## Implemented behavior

- Normal Bangumi matching tries the original/Japanese `name` before `name_cn`.
- Each name runs the original search plus four ordered suffix-cleaning steps; identical generated search strings are requested only once.
- Every eligible TMDB result is ranked by exact localized/original name, similarity, and stable response order.
- A candidate succeeds only after official Series details exist and an ordinary Season matches the Bangumi air date.
- A rejected candidate does not stop the current response, a rejected response does not stop later cleaned titles, and exhausted Japanese titles do not stop Chinese titles.
- Missing source air date never invents the first TMDB Season.
- P3 is an AnimeGoNet extension. It requires `bgmid`, but does not require the current title search to have found a Series.
- Each predecessor is a new complete match node: its Japanese name, Chinese name, and air date may resolve a different `tmdbid + Season`.
- P2 and P1 remain local Season Number fallbacks and are applicable only when an official TMDB Series has already been validated.

## Automated verification

```text
dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj --no-restore \
  --filter "FullyQualifiedName~TmdbSeriesSeasonResolverTests|FullyQualifiedName~BangumiSeasonBacktraceResolverTests|FullyQualifiedName~AutomaticMetadataResolutionProcessorTests"
Passed: 30, Failed: 0

dotnet test tests/AnimeGoNet.Core.Tests/AnimeGoNet.Core.Tests.csproj --no-restore \
  --filter "FullyQualifiedName~TmdbSeriesResolverTests|FullyQualifiedName~TmdbSeasonSelectorTests"
Passed: 14, Failed: 0

dotnet test AnimeGoNet.slnx --no-restore --verbosity minimal
Core: 209 passed
Data: 99 passed
App: 271 passed
Total: 579 passed, 0 failed
```

The focused fixtures include a first candidate whose Season fails followed by a second candidate from the same TMDB response that succeeds, a cleaned title that resolves another Series, Japanese-to-Chinese fallback, missing dates, multi-level predecessor traversal, cycles, and P3 recovery of a different Series after both current titles return no Series.

## NativeAOT verification

`win-x64` restore and Release NativeAOT publish completed with `PublishAot=true`. `eng/smoke-native.ps1` passed `/ping`, status/schema 22, NativeAOT capability, SQLite initialization, secure ingest rejection, qBittorrent capability, and static WebUI checks.
