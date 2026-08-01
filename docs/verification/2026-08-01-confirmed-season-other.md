# Confirmed-season `Other` organization verification (2026-08-01)

## Scope

This module closes the filesystem boundary for files whose TMDB Series and
ordinary Season are confirmed but whose Episode cannot be represented reliably.
It uses disposable application roots, SQLite, the real rename plugin, safe mover,
NFO/directory sidecar writers, and a fake downloader. It does not access a live
qBittorrent, TMDB, Bangumi, AI provider, Torrent URL, passkey, cookie, or API key.

## Proven behavior

- Fractional source Episode `48.5` and special source Episode `SP01` do not become
  ordinary integer TMDB Episodes.
- A missing ordinary TMDB Episode, a unified-AI unmatched file, and an orphan
  subtitle retain their stable `other_reason`.
- Every confirmed-season `Other` file keeps its sanitized original basename and
  moves to `<TmdbSeriesName>/Sxx/Other/`.
- The organizer writes the Series/Season sidecars needed for a confirmed Season,
  but writes no `Eyyy.e_json` file and creates no Episode completion record,
  completion alias, or completed Episode claim.
- Existing metadata fixtures prove the unified AI gate is disabled by default,
  requires no call when disabled, calls at most once per task, and does not run a
  second Episode-stage request after a Season-stage success or failure.

## Targeted tests

- `MediaOrganizationProcessorTests.ConfirmedSeasonOtherMovesOriginalNameWithoutInventingEpisodeProgress`
  covers five filesystem cases through the real organization processor.
- `EpisodeMetadataResolutionProcessorTests.NonIntegerOrUnknownFileGoesToOtherWithoutTmdbRequest`
  covers fractional, special, and unparsed candidates.
- `EpisodeMetadataResolutionProcessorTests.OrphanSubtitleGoesToConfirmedSeasonOtherWithoutTmdbRequest`
  covers an unassociated subtitle.
- `EpisodeMetadataResolutionProcessorTests.MissingAutomaticTmdbEpisodeGoesToOtherInConfirmedSeason`
  covers an absent ordinary TMDB Episode.
- `AutomaticMetadataResolutionProcessorTests.SeasonAiKnownSeasonOtherDoesNotInvokeEpisodeAiAgain`
  covers the shared one-attempt AI gate.
- `AiMetadataResultValidatorTests.RejectsSeasonZeroBeforeTmdbAccess` proves the AI
  result cannot reintroduce Season 0.

## Release gate

- Targeted boundary tests passed 12/12: App 11 and Core 1.
- Release solution build passed with zero warnings and zero errors.
- The complete suite passed 1090/1090: Plugin Abstractions 11, Core 324,
  Data 171, App 584.
- This module changes only deterministic tests and traceability documentation. Its
  production source is byte-for-byte the same as parent commit `eb64f24`, whose
  `win-x64` NativeAOT publish generated native code and passed an isolated
  first-start status smoke with schema v36.
