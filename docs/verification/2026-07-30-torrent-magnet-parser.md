# Torrent magnet parser verification — 2026-07-30

## Upstream behavior

`wetor/AnimeGo develop` delegates `LoadMagnetUri` to
`anacrolix/torrent/metainfo.ParseMagnetUri`. The pinned upstream implementation
requires the lowercase `magnet` scheme, uses the first `xt`, requires the
case-sensitive `urn:btih:` prefix, and decodes either:

- 40 hexadecimal characters; or
- 32 uppercase RFC 4648 Base32 characters.

The two upstream test values both resolve to:

```text
f6aa232b3024073c90d04614fcbf050d94fe8ad6
```

It also takes the first `dn` and collects repeated `tr` values.

## AnimeGoNet safety boundary

`TorrentMagnetParser` ports that identity behavior without returning or storing
the raw URI or tracker URLs. Its result contains only lowercase info hash,
decoded display name and tracker count. This prevents a tracker passkey from
appearing in record `ToString()`, API models, exceptions or logs.

The parser additionally applies bounded-input rules:

- maximum URI length 16 KiB;
- maximum 2048 query fields and 1024 trackers;
- maximum display name length 1024 with control characters rejected;
- strict percent encoding, Unicode scalar decoding and UTF-8;
- stable errors that never include the raw value.

This increment intentionally does not add magnet to `/api/v1/ingest`. The
unified import contract continues to require a staged `.torrent` or allowed
Torrent URL, so adding a pure parser cannot bypass host/DNS/redirect/passkey
controls or create a qBittorrent task.

## Verification

The 15 focused tests cover both upstream hashes, uppercase/lowercase hex,
Base32 case behavior, percent/form decoding, first-value selection, repeated
trackers, duplicate `xt`, invalid scheme/prefix/length/encoding/control
characters, bounded input and secret-free diagnostics.

Complete Release test totals:

```text
AnimeGo.Plugin.Abstractions.Tests   11
AnimeGoNet.Core.Tests              279
AnimeGoNet.Data.Tests              152
AnimeGoNet.App.Tests               505
total                              947 passed, 0 failed, 0 skipped
```

The latest source also completed a `win-x64`, .NET 10,
`PublishAot=true` publish with `Generating native code` and passed the
published-binary first-start smoke (schema 30, SQLite, API, static WebUI,
WebSocket, secure ingest and configuration YAML). The parser itself is a
dependency-free Core pure function covered by AOT compatibility analyzers; no
reflection, dynamic code or serializer is used.

No live tracker request, Torrent download, TestSpace access, qB task or private
input was used.
