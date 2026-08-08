# External qBittorrent deployment verification — 2026-08-08

## Scope

- `docker-compose.external-qbittorrent.yml` starts AnimeGoNet without bundling a
  qBittorrent service and configures independent `bt` and `pt` WebUI endpoints.
- Every endpoint, username, password and AnimeGoNet Access Key is supplied by a
  required environment variable. `.env` and `.env.*` are ignored by Git.
- AnimeGoNet keeps the official `/data`, `/download/incomplete` and
  `/download/anime` paths. Both external qB instances use fixed instance paths
  below the same `/download` mount.
- The deployment guide covers same-host containers and a remote host backed by
  a shared filesystem, qB default-save-path configuration, source routing,
  connection tests, hard-link path probes, explicit test-Torrent boundaries and
  precise cleanup.

## Automated verification

The two delivery contract tests parse the Compose document with YamlDotNet and
verify that it contains exactly one service, long-form `/data` and `/download`
bind mounts, both downloader paths, required secret variables, a read-only root
filesystem and `no-new-privileges`. They also check that the guide requires
identical container paths and documents all four downloader diagnostics.

```text
ExternalQbittorrentDeploymentContractTests: 2/2 passed
```

The complete Release suite ran with the fixed upstream repository and passed
with zero failures and zero skips:

```text
AnimeGo.Plugin.Abstractions.Tests  13
AnimeGoNet.Core.Tests             339
AnimeGo.Plugin.Sdk.Tests           16
AnimeGoNet.Data.Tests             189
AnimeGo.PluginTool.Tests           23
AnimeGoNet.App.Tests              789
Total                            1369/1369
```

After a dedicated `win-x64`, `PublishAot=true` restore, the final publish emitted
`Generating native code`. The resulting 38,304,768-byte native executable passed
`eng/smoke-native.ps1` in first-start mode, including schema 36, SQLite, static
WebUI, WebSocket, canonical YAML, API and clean process shutdown checks. The
smoke uses an OS-assigned loopback port and disposable directories with
background workers disabled; it does not contact qBittorrent.

## Environment boundary

Docker CLI is unavailable on this Windows host, so no local external-container
success is claimed. The real cross-container/network-filesystem integration
gate remains separately open in `TODO.md` and the Docker CI workflow. This
module did not read or modify TestSpace, a real Torrent, qBittorrent Cookie,
WebUI credential, passkey or private download.
