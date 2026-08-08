# Configuration model and validation closure — 2026-08-08

## Upstream field disposition

The baseline is `wetor/AnimeGo@develop` at the commit pinned in the porting
checklist. Every business leaf in `configs/models.go` has one explicit owner:

| Upstream fields | AnimeGoNet disposition |
|---|---|
| client type/URL/user/password/download path | named qBittorrent instances; Transmission is a fail-closed diagnostic |
| data/download/save path | `PathOptions`, absolute and boundary checked |
| category and dynamic tag | immutable SourceProfile route snapshot |
| Web host/port/access key | deployment-only Web binding and private authentication |
| global proxy | migrated to independent TMDB and Bangumi proxy values |
| Mikan Cookie | private, write-only SourceProfile credential |
| Bangumi/TMDB redirects and TMDB key | independent metadata clients |
| request timeout/retries/wait | independent TMDB and Bangumi transport policies |
| rename strategy and seeding time | SourceProfile file strategy and immutable download snapshot |
| TMDB failure switches | P4/P3/P2/P1 policy; new risky switches default off |
| TMDB cache hours and database Cron | SQLite TMDB cache TTL and directory refresh schedule |
| Mikan/Bangumi cache hours | durable identity evidence and versioned AnimeGoNet Data replace expiring in-memory caches |
| `allow_duplicate_download` | replaced by mandatory exact TMDB+EP deduplication |
| `refresh_second`, feed delay and qB polling knobs | bounded independent HostedService schedules and circuit/retry policies |
| Python plugin lists | compile-time built-ins plus explicit RID-specific C# process packages |

Upstream example passwords, Access Keys and the bundled TMDB key are
deliberately not defaults. Mikan defaults to `move` as required by AnimeGoNet,
and AI, trusted offset learning and full Bangumi fallback default to disabled.

## Initialization and validation

- First start creates the deployment YAML with `CreateNew`; Unix permissions
  are `0600`. Concurrent first starts read the winning complete file.
- Legacy YAML is bounded, strict UTF-8, one mapping document, depth/node
  limited and duplicate-key rejecting. All known scalar types and the exact
  13-version allow-list are checked before a backup or atomic replacement.
- The 12 pinned upstream golden YAML files prove preservation of every retained
  field across historical layouts. Unsupported Transmission remains untouched
  and blocks downloader work.
- Canonical options reject non-absolute or escaping paths, unsafe Web bindings,
  non-qB types, unstable downloader IDs, qB URLs containing credentials/query/
  fragment, duplicate or unstable SourceProfile routing IDs, missing routes,
  invalid Torrent hosts/Cookies/download policies, malformed metadata/AI/data
  endpoints and retry limits, and invalid six-field Cron expressions.
- Directory creation is derived only from the validated `data_path`,
  `download_path` and `save_path`; TestSpace and production data are never
  mixed implicitly.

All validation errors use field-oriented messages and never interpolate
passwords, Cookies, API keys, passkeys or URL user information.

## Automated evidence

- Focused defaults/layout/validator tests passed 27/27.
- The complete solution passed 1353/1353 with zero failures and zero skips.
- Changed-file `dotnet format --verify-no-changes`, `git diff --check` and the
  repository secret scan passed.
- A fresh win-x64 Release publish completed `Generating native code` with
  `PublishAot=true`. The published executable then passed
  `eng/smoke-native.ps1` in first-start mode under PowerShell 7, including
  canonical YAML creation, validated directory initialization, SQLite startup,
  HTTP/static resources and clean process shutdown.
