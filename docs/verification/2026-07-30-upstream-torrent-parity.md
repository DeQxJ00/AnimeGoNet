# Upstream Torrent fixture parity — 2026-07-30

## Source isolation

The authoritative fixtures remain in the separate public repository
`wetor/AnimeGo@c7475dfc55a374cd0dd08821bf17125dab1e3145` under
`internal/pkg/torrent/testdata`. They are not copied into AnimeGoNet and no
Git history is shared.

This is deliberate: `.torrent` announce values are treated as secrets even
when a fixture is public. The parity test reads complete files only in memory
so the existing strict parser can locate the original raw `info` byte range,
but neither the parser result nor assertions contain announce/tracker values.

Locally the test is enabled by:

```powershell
$env:ANIMEGO_UPSTREAM_REPO = 'E:\WorkSpaceAI\AnimeGoNet\AnimeGo'
dotnet test tests/AnimeGoNet.Core.Tests/AnimeGoNet.Core.Tests.csproj `
  -c Release --filter FullyQualifiedName~TorrentUpstreamParityTests
```

The local upstream HEAD was verified as the exact pinned commit. GitHub Actions
checks out the same commit to a separate `upstream-animego` path and sets
`ANIMEGO_UPSTREAM_REPO` for the three-OS build-test matrix. A contract test
pins repository, commit and environment wiring.

## Compared behavior

The four upstream fixtures cover two single-file and two multi-file Torrents.
AnimeGoNet compares:

- SHA-1 of the original raw bencoded `info` bytes against each canonical
  40-character fixture filename;
- Unicode Torrent name;
- total declared size;
- every non-padding relative file path in stable order;
- every individual file size.

Observed parity covers 4/4 Torrents and 17/17 files. Padding remains marked by
the .NET model and is excluded only for comparison with the upstream Go view,
which intentionally skips its padding prefix.

The upstream Go test was also executed from the separate repository after
overriding the host's unrelated Android `GOOS/GOARCH` environment to
`windows/amd64`; it passed and printed the same four hashes, names, totals and
file list. No upstream file was modified or deleted.

## Regression result

The complete .NET Release suite was executed with the upstream path enabled:

```text
AnimeGo.Plugin.Abstractions.Tests   11
AnimeGoNet.Core.Tests              281
AnimeGoNet.Data.Tests              152
AnimeGoNet.App.Tests               505
total                              949 passed, 0 failed, 0 skipped
```

This increment changes only tests, CI wiring, TODO and verification documents;
production IL is unchanged from the immediately preceding safe magnet parser
commit, whose latest win-x64 NativeAOT publish and published-binary smoke
passed. No HTTP request, tracker contact, qB task, passkey, TestSpace read or
download occurred.
