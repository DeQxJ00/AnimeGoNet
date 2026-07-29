# AnimeGoNetData offline import verification — 2026-07-29

Scope: raw ZIP upload, safe extraction into application-owned storage, verified
catalog registration, transaction import and static WebUI entry.

## Invariants

- the endpoint accepts only `application/zip` or `application/octet-stream`;
- the request body is streamed to `data_path/data-update/.partial-*`; the
  browser file name and local path are never sent as metadata or persisted;
- ZIP entries must be root basenames and exactly equal `manifest.json` plus all
  declared asset names;
- directories, nested/path-traversal names, duplicate or extra entries are
  rejected;
- manifest bytes use the v1 strict parser; every extracted asset has exact
  uncompressed length and SHA-256 before package registration;
- gzip/JSONL, order, count, uniqueness and Subject-reference validation reuse
  `DataPackageStore`;
- a cataloged `data_version` remains immutable even when its package directory
  was externally removed;
- invalid archive, checksum or import failure removes partial files and leaves
  active/previous unchanged; a byte-verified package may remain cataloged after
  deeper JSONL import failure but is not activated;
- valid offline import requires no configured manifest URL and performs no
  network request.

## Automated coverage

`DataUpdateApiTests` exercise a complete offline import and verify active and
download-catalog state. Parameterized failures cover extra entries,
path traversal and asset checksum mismatch after a valid baseline import, and
assert the previous active version remains the sole installed/downloaded
version. A separate contract test rejects JSON content type.

`DataUpdateServiceTests.MissingCatalogDirectoryCannotReplaceImmutableVersion`
proves that a missing on-disk directory does not permit replacing an already
cataloged version with a different manifest.

Static asset tests require the offline endpoint, file input and responsive
offline card to be present in generated WebUI resources.

## Validation

```text
npm run web:check
npm run web:build
node --check src/AnimeGoNet.App/wwwroot/app.js
All passed.

dotnet test AnimeGoNet.slnx --no-restore
Plugin abstractions: 11 passed
Core:                263 passed
Data:                146 passed
App:                 416 passed
Total:               836 passed, 0 failed, 0 skipped
```

The win-x64 Release NativeAOT publish completed `Generating native code` without
warnings. The published executable passed `eng/smoke-native.ps1` with schema
v29, `native_aot=true`, SQLite initialization, static WebUI, qB capability and
secure ingest rejection.

An isolated JIT browser run rendered the offline ZIP selector while manifest
configuration was absent. The selector remained enabled, upload stayed disabled
until one file is selected, all online actions stayed disabled, and the page
reported the correct scheduling policy. The browser console contained no
entries. No external endpoint, qBittorrent task or real package was used.
