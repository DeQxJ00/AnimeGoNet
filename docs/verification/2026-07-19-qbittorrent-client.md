# qBittorrent client verification — 2026-07-19

## Implemented boundary

- NativeAOT-safe `IDownloadClient` and named-instance registry.
- qBittorrent WebUI API login with exact-origin Referer and cookie-capable `HttpClientHandler` per instance; both legacy HTTP 200 + `Ok.` and qBittorrent 5.2 HTTP 204 success responses are accepted.
- Source-generated torrent-list JSON DTO and canonical download-state mapping, including qBittorrent 4 `paused*` and qBittorrent 5 `stopped*` names.
- Multipart `.torrent` add with save path, rename, category, tags, and stopped/paused compatibility fields.
- qBittorrent 5 stop/start and delete form calls.
- Independent `bt` and `pt` clients; only qBittorrent is accepted by the registry.

The wire contract follows the official [qBittorrent 5.0 WebUI API](https://github.com/qbittorrent/qBittorrent/wiki/WebUI-API-%28qBittorrent-5.0%29).

## Evidence

```text
dotnet build AnimeGoNet.slnx --configuration Release --no-restore
0 warnings, 0 errors

dotnet test AnimeGoNet.slnx --configuration Release --no-build --no-restore
AnimeGoNet.Core.Tests: 27 passed
AnimeGoNet.Data.Tests: 10 passed
AnimeGoNet.App.Tests: 16 passed
Total: 53 passed, 0 failed

dotnet restore AnimeGoNet.App.csproj --runtime win-x64 -p:PublishAot=true
dotnet publish ... --runtime win-x64 -p:PublishAot=true
Generating native code
eng/smoke-native.ps1 .../AnimeGoNet.App.exe
Native smoke passed
```

## Explicitly deferred

The adapter is registered but ingest does not dispatch to it yet. Torrent URL security validation, bencode parsing, staging, info-hash acquisition, episode claim/dedup, and durable download-job creation must precede dispatch. A separate external portable qBittorrent sandbox now covers local process/profile/port/username-password/list/path smoke without joining default CI; evidence is tracked in `2026-07-19-local-qbittorrent-sandbox.md`. Real qBittorrent container, reconnect, polling, and file-priority tests remain pending because Docker is unavailable on this host.
