# Bangumi fixed-season fallback verification

Date: 2026-07-29

## Corrected behavior

Bangumi final fallback is separate from the TMDB season-failure chain:

- disabled by default;
- requires an effective positive `bgmid`;
- requires an authoritative TMDB request that ended in `SemanticNoMatch` with access confirmed;
- does not run for network, remote-service, authentication, configuration, protocol or invalid-input failures;
- does not run when a valid TMDB Series was found and only Season matching failed;
- does not read the task title or Bangumi names for a season;
- does not depend on `TMDBFailUseTitleSeason` or `TMDBFailUseFirstSeason`;
- always assigns the local pending-completion scope `S01`;
- keeps canonical task/run TMDB IDs null, stores the exceptional library identity as `tmdb_series_id=0`, creates no fake TMDB Episode progress, and writes root `tvshow.nfo` with `tmdbid=0` plus the real `bangumiid`.

## Automated coverage

`AutomaticMetadataResolutionProcessorTests` verifies:

- an authoritative empty Series result with a title explicitly containing “Season 2” still produces fixed local `S01` while both P2/P1 are disabled;
- the default-disabled switch stops the task and creates no `tmdb_series_id=0` row;
- a TMDB network failure never creates fallback state;
- a validated Series with only air-date/Season mismatch records Series success and never creates complete-failure fallback state.

`MediaOrganizationProcessorTests.BangumiFallbackMovesToOtherAndWritesTmdbZeroNfoWithoutCanonicalCompletion` verifies the real disposable filesystem output, `Other` placement, XML-readable `tmdbid=0`/`bangumiid`, fallback completion record and absence of canonical TMDB completion.

Full validation completed with 209 Core, 100 Data and 279 App tests: 588 passed, 0 failed, 0 skipped. The `win-x64` Release NativeAOT publish and `eng/smoke-native.ps1` passed schema v22/SQLite initialization, static WebUI, secure ingest rejection and native capability checks.
