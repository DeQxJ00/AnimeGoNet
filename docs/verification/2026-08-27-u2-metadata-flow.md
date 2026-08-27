# U2 metadata flow verification — 2026-08-27

Scope is restricted to `source_adapter=u2`; formal AI Prompt files were not changed.

- U2 plugin `media_type=tv|movie` is persisted exactly as submitted and is not inferred from release names.
- U2 TV without AniDB uses AnitomySharp's anime-title element for TMDB TV search.
- With AniDB direct mapping enabled, `tmdbtv` is validated without title search. With it disabled, local AniDB titles are searched in official-first order. In both modes `tmdbseason` may select a valid regular Season when a Series has multiple regular Seasons.
- Season 0 is excluded. A single regular Season can be selected locally; an unresolved multi-Season result proceeds to the existing unified task AI.
- The deterministic Episode gate compares every main video in the Torrent with every normal Episode in the verified TMDB Season. Exact count, exact positive-number set, and one-to-one mapping are required.
- Single/incomplete/superset/duplicate/cross-season/conflicting/unparsed groups enter the unified AI once. Explicit unnumbered NCOP/NCED/extras do not trigger AI when the main full-season set is exact and remain Extras.
- U2 AI results use the existing validator for Series/Season/Episode identity and duplicate target rejection. An unmatched ordinary video is additionally rejected with `ai_u2_main_video_unmatched`; only explicit Extras may remain unmatched.
- Trusted EP Offset guards remain Mikan-only. Existing Mikan processor tests are part of the regression filter.

Verification performed:

- U2-focused App tests: 26 passed.
- Automatic/Episode/AI/Mikan regression filter: 97 passed.
- NativeAOT `win-x64` publish completed successfully.

Movie boundary: explicit U2 `media_type=movie` still routes to the existing `TmdbMovieResolver`. A unified Movie AI workflow has not been specified and is intentionally not invented in this change.
