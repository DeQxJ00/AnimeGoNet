# Unified ingest and Mikan RSS rules verification — 2026-07-19

## Implemented boundary

- Strongly typed `source + data[].torrent + data[].info` batch contract.
- Mikan/U2/TTG source normalization, legacy `name`/`url` aliases, positive ID validation, canonical IMDb ID, and conflicting `mikanid` evidence rejection.
- `/api/v1/ingest` per-item accepted/rejected results without returning the passkey URL.
- Legacy `/api/download/manager` adapter using the same command normalizer, source profile lookup, immutable route snapshot, and explicit SQL task store.
- Seeded source routing: Mikan → `bt` with `move`. A test-only U2 profile verifies routing to the separately named `pt` qBittorrent instance; U2/TTG default file strategy remains deliberately unset pending the recorded product decision.
- Configurable pure C# Mikan batch rule engine: blacklist before whitelist, invariant lowercase matching, reliable `(mikanid, episode kind, episode)` grouping, single-candidate bypass, ordered priority arrays, immediate winner short-circuit, and stable RSS-order fallback.
- Default 720p blacklist and four editable preset groups.

## Evidence

```text
dotnet build AnimeGoNet.slnx --configuration Release --no-restore
0 warnings, 0 errors

dotnet test AnimeGoNet.slnx --configuration Release --no-build
AnimeGoNet.Core.Tests: 19 passed
AnimeGoNet.Data.Tests: 10 passed
AnimeGoNet.App.Tests: 9 passed
Total: 38 passed, 0 failed

dotnet restore AnimeGoNet.App.csproj --runtime win-x64 -p:PublishAot=true
dotnet publish ... --runtime win-x64 -p:PublishAot=true
Generating native code
eng/smoke-native.ps1 .../AnimeGoNet.App.exe
Native smoke passed
```

The AOT smoke posts a Mikan batch, requires one accepted item routed to `bt`, checks the 64-character URL fingerprint, and still validates SQLite plus static WebUI startup.

## Explicitly deferred

This increment does not fetch or parse the Torrent URL, invoke qBittorrent, parse `/api/rss`, persist editable rule definitions, or apply episode completion dedup to ingest. The stored task status is therefore `received`; it is not represented as downloaded or fully accepted by a downstream client. Passkey URL staging, host/redirect/DNS controls, bencode verification, and immediate cleanup are the next network boundary and must land before qBittorrent dispatch.
