# Metadata task source/TMDB detail

Date: 2026-07-29

## Scope

- Added `GET /api/v1/metadata/tasks/{taskId}`.
- The response pairs each safe Torrent-relative source file name and source EP
  with its final validated TMDB Series, Season, and Episode projection.
- The response exposes the latest unified `ai_metadata` attempt state, stage,
  duration, safe error code/reason, and an explicit confidence basis.
- A matched AI result is labelled `tmdb_verified` only because the existing
  processor records `matched` after `AiMetadataResultValidator` validates the
  Series, Season, every file mapping, and every Episode against TMDB.
- Model-provided numeric confidence remains forbidden by the AI response
  contract. Unattempted, failed, unmatched, or non-applicable AI results use
  `not_established` rather than inventing a score.
- The static WebUI lazily expands the source-to-canonical mapping and keeps the
  existing strategy-attempt timeline and explicit retry action.

The detail API does not return Torrent URLs, passkeys, qBittorrent credentials,
absolute download paths, absolute media paths, internal claim keys, or file
operation targets.

## Verification

```powershell
dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj --no-restore `
  --filter "FullyQualifiedName~MetadataTaskDetailApiTests|FullyQualifiedName~StaticWebUiTests"
# Passed: 43, Failed: 0, Skipped: 0
```

The API tests cover a resolved source-EP-to-TMDB mapping, canonical names,
verified AI trust basis, an unattempted task with no invented confidence, a
missing task, and passkey/Torrent-name non-disclosure. Static asset tests cover
the new expansion control and trust label.
