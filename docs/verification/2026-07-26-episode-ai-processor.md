# Post-season Episode AI processor verification

## Scope

- `UseEpisodeMatch` remains independent and disabled by default.
- Manual Mikan TMDB/season/Episode offset rules remain highest priority and suppress
  Episode AI.
- One task-level AI request is made only when a confirmed-season task still has at
  least one unmatched video.
- The request contains the task title, all candidate video relative paths/sizes,
  actual Torrent file count, and optional `bgmid`/`anidbid`/`imdbid`.
- Publication-date evidence remains disabled until the ingest task schema persists
  the authoritative Mikan publication timestamp and Bangumi candidate.
- AI output cannot change the confirmed TMDB Series, confirmed Season or any
  deterministically resolved Episode.
- Every AI Series/Season/Episode candidate is re-read through `ITmdbClient`.
- AI-matched subtitles follow the verified video and retain language suffixes.

## Failure semantics

- Network and remote-service failures fail the Episode resolution lease, retain
  pending files, and are retryable.
- Configuration, authentication, protocol and semantic no-match failures are
  audited as `ai_episode` attempts, annotate unmatched video `other_reason`, and
  continue to the confirmed Season `Other` path.
- A model attempt that changes a deterministic Episode is rejected with
  `ai_confirmed_episode_changed`.

## Acceptance

- Fake matcher resolves an unparsed video and its multi-language subtitle.
- The request preserves Bangumi/AniDB/IMDb work IDs without sending source or
  downloader configuration.
- Fake matcher conflict, configuration failure and network failure are covered.
- Manual offset suppression is covered.
- Metadata Store tests cover AniDB/IMDb propagation in both season and Episode
  claims.
- Full solution tests and `win-x64` NativeAOT publish.
