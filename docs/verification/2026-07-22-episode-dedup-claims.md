# TMDB Episode claim and precise dedup verification

Date: 2026-07-22

## Scope

- Acquire the existing SQLite `episode_claims` unique key in the same transaction that persists validated TMDB Episode mappings.
- Share one claim across video/subtitle files in the same task and Episode.
- Mark only a matching file as `duplicate` when a canonical completion already exists or another task owns the active claim.
- Atomically transition an active claim to `completed` when `CompletionRecordStore` writes the first canonical completion.
- Allow an organizer failure path to release only the owned claim by Episode identity and `task_file_id`.
- Expose duplicate file counts through the metadata task API and static TypeScript WebUI.

## Evidence

- `EpisodeMetadataResolutionProcessorTests.VideoAndSubtitleWithSameCandidateShareVerifiedTmdbEpisode`
- `EpisodeMetadataResolutionProcessorTests.CompletedEpisodeIsSkippedWithoutSuppressingAnotherEpisode`
- `EpisodeMetadataResolutionProcessorTests.ActiveClaimFromAnotherTaskSkipsOnlyMatchingEpisode`
- `EpisodeMetadataResolutionProcessorTests.CompletionFinalizesOwnedEpisodeClaim`
- `EpisodeMetadataResolutionProcessorTests.FailedOrganizerCanReleaseClaimForAnotherTask`
- `CompletionRecordStoreTests.ConcurrentSameEpisodeWritesCreateOneCompletion`
- `CompletionRecordStoreTests.AnotherEpisodeIsNotSuppressed`
- `MinimalApiTests.MetadataTaskListShowsPipelineStateWithoutSecretTorrentUrl`

The complete solution test suite, win-x64 NativeAOT publish, and native process smoke are required again before this increment is committed.

## Boundary

The current metadata workers run after qBittorrent reports the task downloaded. This increment closes the database concurrency window before organizing, but does not yet prevent duplicate bytes from downloading. Pre-download TMDB resolution, qBittorrent file priority/unwanted selection, claim crash reconciliation, aliases, and completion-record deletion remain separate TODO items.
