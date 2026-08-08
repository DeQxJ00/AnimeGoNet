# Renamer and persistent organization closure — 2026-08-08

## Upstream baseline

`wetor/AnimeGo@develop` combines a rename plugin with an in-memory task manager.
The built-in Python plugin writes recognized episodes as
`<anime>/Sxx/Eyyy.ext`, preserves the original filename for unknown Episode
types, and marks the result for scraping. The manager reacts to seeding and
complete states to implement `link`, `link_delete`, `move` and `wait_move`.

## AnimeGoNet replacement

- `anime-library` is a compile-time C# rename plugin; no Python runtime or
  reflection-based plugin loading participates in the built-in path.
- Verified TMDB Series/Season/Episode values exclusively produce canonical
  `<TMDB name>/Sxx/Eyyy.ext` paths. A confirmed Season with an unreliable
  Episode keeps a sanitized source basename under `Other` and creates no fake
  Episode completion.
- Associated subtitles inherit the video's verified Episode and retain
  language, track, default, forced and SDH suffixes. Ambiguous or orphaned
  subtitles enter `Other`.
- SQLite stores immutable source/target operations and separate organization
  and downloader-cleanup leases. Restart retries only pending work; a committed
  identical target is recovered, while conflicting content preserves both
  files.
- `move`, `wait_move`, `link` and `link_delete` use the captured download/save
  roots. NFO and upstream-compatible directory sidecars are committed before
  Episode completions. qB cleanup is a later stage and always uses
  `deleteFiles=false`.
- `MediaOrganizationWorker` continuously advances the persistent processor,
  respects host cancellation, and delays only no-work/retry iterations.

## Automated evidence

- `MediaPathPlannerTests`: canonical TMDB path, confirmed-Season Other,
  sanitation, subtitle suffix and unverified identity rejection.
- `SubtitleAssociationResolverTests`: same-stem and source-EP association,
  ambiguity, orphan and IDX/SUB pairing.
- `SafeFileMoverTests` and `SafeFileLinkerTests`: atomic move/hard link,
  verified-copy recovery, conflicts, path boundaries and link-delete safety.
- `MediaOrganizationProcessorTests`: real disposable-file move/link flows,
  subtitle naming, NFO/sidecar/completion ordering, multi-file resume,
  fallback/Other behavior, seeding gates and retry-safe qB cleanup.
- Existing host lifecycle tests exercise background-worker cancellation. All
  filesystem tests use disposable roots and fake qB clients; they do not touch
  TestSpace, passkeys, cookies or user downloads.

Revalidated on 2026-08-08: Core path/subtitle tests 12/12 and App mover/linker/
organization tests 23/23 passed. The immediately preceding full solution run
remains 1349/1349 with zero failures and zero skips; this closure changes only
documentation, not the tested production or test tree.
