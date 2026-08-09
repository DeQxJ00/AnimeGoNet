# Bangumi Archive relation schema v2 verification

## Scope

- `DataManifestParser` accepts legacy schema v1 and schema v2. Schema v2
  requires non-empty `relations` assets and an exact `totals.relations` value.
- schema v42 adds staging and versioned active tables for normalized Bangumi
  Subject relations. Import validates stable order, source shard range,
  uniqueness, counts, and both source/target Subject references before atomic
  activation.
- `BangumiArchiveStore` treats an active v2 package as authoritative for a
  known Subject, including an empty relation list. Active v1 remains a relation
  cache miss and falls back to the configured Bangumi API.
- P3 can therefore traverse Bangumi `relation_type=2` (`前传`) without a live
  Bangumi request. TMDB Series/Season/Episode candidates are still validated
  against the configured TMDB API; the archive does not create TMDB identity.
- `AnimeGoNet.DataBuilder` reads the official root
  `subject-relations.jsonlines`, retains only relations whose two endpoints are
  retained anime Subjects, and emits deterministic relation shards and Release
  checksums. The scheduled build applies a 10,000-record production floor.

## Evidence

- Focused parser tests: 10/10 passed.
- Focused Data builder/import/archive tests: 38/38 passed.
- Focused application cache tests: 6/6 passed.
- Release build: 0 warnings, 0 errors.
- Full suite: 1520/1520 passed (Core 385, Data 217, App 866, plugin projects 52).
- win-x64 NativeAOT publish completed without trim/AOT warnings; first-start
  native smoke passed against schema 42, SQLite, WebUI, and isolated temporary
  directories.
- Docker execution remains intentionally unverified by operator request; the
  repository workflow and generated build path remain covered by static tests.

All relation fixtures use temporary SQLite databases and synthetic Archive
ZIPs. They do not access the user's Bangumi/TMDB credentials, qBittorrent,
Torrent tasks, TestSpace downloads, Cookie, or passkey.
