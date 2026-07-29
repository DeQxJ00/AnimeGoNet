# C# plugin abstractions and catalog verification — 2026-07-29

## Scope

- Added the NativeAOT-compatible `AnimeGo.Plugin.Abstractions` project.
- Added stable DTOs and executable contracts for source, feed, parser, filter, rename, and schedule plugins.
- Added an explicit `PluginCatalog`; it performs no assembly discovery and has no DLL loading path.
- Registered Mikan, U2, and TTG adapters by direct construction.
- Routed both unified ingest and source-route preview through the catalog.
- Retained the synchronous normalizer only as a test/backward-compatible facade over the same built-in catalog.

## Safety invariants

- IDs are lowercase ASCII segments and are unique across all categories.
- A plugin implements exactly one category and its descriptor must agree with that category.
- Execution order is deterministic: configured order, then stable ID.
- Unknown adapters are rejected before torrent staging.
- Source plugin output is not trusted: source ID, HTTP(S) URL, non-empty title, and lowercase SHA-256 fingerprint are revalidated by the host.
- No `Assembly.Load*`, `Reflection.Emit`, MEF, runtime proxy, or filesystem plugin scan is used.

## Tests

- `PluginCatalogTests`: deterministic order, duplicate ID, malformed descriptor, category mismatch, category-safe lookup, and unknown plugin.
- `BuiltInPluginCatalogTests`: explicit source registrations, real Mikan normalization, unknown adapter rejection, and malicious/invalid output rejection.
- Existing ingest, storage, API, and staging tests continue to cover the normalized result.

Observed Release totals: Plugin Abstractions 4, Core 213, Data 100, App 279; total 596/596, with zero failures and zero skips. The solution build completed with zero warnings and zero errors.

`win-x64` NativeAOT publish completed without trim/AOT warnings. The published output was copied as a complete bundle into the ignored local TestSpace integration directory, started with background workers disabled and the isolated TestSpace data/download/save paths, then verified with `GET /ping` (`code=200`, `msg=pong`) and `GET /` (HTTP 200, AnimeGoNet WebUI present). The exact smoke process was stopped after verification.
