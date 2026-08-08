# AI input secret boundary verification — 2026-08-08

## Boundary

`AiMetadataInputBoundary` is the only task-to-matcher projection. It constructs
the request from the task title, candidate video relative paths and byte sizes,
optional Bangumi/AniDB/IMDb work IDs, actual Torrent file count, and the already
gated Mikan publication evidence. It deliberately has no parameter or output
field for the Torrent URL/fingerprint, announce dictionary, info-hash, staged
bytes, immutable route snapshot, Cookie, Authorization or downloader secret.

Metadata claim SQL independently selects only the approved task/source fields
and task-file rows; it never selects `torrent_url_fingerprint`. The prompt
renderer continues to write every allowed field explicitly rather than
serializing a task, route or database record.

## Tests

Focused resolver and unified-ingest tests passed 4/4:

- the public properties of `AiMetadataMatchInput` and `AiMetadataFileInput` must
  exactly equal the approved evidence list, so adding a field breaks the gate;
- run ID, task ID, lease, source adapter and raw source evidence canaries are
  absent from the matcher request;
- a unified ingest uses a synthetic passkey-bearing Torrent URL, stages and
  persists the task, and runs the real metadata processor with a fake matcher;
- the test reads the actual URL fingerprint from SQLite and proves the complete
  URL, passkey value and fingerprint are absent from both the matcher record and
  the rendered authoritative Prompt.

The complete Release suite passed with zero failures and zero skips:

```text
AnimeGo.Plugin.Abstractions.Tests  13
AnimeGoNet.Core.Tests             339
AnimeGo.Plugin.Sdk.Tests           16
AnimeGoNet.Data.Tests             189
AnimeGo.PluginTool.Tests           23
AnimeGoNet.App.Tests              790
Total                            1370/1370
```

A dedicated win-x64 `PublishAot=true` restore/publish emitted `Generating native
code`. The exact native executable passed `eng/smoke-native.ps1` in first-start
mode, including schema 36, SQLite, canonical YAML, API/static WebUI/WebSocket,
plugin discovery and clean shutdown.

All transports in these tests are fakes or loopback fixtures and all databases
and files use disposable directories. No real AI/TMDB/Bangumi/qBittorrent
endpoint, TestSpace content, private Torrent URL, passkey, Cookie or API key was
read or modified.
