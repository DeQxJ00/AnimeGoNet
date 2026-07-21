# Subtitle episode association verification

Date: 2026-07-22

## Rules

- Recognized subtitle extensions: ASS, SSA, SRT, VTT, SUP, IDX and SUB; recognized video extensions are handled separately.
- Same-directory same-stem matching is authoritative and permits dot-delimited language/default/forced/SDH/track suffixes.
- If stems differ, a subtitle binds only when exactly one video has the same positive source Episode candidate.
- Multiple possible videos never use filename order as a guess. The subtitle enters confirmed-season Other with a stable ambiguity reason.
- Bound subtitles inherit the video's TMDB Series/Season/Episode and duplicate decision without a second TMDB request, episode claim or completion record.
- Rename suffixes are persisted. Move output uses `Eyyy.<suffix>.<subtitle-extension>`; unmatched subtitles retain their sanitized basename below Other.
- IDX and SUB entries bind independently to the same video and preserve `.idx`/`.sub`.

## Evidence

- `SubtitleAssociationResolverTests`: same stem, suffix preservation, unique-EP fallback, ambiguity, orphan, IDX/SUB.
- `EpisodeMetadataResolutionProcessorTests.VideoAndSubtitleWithSameCandidateShareVerifiedTmdbEpisode`: one TMDB request, one claim, persisted association/suffix.
- `EpisodeMetadataResolutionProcessorTests.OrphanSubtitleGoesToConfirmedSeasonOtherWithoutTmdbRequest`.
- `MediaOrganizationProcessorTests.AssociatedSubtitleMovesWithSuffixWithoutCreatingSecondCompletion`: real disposable file move and one completion record.
- Full solution: Core 99, Data 47, App 107; total 253 passed.
- Release build completed with 0 warnings/0 errors; win-x64 NativeAOT generation and schema v11 binary smoke passed.

SQLite schema v11 adds the self-referencing associated task-file ID and rename suffix. Tests remain fake-qB/disposable-filesystem only; no portable qB task or TestSpace content is touched.
