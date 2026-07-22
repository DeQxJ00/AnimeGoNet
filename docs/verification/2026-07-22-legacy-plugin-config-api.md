# Legacy plugin config API verification — 2026-07-22

## Scope

- Added `POST /api/plugin/config` and `GET /api/plugin/config?name=...` for the built-in C# Mikan filter.
- The legacy names `filter/mikan_tool.py`, `filter/mikan_tool`, `mikan_tool.py`, `mikan_tool`, the optional `plugin/` prefix, and Windows separators resolve to the same built-in adapter.
- POST decodes at most 1 MiB of Base64 JSON, parses `Filiter0` through `Filiter4`, and performs a complete SQLite replacement with `updated_source=legacy_api`.
- GET returns canonical, isomorphic Base64 JSON while preserving ordered `Filiter0` keys, empty strings, duplicate values, case, and Unicode.
- The adapter never discovers, creates, reads, or executes a Python file.

## Contract evidence

`LegacyPluginConfigApiTests` uses a real Kestrel server and verifies:

- the upstream success messages and HTTP 200 + body `code=200` envelope;
- bad aliases, Base64, roots, and tier shapes return HTTP 200 + body `code=300` without leaking paths;
- all supported aliases return the original requested `name`;
- eight concurrent legacy full uploads all commit and advance revision from 1 to 9;
- the persisted source is `legacy_api` and the application test data tree contains no `.py` file.

## Commands

```powershell
dotnet test tests\AnimeGoNet.App.Tests\AnimeGoNet.App.Tests.csproj -c Release --no-restore
dotnet test AnimeGoNet.slnx -c Release --no-restore
dotnet publish src\AnimeGoNet.App\AnimeGoNet.App.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -o artifacts\legacy-plugin-config-win-x64
```

Observed totals: Core 156, Data 60, App 143, total 359/359. The win-x64 NativeAOT publish completed without trimming or AOT warnings; the published executable reported schema 15 and `native_aot=true`, and its plugin-config GET smoke returned `code=200` for `filter/mikan_tool.py`.

## Remaining boundary

This module only supplies compatible configuration storage. Applying `Filiter0..4` ahead of Mikan RSS batch priority, fetching page identity for tiers 1/2/3, recording per-item filter decisions, and the WebUI editor remain separate work.
