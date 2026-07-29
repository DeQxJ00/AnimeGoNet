# Built-in C# plugins verification — 2026-07-29

## Registered implementations

| Category | Stable ID | Existing business implementation |
|---|---|---|
| source | `mikan`, `u2`, `ttg` | unified ingest source normalization |
| feed | `mikan-rss` | bounded `RssFeedReader` and safe profile-bound HTTP transport |
| parser | `mikan-title` | `MikanRssEpisodeParser` |
| filter | `mikan-tool` | persisted `MikanLegacyFilterProcessor` / five Filiter tiers |
| rename | `anime-library` | `MediaPathPlanner` |
| schedule | `staged-torrent-dispatch` | `StagedTorrentDispatcher` |

Every instance is constructed by C# code and passed to `PluginCatalog`. No assembly discovery, plugin-directory scan, dynamic DLL load, Python runtime, or script execution path was added.

## Runtime wiring

- Legacy `/api/rss` resolves and executes `mikan-rss`.
- `MikanRssIngestProcessor` resolves `mikan-tool` and `mikan-title`; the host validates filter revision/enabled metadata, exact item count, unique indexes, state names, and accepted-state consistency before planning.
- Existing ordered RSS rules still run after the legacy filter and parsed source EP classification.
- `MediaOrganizationProcessor` resolves `anime-library`; it rejects an unmatched/error result and still passes the returned relative path through `PathBoundary`.
- `StagedTorrentDispatchWorker` resolves `staged-torrent-dispatch`; the plugin executes the real dispatcher and returns the next idle/retry delay.

## Focused evidence

- Core plugin tests cover all built-in category registrations, normal/fractional Mikan parsing, safe media naming, source routing, unknown adapters, and invalid source output.
- App plugin tests cover all six categories in the composed host, absence of Python entries, stable feed error mapping, persisted Mikan filter delegation, and real no-work schedule execution.
- Existing Mikan RSS ingestion, five-level filter, legacy RSS API, and real filesystem media organization tests exercise the newly routed production paths.

Observed Release totals: Plugin Abstractions 4, Core 215, Data 100, App 283; total 602/602, with zero failures and zero skips. The Release solution build completed with zero warnings and zero errors.

`win-x64` NativeAOT publish completed without trim/AOT warnings. The complete bundle was copied to the ignored TestSpace integration directory and started with background workers disabled plus the isolated TestSpace data/download/save paths. Process smoke verified `GET /ping` (`200/pong`), `GET /` (HTTP 200), and the feed-plugin route through `POST /api/rss` with an intentionally invalid local URL (`code=300`, stable `rss_url_invalid` classification). The exact test process was stopped afterward.
