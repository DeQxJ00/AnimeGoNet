# AnimeGoNetData package storage verification — 2026-07-29

Scope: client-side local package validation, staging import, version activation,
retention and rollback. Network manifest checks/downloads, scheduling and Web UI
are deliberately outside this commit.

## Contract exercised

- schema v28 creates version/state/run/staging/archive tables and an exclusive
  active-version index;
- compressed assets must be regular files with exact manifest size and SHA-256;
- gzip JSONL is consumed with a 64 KiB read buffer and a 1 MiB maximum record,
  without materializing the full asset;
- UTF-8 BOM, CRLF, missing final LF, invalid JSON/date/decimal/range/order/count,
  duplicate IDs and missing Subject references are rejected with stable codes;
- decimal Bangumi episode evidence such as `48.5` is retained as text;
- only fully validated staging rows are copied into versioned archive tables;
- active/previous switching and run completion happen in one transaction;
- retention always preserves active and previous, explicit rollback swaps them;
- an installed `data_version` is immutable, while an identical active import is
  idempotent;
- failed and cancelled runs leave the prior active version intact and remove
  their staging rows.

## Evidence

```text
dotnet test AnimeGoNet.slnx --no-restore
Plugin abstractions: 11 passed
Core:                263 passed
Data:                145 passed
App:                 384 passed
Total:               803 passed, 0 failed, 0 skipped
```

```text
dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj \
  -c Release -r win-x64 --self-contained true -p:PublishAot=true
Result: native code generation succeeded with no warnings.
```

The published executable was started against isolated test directories with
background workers disabled. `GET /api/v1/status` returned:

```json
{
  "database_schema_version": 28,
  "native_aot": true,
  "runtime_identifier": "win-x64"
}
```

No real qBittorrent task or external metadata request was made.
