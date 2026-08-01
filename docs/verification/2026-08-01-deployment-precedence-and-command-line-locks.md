# Deployment precedence and command-line lock verification — 2026-08-01

## Resolved gap

Legacy and canonical aliases previously used a fixed key order after each
provider had already resolved its own key. Consequently a lower-priority
environment alias such as `ANIMEGO_DATA_PATH` could incorrectly beat a
higher-priority `--data_path`. Application private configuration locks also
recognized only environment names and omitted ten editable season/fallback/
Torrent fields.

Alias resolution now inspects configuration providers from highest to lowest,
then applies compatibility alias order inside that one provider. This preserves
command line → environment → YAML → defaults across different key spellings.
The same resolver covers config-file selection, three paths, Web binding,
metadata, unified AI legacy switches, named downloaders and SourceProfiles.

Application locks now recognize flat, upstream and canonical double-underscore
environment names plus command-line keys. The API keeps the existing
`environment_variables` property and adds `command_line_arguments`,
`controlling_keys`, and an accurate combined source. Values after `=` are never
retained or returned. All editable application fields are locked and reapplied
after private JSON, including the four season-failure policies, Bangumi final
fallback, trusted Mikan offset cache and four Torrent-fetch settings.

## Focused evidence

The focused Release runs passed 21/21 across provider-priority, upstream path,
named downloader/source, unified AI, application-lock, private-inheritance and
configuration API tests. The broader configuration/API run passed 149/149.
Strict TypeScript checking, deterministic WebUI generation and the five Node
HTTP/security tests passed.

Tests use fake URLs, disposable paths and supplied deployment-name lists. They
do not read process secrets, connect to qBittorrent/TMDB/Bangumi, or touch
TestSpace.

## Release gates

The complete Release suite passed 1329/1329:

```text
AnimeGo.Plugin.Abstractions.Tests   12
AnimeGo.Plugin.Sdk.Tests            16
AnimeGo.PluginTool.Tests            23
AnimeGoNet.Core.Tests              330
AnimeGoNet.Data.Tests              177
AnimeGoNet.App.Tests               771
```

The final `win-x64` Release NativeAOT publish completed native-code generation
without trim/AOT warnings. The exact executable passed both first-start and
legacy-YAML-upgrade modes of `eng/smoke-native.ps1`, including schema v36,
SQLite initialization, OpenAPI, static WebUI, WebSocket and process cleanup.
