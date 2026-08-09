# Legacy global proxy compatibility verification — 2026-08-01

> Superseded on 2026-08-09 before any production deployment. The owner confirmed there is no historical runtime data to preserve, so the legacy and per-client proxy keys described below were removed instead of migrated.

## Mapping

Upstream AnimeGo exposes one `ANIMEGO_PROXY_URL`. AnimeGoNet has independent
TMDB and Bangumi proxy settings, so compatibility is defined explicitly:

1. `tmdb_proxy_url` or `bangumi_proxy_url` wins for its own client;
2. otherwise `ANIMEGO_PROXY_URL` applies to that client;
3. otherwise canonical `metadata.*.proxy_url` YAML is used;
4. an explicitly present empty value clears the applicable proxy instead of
   falling through to YAML.

Consequently an empty legacy global variable disables both clients, matching
the upstream `Proxy.Enable=false` behavior. A configured global value locks
both `tmdb_proxy_url` and `bangumi_proxy_url` in the management API/WebUI.
Private configuration cannot replace either field or silently persist the
environment value.

## Automated evidence

The targeted Release run passed 9/9. Tests cover:

- one global HTTP proxy mapped to both metadata clients;
- a client-specific SOCKS5 proxy overriding the legacy global proxy;
- explicit empty global value overriding two non-empty YAML proxy values;
- case-insensitive environment-lock discovery for both fields;
- `/api/v1/config` projection of both locks and rejection of a request that
  attempts to modify both values, with no private file written.

Tests use only in-memory configuration, fake endpoint values and disposable
application data. No external proxy, qBittorrent, TMDB/Bangumi request, API key,
Cookie, passkey or TestSpace file is accessed.

The complete Release suite passed 1314/1314:

```text
AnimeGo.Plugin.Abstractions.Tests   12
AnimeGo.Plugin.Sdk.Tests            16
AnimeGo.PluginTool.Tests            23
AnimeGoNet.Core.Tests              330
AnimeGoNet.Data.Tests              176
AnimeGoNet.App.Tests               757
```

The final `win-x64` Release NativeAOT publish completed `Generating native
code` without trim/AOT warnings. The exact executable passed the first-start
and legacy-YAML-upgrade modes of `eng/smoke-native.ps1`, including schema v36,
OpenAPI, static WebUI, WebSocket and cleanup checks.
