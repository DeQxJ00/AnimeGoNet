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
| global proxy | replaced before production by one `outbound_proxy.url + hosts` domain-selective policy; old/per-client keys intentionally removed |
| Mikan Cookie | private, write-only SourceProfile credential |
| Mikan feed name/URL/Cron/enable | SourceProfile display name and persisted RSS schedule seed; old disabled feeds remain disabled |
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
- `UPSTREAM_CONFIGURATION_CONTRACTS.psv` compares every production Go file and
  exported symbol under `configs`; `UPSTREAM_CONFIGURATION_TESTS.psv` maps every
  exported `config_test.go` entry to a real replacement test. The pinned HEAD is
  checked before either comparison.
- Both legacy Mikan feed variable generations (`name/url/cron` and their
  `__name__/__url__/__cron__` predecessors) preserve display name, URL, enabled
  state and Cron. A custom feed Host is added to the SourceProfile allow-list;
  the seed is verified in SQLite and is not re-applied after a user clears it.
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

- App configuration tests: 159/159; Core configuration/policy tests: 24/24;
  SourceProfile store tests: 11/11.
- The complete solution passed 1420/1420 with zero failures and zero skips;
  WebUI passed 14/14; Release build completed with zero warnings and errors.
- A fresh win-x64 Release publish completed `Generating native code` with
  `PublishAot=true`. The published executable passed first-start, legacy YAML
  upgrade and AI metadata smokes at schema v38.
- Changed-file `dotnet format --verify-no-changes`, `git diff --check`, staged
  diff and repository secret scans are required immediately before commit.
