# AI cross-season package verification

## Scope

- One task can contain files from multiple positive TMDB TV Seasons of the same
  verified Series.
- The task-level resolution run uses a null Season summary; each pending file
  stores its own verified Season number.
- AI Season verification seeds each video with its verified Episode or safe
  `ai_episode_unmatched` reason.
- A uniquely associated subtitle inherits the video's Season and Episode/Other
  seed, then the Episode worker preserves its language suffix.
- Cross-season tasks bypass single-season manual Episode offsets and the
  post-season Episode AI pass.
- Any pending file without a reliable Season assignment rejects the cross-season
  candidate before the atomic completion transaction.

## Automated acceptance

- `MetadataResolutionStoreTests.AiCrossSeasonAssignmentPersistsAndCompletesEachFileAgainstItsOwnSeason`
  verifies two files with Episode 1 in Seasons 1 and 2, a null run-level Season,
  per-file Episode claim projection, and final completion.
- `AutomaticMetadataResolutionProcessorTests.SeasonAiResolvesCrossSeasonVideosAndAssociatedSubtitleEndToEnd`
  verifies one task-level AI request, TMDB validation in both Seasons, associated
  `.zh-Hans.ass` inheritance, and final `metadata_resolved` state.
- `AutomaticMetadataResolutionProcessorTests.SeasonAiRejectsCrossSeasonPackageWhenAFileCannotBeAssignedSafely`
  verifies `ai_cross_season_file_unassigned` and confirms no file receives partial
  Season state.

## Commands

```powershell
dotnet test tests/AnimeGoNet.Data.Tests/AnimeGoNet.Data.Tests.csproj --no-restore --filter FullyQualifiedName~MetadataResolutionStoreTests
dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj --no-restore --filter FullyQualifiedName~AutomaticMetadataResolutionProcessorTests
dotnet test AnimeGoNet.slnx --no-restore
dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 --self-contained true /p:PublishAot=true
```

## Result

- Data metadata store target: 12 passed.
- Automatic metadata processor target: 14 passed.
- Full solution: 502 passed (`Core 199`, `Data 74`, `App 229`), 0 failed,
  0 skipped.
- `win-x64` NativeAOT publish: passed with no warnings or errors.
