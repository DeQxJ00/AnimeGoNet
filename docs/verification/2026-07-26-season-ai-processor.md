# Season AI processor verification

## Scope

- Season AI remains independently configurable and disabled by default.
- Confirmed execution order is `Skip=4` → `Backtrace=3` → independent Season AI →
  `TitleSeason=2` → `FirstSeason=1`.
- A claim carries the complete pending Torrent file projection and optional
  Bangumi/AniDB/IMDb work IDs.
- The AI request includes candidate videos only, while `torrent_file_count` includes
  every actual Torrent file.
- Authoritative Mikan publication evidence and the Bangumi Episode candidate are
  now supplied by the separately verified `ai_pubdate` gate.
- AI output is structurally checked and every Series/Season/Episode is verified by
  TMDB before the season transaction commits.
- Same-season and cross-season verified Episode/Other seeds are persisted
  atomically with the canonical Series/Seasons and resolution lease.
- The Episode processor consumes those seeds and does not run post-season EP-AI for
  a task that already used Season AI.

## Failure semantics

- Skip terminates before AI.
- Backtrace success terminates before AI.
- AI configuration, authentication, transport, protocol and no-match results are
  audited under `ai_season`, then the lower deterministic Title/First strategies
  continue.
- A result spanning multiple ordinary seasons keeps the task-level Season summary
  null and persists the verified Season on each task file. Associated subtitles
  inherit the verified video's Season/Episode seed.
- If any pending file cannot be assigned to one verified ordinary Season, the
  result is rejected with `ai_cross_season_file_unassigned`; no partial Season
  state is committed.
- Model-provided free-text reasons are not written directly into state; a verified
  known-season non-Episode uses the safe code `ai_episode_unmatched`.

## Acceptance

- Fake AI wins before Title/First, persists Episode 7 and completes through the
  Episode processor.
- Fake AI receives `bgmid`/`anidbid`/`imdbid`; ordinary API ingest without trusted
  RSS evidence keeps the publication gate false.
- Fake configuration failure is audited and TitleSeason succeeds.
- Skip suppresses all AI calls.
- Known-season Other completes without a second AI call.
- Data tests cover atomic Episode seed projection and the `SeasonResolvedByAi`
  marker.
- Cross-season data/application tests cover per-file Season state, two Seasons
  sharing Episode 1, associated subtitle suffix preservation, one AI request, and
  safe rejection of an unassigned subtitle.
- Full solution tests and `win-x64` NativeAOT publish.
