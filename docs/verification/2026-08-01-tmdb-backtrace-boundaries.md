# TMDB P3 backtrace boundary verification

## Scope

The P3 `TMDBFailBacktrace` resolver requires a positive `bgmid`, walks only Bangumi `前传` relations breadth-first, and evaluates every predecessor as one combined Japanese-name/Chinese-name TMDB Series+Season search. A candidate is successful only after both TMDB Series details and the air-date-selected Season endpoint validate.

## Deterministic fixtures

`BangumiSeasonBacktraceResolverTests` now proves:

- no predecessor returns `tmdb_backtrace_exhausted` without a TMDB request;
- missing dates remain traversable but are not used to invent a Season;
- same-level predecessors are ordered by latest air date then lowest subject ID;
- cycles terminate through the visited set;
- the earliest work still failing exhausts all title cleanup rounds before returning no match;
- a TMDB transport failure retains its stable failure kind/code and access-confirmed flag;
- a Bangumi relation failure propagates its stable kind/code to the outer processor;
- cancellation interrupts the active relation request and produces no fallback result.

Existing processor fixtures additionally prove that retryable P3 failures are audited before the independently configured AI/P2/P1 stages continue. No live TMDB/Bangumi credential or network call is used by this module.

## Release gate

- Targeted P3 tests passed 9/9.
- Release solution build passed with zero warnings and zero errors.
- The complete suite passed 1076/1076: Plugin Abstractions 11, Core 324, Data 171, App 570.
- Production source code is unchanged from the schema v36 NativeAOT binary that passed first-start and legacy-YAML-upgrade smoke; this commit only adds deterministic tests and traceability documentation.
