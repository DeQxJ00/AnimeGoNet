# Bangumi fallback after authoritative TMDB no-match

## Scope

- The feature remains disabled by default.
- Only authoritative TMDB `SemanticNoMatch` with `tmdb_access_confirmed=true`,
  valid `bgmid`, and a configured deterministic positive Season can continue.
- Enabled Season AI runs first and may recover a real TMDB identity.
- Network, remote service, authentication, configuration, protocol, invalid input,
  ambiguity and cancellation never enter the fallback.
- `anime_series` stores `tmdb_series_id=0`, `bgmid`, canonical Bangumi name and
  `needs_tmdb_completion=1`; task files and resolution runs keep the TMDB Series
  field null.
- No source Episode is represented as a TMDB Episode. Files enter the confirmed
  Season's `Other` directory with `tmdb_fallback_pending_completion`.
- Organization writes root `tvshow.nfo` with exact `tmdbid=0` and `bangumiid`,
  creates a scoped fallback completion record, and creates no canonical TMDB
  completion record.

## Automated acceptance

- Authoritative empty TMDB search plus enabled fallback and title Season 2 creates
  a fallback-resolved run and pending-completion Series projection.
- TMDB network failure remains `metadata_failed`, `fallback_eligible=false`, and
  creates no `tmdbid=0` Series.
- A real temporary filesystem move places the file in `S02/Other`, writes the
  exact NFO IDs, persists a `torrent_file` fallback completion, and leaves
  canonical completion count at zero.

## Result

- `dotnet test AnimeGoNet.slnx --no-restore`: passed
  - Core: 199
  - Data: 75
  - App: 239
  - Total: 513
- `dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64
  --self-contained true /p:PublishAot=true --no-restore`: passed
