# TMDB Season failure-chain closure — 2026-08-08

## Confirmed semantics

The chain is evaluated in descending priority:

1. **P4 `TMDBFailSkip`** stops the item only after the normal verified TMDB
   Series/Season path fails.
2. **P3 `TMDBFailBacktrace`** requires `bgmid`. Each Bangumi predecessor is
   treated as a new joint Series/Season attempt: its Japanese name and Chinese
   name are searched, a TMDB Series is selected, its details are fetched, the
   predecessor air date is matched to a Season, and that exact Season endpoint
   is verified. Failure continues through every predecessor level with cycle
   protection.
3. **P2 `TMDBFailUseTitleSeason`** reads only the immutable task title and
   locally parses an explicit Season. It does not validate a TMDB Season.
4. **P1 `TMDBFailUseFirstSeason`** locally selects S01. It does not validate a
   TMDB Season.

AI metadata matching is one independent task-level Series/Season/Episode flow
between P3 and P2 when enabled. It is not a second P3 or a separate AI Episode
fallback.

## Controlled network fixture

`BangumiSeasonBacktraceLoopbackTests` starts a real Kestrel listener on an
ephemeral loopback port and uses the production `BangumiSubjectClient`,
`TmdbClient`, retry executor, title resolver, Season resolver and backtrace
resolver. The fixture proves in one request chain that:

- a Bangumi relations request returning HTTP 503 is retried and then yields the
  first predecessor;
- the first predecessor resolves a real TMDB Series but its Season air date
  does not match, so P3 continues instead of reusing that `tmdbid`;
- the next Bangumi predecessor is fetched and searched as a new identity;
- a TMDB search returning HTTP 429 is retried;
- the second predecessor's Series details and exact Season endpoint are both
  verified before P3 succeeds;
- three Bangumi subjects were visited, with no public network or user secret.

The existing deterministic tests continue to cover no-prequel, multi-level,
same-level ordering, missing dates, cycles, complete exhaustion, Japanese then
Chinese names, different-Series recovery, network/protocol failures and
cancellation. Processor tests prove the P4→P3→AI→P2→P1 ordering and audit
records.

Revalidated on 2026-08-08: the focused backtrace suite passed 10/10 and the
complete solution passed 1351/1351 with zero failures and zero skips.
`dotnet format --verify-no-changes` and `git diff --check` passed. This closure
adds only a test and documentation, so the production source remains identical
to the previously successful win-x64 NativeAOT publish.
