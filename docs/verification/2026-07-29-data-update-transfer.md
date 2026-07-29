# AnimeGoNetData transfer verification — 2026-07-29

Scope: configured manifest check, streaming asset download, verified package
catalog, delayed import and scheduled policy mapping. Web API/UI is the next
module.

## Invariants

- a manual check works independently of `data_update.enabled`; that flag only
  controls whether the scheduled registration is added;
- scheduled policy maps to one explicit action:
  - `auto_download=false`: `check`;
  - `auto_download=true`, `auto_import=false`: `download`;
  - both true: `download_import`;
- manifest responses are read headers-first and capped at 1 MiB;
- each asset is written through a 64 KiB stream into an application-owned
  `.partial-*` directory, with exact Content-Length/actual length/SHA-256;
- only a complete package is moved into
  `data-update/packages/<data_version>`;
- URL values are neither persisted in SQLite nor included in operation errors;
- schema v29 audits check/download/import state and byte progress using an
  autoincrement sequence, so same-timestamp operations retain deterministic
  ordering;
- download-only leaves active data unchanged; later manual import reads the
  verified local manifest and performs no network request;
- HTTP, size or checksum failure removes partial files and leaves the previous
  active data version unchanged.

## Automated evidence

Targeted tests cover check-only, download-only, automatic import, already
current, delayed import without network, corrupt asset, HTTP failure, missing
manifest configuration, all four scheduled policy combinations and stable
scheduled failure propagation.

```text
dotnet test AnimeGoNet.slnx --no-restore
Plugin abstractions: 11 passed
Core:                263 passed
Data:                146 passed
App:                 397 passed
Total:               817 passed, 0 failed, 0 skipped
```

The win-x64 NativeAOT publish completed without warnings. Its isolated startup
smoke returned `database_schema_version=29`, `native_aot=true` and
`runtime_identifier=win-x64` from `GET /api/v1/status`.

No external HTTP endpoint, qBittorrent instance, Torrent or metadata key is used
by these tests.
