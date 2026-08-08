# AnimeGoNetData immutable release verification — 2026-08-08

## Delivered contract

- DataBuilder writes `SHA256SUMS` in ordinal file-name order using lowercase
  SHA-256. It covers `manifest.json`, every online JSONL gzip asset and the
  offline ZIP, while intentionally excluding the checksum file itself.
- The scheduled/manual workflow requires the independent repository variable
  `ANIMEGONET_DATA_REPOSITORY`, rejects the main application repository, and
  uses the optional target branch variable `ANIMEGONET_DATA_TARGET` plus the
  scoped `ANIMEGONET_DATA_TOKEN` secret.
- The upstream Archive UTC timestamp down to the second forms the data version,
  so two distinct upstream exports from one day cannot silently share a tag.
- A new tag begins as a draft. Existing assets are downloaded and byte-compared;
  only missing draft assets may be uploaded. The exact remote name set and every
  asset byte are verified again before the release becomes public/latest.
- An already published tag is read-only: incomplete, duplicate or different
  bytes fail the job, and the workflow never uses `--clobber`.

Publishing the fully verified release last makes GitHub's
`/releases/latest/download/manifest.json` pointer the atomic latest-manifest
switch. No mutable duplicate manifest is maintained.

## Tests

The focused DataBuilder suite passed 9/9. It creates real ZIP fixtures and
verifies production-store import, manifest/asset hashes, the offline package,
the new checksum list, byte-identical repeated output, corrupt upstream hash,
duplicate IDs, dangling Episode references, minimum production count gates,
oversized lines, YAML parsing and the immutable workflow contract.

The complete Release solution suite passed with zero failures and zero skips:

```text
AnimeGo.Plugin.Abstractions.Tests  13
AnimeGoNet.Core.Tests             339
AnimeGo.Plugin.Sdk.Tests           16
AnimeGoNet.Data.Tests             189
AnimeGo.PluginTool.Tests           23
AnimeGoNet.App.Tests              789
Total                            1369/1369
```

The extracted publication shell block passed `bash -n` using its original LF
bytes. A dedicated win-x64 `PublishAot=true` restore/publish emitted
`Generating native code`; the resulting native DataBuilder executable returned
its complete CLI help contract successfully. The builder's full checksum path
is executed by the real ZIP fixture tests; the native smoke proves compilation
and startup rather than claiming an external GitHub publication.

## External boundary

No `ANIMEGONET_DATA_REPOSITORY` or write token is configured in this local
worktree, so no external repository, tag, Release or latest pointer was changed.
The first real publication remains an explicit repository-owner acceptance gate
in `TODO.md`. No TestSpace, qBittorrent, TMDB credential, Torrent URL, passkey or
private media was read or modified.
