# Pending TMDB API and WebUI verification

## Scope

- `GET /api/v1/metadata/pending-tmdb` returns only `anime_series` rows with
  `tmdb_series_id=0` and `needs_tmdb_completion=1`.
- `GET /api/v1/metadata/pending-tmdb/{bgmid}` returns related tasks and fallback
  claim/completion scopes; missing or canonical Series return 404.
- Summary fields include the Bangumi fallback name, confirmed Season numbers,
  task/file/fallback record counts, claim states, duplicate count, and latest
  safe failure classification.
- Scope keys are internal and are not returned by the API. Each scope instead
  exposes a user-facing dedup boundary and a cross-source duplicate-risk flag.
- The static framework-free WebUI renders a dedicated read-only section
  and lazy detail expansion. It explicitly states that fallback state is not a
  TMDB Episode grid or completion percentage.
- Manual TMDB mapping and fallback-to-canonical merge remain a separate module.

## Automated acceptance

- A seeded `tmdbid=0 + bgmid` fallback appears in list and detail responses.
- The list contains no `tmdb_series_id` or Episode progress field.
- A Mikan source scope is labeled `仅同一 mikanid`, warns about possible
  cross-source duplication, and does not expose its raw key.
- A missing pending Series returns 404.
- Static assets contain the dedicated API path, container, warning copy and
  responsive card styles.
- `node --check` validates the browser script.

## Visual acceptance

- An isolated local instance with background workers disabled rendered the new
  empty state between metadata pipeline and download status.
- DOM and screenshot inspection confirmed the heading, explanation, refresh
  control and empty card alignment.
- Browser console contained no warning or error entries.

## Result

- `npm run web:check`: passed with TypeScript 7 strict mode.
- `npm run web:build`: passed; committed JavaScript regenerated from TypeScript.
- `node --check src/AnimeGoNet.App/wwwroot/app.js`: passed.
- `dotnet test AnimeGoNet.slnx --no-restore`: passed
  - Core: 199
  - Data: 82
  - App: 245
  - Total: 526
- `dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64
  --self-contained true /p:PublishAot=true --no-restore`: passed
