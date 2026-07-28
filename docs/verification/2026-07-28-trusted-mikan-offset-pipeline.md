# Trusted Mikan offset pipeline verification

## Scope

- The cache remains disabled by default; disabled or non-Mikan tasks perform no
  trusted-offset lookup or evidence write.
- Manual work rules remain higher priority because a complete manual Series/Season
  override is claimed by the manual worker before the automatic worker.
- An enabled Mikan task with positive `mikanid/groupid` reads one Trusted signature
  before Bangumi, TMDB and AI resolution.
- Every pending video must have a positive local Episode candidate and positive
  `candidate + offset`, or be deterministically classified as Special/Fractional.
  Unknown video files reject the entire shortcut and use the normal pipeline.
- A hit requires the previously verified local Series/Season canonical projection.
- Cache-derived Episodes do not call AI or TMDB Episode and do not invent a
  `tmdb_episode_id`; they still participate in the normal global
  `(series, season, episode)` claim/completion transaction.
- Normal TMDB-verified results learn only a single consistent Series/Season/offset
  signature. Cross-season, inconsistent, candidate-less and cache-derived results
  do not learn.

## Automated acceptance

- `TrustedMikanOffsetBypassesAiAndTmdbEpisodeRequests` verifies Trusted offset
  `+13` maps source EP4 to TMDB EP17, completes the task, records
  `trusted_mikan_offset`, and makes zero AI, TMDB search and TMDB Episode calls.
- `VerifiedEpisodeCompletesThirdTrustedOffsetObservation` starts with two distinct
  observations, follows the normal AI/TMDB validation path for source EP4→EP17,
  and promotes the signature to Trusted with `3/3` evidence.
- Existing store tests continue to cover duplicate source EP suppression, positive,
  zero and negative offsets, conflict revocation, ambiguous signatures and the
  disabled switch.

## Commands

```powershell
dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj --no-restore --filter FullyQualifiedName~AutomaticMetadataResolutionProcessorTests
dotnet test AnimeGoNet.slnx --no-restore
dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 --self-contained true /p:PublishAot=true --no-restore
```

## Result

- Automatic metadata processor target: 16 passed.
- Full solution: 504 passed (`Core 199`, `Data 74`, `App 231`), 0 failed,
  0 skipped.
- `win-x64` NativeAOT publish: passed with no warnings or errors.
