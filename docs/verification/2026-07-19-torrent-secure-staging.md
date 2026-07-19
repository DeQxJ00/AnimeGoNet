# Secure Torrent staging verification — 2026-07-19

## Scope

- Added strict BitTorrent v1 Bencode parsing with canonical integers/dictionaries, bounded nesting, exact original `info` byte SHA-1, single/multi-file projection, padding identification, and unsafe path rejection.
- Added per-SourceProfile Torrent host allowlists to configuration, SQLite schema v2, source records, and immutable ingest route snapshots.
- Added a manual redirect pipeline. Every hop validates the allowlisted host, resolves DNS again, rejects the complete response if any address is private/special, and pins the connection to the validated address set.
- Added fetch timeout, redirect count, response byte limit, HTTPS downgrade rejection, staging files under `data_path/staging`, Unix mode `0600`, explicit consumer disposal, and TTL crash cleanup.
- Public failure codes and messages never contain URL path/query/fragment. Network exceptions from the HTTP stack are not retained as inner exceptions because they can embed a passkey URL.

## Automated evidence

```text
dotnet test AnimeGoNet.slnx --no-restore
Core: 34 passed
Data: 10 passed
App: 31 passed

dotnet build AnimeGoNet.slnx -c Release --no-restore
0 warnings, 0 errors

dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 --no-restore
Generating native code; NativeAOT publish succeeded without warnings

eng/smoke-native.ps1 -Executable artifacts/torrent-staging-aot-win-x64/AnimeGoNet.App.exe
Native smoke passed
```

The tests cover valid staging and disposal, original-info hashing, multi-file and padding projection, path traversal, malformed Bencode, source host rejection, cross-host redirect rejection before a second DNS lookup, loopback/private/link-local/ULA rejection, streaming overflow cleanup, secret-free exceptions, invalid metainfo cleanup, wildcard semantics, and TTL cleanup.

## Security and lifecycle boundary

The service is registered in the application but is not yet scheduled by `/api/v1/ingest`. This increment therefore performs no live Torrent request and creates no qBittorrent task. The next worker increment must load the stored profile revision/allowlist snapshot, stage the URL while keeping it ephemeral, create file/episode claims, submit the staged stream to the selected qBittorrent instance, and dispose the staged file only after qB confirms receipt. AI inputs must be constructed only from the returned title/file-size projection and must never receive the staged bytes, announce fields, or secret URL.
