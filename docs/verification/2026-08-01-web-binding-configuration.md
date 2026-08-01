# Web binding configuration verification — 2026-08-01

## Behavior

AnimeGoNet now owns a strongly typed Web listener configuration instead of
depending on the framework's unrelated development default:

- native first start: `127.0.0.1:7991`;
- Docker first start: `0.0.0.0:7991`;
- canonical YAML: `web.host` and `web.port`;
- upstream compatibility: `ANIMEGO_WEB_HOST` and `ANIMEGO_WEB_PORT`;
- standard override: `--urls` or `ASPNETCORE_URLS` remains authoritative.

Legacy `setting.webapi.host/port` values are carried into the canonical YAML
during the same exact-version migration chain. Host input is restricted to a
trimmed DNS name or IP address; schemes, paths and malformed hosts are rejected.
Ports outside 0–65535 are rejected. Port 0 is retained only for controlled
ephemeral test listeners.

Docker continues to require a non-empty Access Key before it can start. Native
mode remains loopback-only unless the operator explicitly changes the binding.

## Automated evidence

Targeted Release tests passed:

```text
AnimeGoNet.Core.Tests  9 passed
AnimeGoNet.App.Tests  43 passed
```

The App set includes two real Kestrel socket tests. One proves the legacy host
and port control the actual listener; the other configures a conflicting legacy
binding and proves `--urls=http://127.0.0.1:0` wins. The 12 pinned upstream YAML
goldens additionally prove every historical fixture preserves `localhost:7991`
through canonical migration.

All tests use disposable paths and OS-assigned loopback ports. No qBittorrent,
TMDB key, Cookie, passkey, TestSpace profile or user configuration is read.

The complete Release suite passed 1310/1310:

```text
AnimeGo.Plugin.Abstractions.Tests   12
AnimeGo.Plugin.Sdk.Tests            16
AnimeGo.PluginTool.Tests            23
AnimeGoNet.Core.Tests              330
AnimeGoNet.Data.Tests              176
AnimeGoNet.App.Tests               753
```

## NativeAOT

The final `win-x64` Release publish completed `Generating native code` with
`PublishAot=true` and no trim/AOT warning. The exact executable passed both
`eng/smoke-native.ps1` modes: first start and legacy YAML upgrade. Both smokes
use `--urls` with an OS-assigned loopback port, so this also proves the standard
override remains effective in the published native process while schema v36,
OpenAPI, static WebUI, WebSocket and cleanup checks continue to pass.
