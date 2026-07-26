# Season AI processor verification

## Scope

- Season AI remains independently configurable and disabled by default.
- Confirmed execution order is `Skip=4` → `Backtrace=3` → independent Season AI →
  `TitleSeason=2` → `FirstSeason=1`.
- A claim carries the complete pending Torrent file projection and optional
  Bangumi/AniDB/IMDb work IDs.
- The AI request includes candidate videos only, while `torrent_file_count` includes
  every actual Torrent file.
- Publication-date evidence remains disabled until its authoritative source fields
  are persisted.
- AI output is structurally checked and every Series/Season/Episode is verified by
  TMDB before the season transaction commits.
- Same-season verified Episode/Other seeds are persisted atomically with the
  canonical Series/Season and resolution lease.
- The Episode processor consumes those seeds and does not run post-season EP-AI for
  a task that already used Season AI.

## Failure semantics

- Skip terminates before AI.
- Backtrace success terminates before AI.
- AI configuration, authentication, transport, protocol and no-match results are
  audited under `ai_season`, then the lower deterministic Title/First strategies
  continue.
- An AI result spanning multiple ordinary seasons is currently rejected with
  `ai_multiple_seasons_unsupported`; cross-season task state is still tracked as a
  separate remaining item rather than being silently flattened.
- Model-provided free-text reasons are not written directly into state; a verified
  known-season non-Episode uses the safe code `ai_episode_unmatched`.

## Acceptance

- Fake AI wins before Title/First, persists Episode 7 and completes through the
  Episode processor.
- Fake AI receives `bgmid`/`anidbid`/`imdbid` and no publication-date evidence.
- Fake configuration failure is audited and TitleSeason succeeds.
- Skip suppresses all AI calls.
- Known-season Other completes without a second AI call.
- Data tests cover atomic Episode seed projection and the `SeasonResolvedByAi`
  marker.
- Full solution tests and `win-x64` NativeAOT publish.
