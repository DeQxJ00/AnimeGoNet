# Loopback HTTP fixture verification — 2026-07-30

## Scope and security boundary

`LoopbackHttpFixtureTests` starts a one-shot `TcpListener` on an ephemeral
`127.0.0.1` port and records the raw HTTP request headers. It has no external
network dependency and never starts qBittorrent, creates a Torrent task, reads
TestSpace, or contacts a tracker.

The direct RSS test exercises the normal `RssFeedHttpClient` against a real
chunked HTTP/1.1 response. The lower-level pinned transport tests deliberately
pass `IPAddress.Loopback` as an already validated connection target so the
socket behavior can be observed without DNS. This does not weaken production
policy: `TorrentNetworkPolicy` and `ProfileBoundRssFeedHttpClient` continue to
reject loopback and private addresses before transport invocation.

## Verified behavior

- a chunked UTF-8 RSS response is streamed and parsed through `HttpClient`;
- request path and query are preserved;
- the pinned transport connects only to the supplied validated address;
- the original URI host and port are retained in the `Host` header;
- `User-Agent: AnimeGoNet/1.0` is emitted;
- a `302` response is returned to the caller rather than followed;
- response bytes are streamed from the real socket;
- each fixture accepts exactly one connection;
- request headers are bounded to 16 KiB and fixture lifetime is cancellable.

## Commands and results

Focused verification:

```powershell
dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj `
  -c Release --no-restore `
  --filter FullyQualifiedName~LoopbackHttpFixtureTests
```

Result: 3 passed, 0 failed, 0 skipped.

Complete Release regression with the isolated upstream fixture repository
enabled:

```powershell
$env:ANIMEGO_UPSTREAM_REPO = 'E:\WorkSpaceAI\AnimeGoNet\AnimeGo'
dotnet test AnimeGoNet.slnx -c Release --no-restore
```

```text
AnimeGo.Plugin.Abstractions.Tests   11
AnimeGoNet.Core.Tests              281
AnimeGoNet.Data.Tests              152
AnimeGoNet.App.Tests               508
total                              952 passed, 0 failed, 0 skipped
```

This module changes tests and documentation only. Production IL is unchanged
from the preceding safe magnet parser and upstream parity commits, whose
win-x64 NativeAOT publish and published-binary smoke passed.
