# NativeAOT published AI metadata smoke — 2026-08-08

## Scope

The previously verified AI metadata pipeline had deterministic unit, Kestrel,
SQLite and fake-HTTP coverage, but no gate proved that the exact published
NativeAOT executable could run the complete background-worker flow. This module
closes that gap without adding a production test endpoint.

## Fixture and isolation

- `AnimeGoNet.NativeMetadataSmokeFixture seed` uses the production
  `AnimeGoSqliteDatabase`, `IngestTaskStore` and `DownloadJobStore` to create one
  completed-download task containing `Native.AI.S02E07.mkv`.
- `serve` refuses non-loopback listener URLs and exposes deterministic fake AI,
  MCP and TMDB routes plus request counters.
- The AI response first calls the namespaced TMDB MCP tool, then returns one
  `tmdb_id=72517`, Season 2, Episode 7 candidate. Production code must re-fetch
  and validate the fake TMDB Series, Season and Episode before accepting it.
- The qBittorrent instance is disabled. No real Torrent is fetched or added, and
  the fixture has no Bangumi, AniDB or IMDb reference that could trigger an
  unrelated network request.
- All application paths, ports, credentials and state are disposable placeholders
  under a GUID-named system temporary directory. The script always stops the
  owned processes and recursively removes only that validated temporary root.

## Assertions

The smoke starts the same published executable twice: first with workers disabled
to initialize schema 36, then with workers enabled and all metadata endpoints
locked to the loopback fixture. It requires:

- `metadata_resolved` through the public task-detail API;
- authoritative TMDB `72517 / S02 / E07` on the single file;
- both Season and Episode evidence stored with `ai_metadata`;
- AI status `matched` and confidence basis `tmdb_verified`;
- exactly two AI requests, one MCP initialize/notification/tools-list/tool-call,
  and at least one real HTTP request for every TMDB validation level;
- placeholder Authorization/API-key propagation with zero failures;
- no absolute data/download/save path in the AI request body;
- NativeAOT runtime identity, schema 36 and enabled background workers.

## Evidence

Local `win-x64` execution against a fresh NativeAOT publish of the current source
completed the entire request graph and final SQLite/API projection with zero
publish warnings or errors. The accompanying contract tests passed `2/2`, and
the full solution regression passed `1360/1360` with no skips. The five-RID
NativeAOT workflow now invokes the same script for `win-x64`, `win-arm64`,
`linux-x64`, `linux-arm64` and `osx-arm64`.
