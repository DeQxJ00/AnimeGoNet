# Directory database write, scan and refresh

## Upstream parity

The reference Go `database.Scan()` recursively loads only AnimeGo's own
`anime.a_json`, `anime.s_json` and `*.e_json` sidecars. It does not infer
TMDB or Episode identity from video names or `tvshow.nfo`. AnimeGoNet keeps
that boundary: SQLite remains canonical, while the directory database is a
compatible, auditable media-side mirror.

## Implemented boundary

- media organization atomically writes the three upstream JSON shapes;
- series and season creation timestamps are preserved across later episodes;
- Episode sidecars record downloaded/renamed/scraped and preserve seeded state;
- sidecars and schema v27 index updates complete before canonical completion
  records and Episode claims are committed;
- invalid existing sidecars, paths outside the captured save root, symlinks,
  reparse points, oversized files and malformed JSON fail safely;
- startup performs one full scan; scheduled refresh uses the upstream default
  `0 0 6 * * *` and `StartRun=false`;
- full refresh and incremental organization upserts share one process gate;
- APIs and the static WebUI expose the latest run and an explicit refresh.

## Verification

- exact JSON shape and create/update/seed-state preservation;
- valid anime/season/Episode scan plus malformed-file continuation;
- transactional index replacement, missing-file removal and issue audit;
- media organization creates NFO plus all sidecars and three index rows;
- malformed pre-existing sidecar schedules retry and leaves completion count 0;
- schedule plugin, status API and explicit refresh API;
- strict TypeScript, all .NET tests and win-x64 NativeAOT publish/smoke.
