# Manual Torrent and RSS submission verification — 2026-07-29

## Implemented boundary

- The static TypeScript WebUI submits one Torrent through the existing unified `/api/v1/ingest` pipeline.
- Enabled SourceProfile IDs populate the source selector, so the task keeps that profile's downloader, file policy and revision snapshot.
- Mikan manual submission requires `mikanid` and `bgmid`; U2 retains optional work-level references.
- New `POST /api/v1/rss/ingest` accepts an explicit enabled Mikan SourceProfile and then reuses legacy filtering, ordered priority rules, winner leasing and unified staging.
- A non-Mikan profile is rejected before the RSS URL is fetched.
- Torrent/RSS URL fields use password inputs, are cleared immediately after request construction and are never rendered back. Results contain only stable task/batch data and irreversible fingerprints.

The old `/api/rss` contract remains unchanged and now shares the same bounded Mikan feed conversion helper.

## Automated verification

```text
dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj --no-restore \
  --filter "FullyQualifiedName~ManualSubmissionApiTests|FullyQualifiedName~StaticWebUiTests"
Passed: 36, Failed: 0

dotnet test AnimeGoNet.slnx --no-restore -c Release --verbosity minimal
Plugin: 11 passed
Core: 215 passed
Data: 109 passed
App: 326 passed
Total: 661 passed, 0 failed
```

The API fixtures prove that a custom Mikan profile is persisted on the RSS batch, a U2 profile causes zero RSS transport calls, and neither success nor failure responses echo the test passkey.

TypeScript strict checking passed. Two consecutive builds produced identical `wwwroot/app.js` SHA-256:

```text
4DD47003EF806254A32442AE47868283B3530A6ED0157162D7679F4887521AD9
```

## Browser verification

An isolated local JIT instance was exercised with deliberately rejected loopback URLs, not real Torrents or private RSS:

- both source selectors loaded the persisted `mikan → bt, revision 1` route;
- the Mikan-specific `mikanid`/`bgmid` requirement and route hint appeared;
- after each submit, the sensitive input value was empty;
- the fake passkey did not occur in visible DOM text;
- rejected Torrent and RSS results displayed only sanitized stable failure text;
- desktop two-column layout rendered without overlap.

The browser-control workflow led to explicit verification of keyboard-safe native form controls and immediate sensitive-field clearing; no real passkey or user qBittorrent task was used.

## NativeAOT verification

```text
dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishAot=true \
  -o artifacts/manual-submit-native-win-x64
Generating native code

eng/smoke-native.ps1 \
  -Executable artifacts/manual-submit-native-win-x64/AnimeGoNet.App.exe
Native smoke passed
```

The native executable passed `/ping`, schema v23 and SQLite initialization, NativeAOT capability, secure ingest rejection, qBittorrent capability and static WebUI checks.
