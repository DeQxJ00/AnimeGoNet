# Environment-controlled application configuration locks

Date: 2026-07-29

## Scope

- Detect the environment variable names already supported by the application configuration binder without reading or returning their values.
- Project canonical field locks from `GET /api/v1/config` through `editable.locked_fields`.
- Keep environment values authoritative after loading `application.private.json`.
- Make locked inputs, secret inputs, and secret-clear controls read-only in the WebUI.
- Reject direct API attempts to change a locked value with `configuration_field_locked`.
- Preserve a pre-existing private value below the environment layer; when no private value existed, persist the locked field as inherited so removing the environment variable does not create a stale private override.

The lock set covers TMDB base/proxy/language/timeout/API key/read token, Bangumi base/proxy/timeout, and the unified AI metadata switch/timeout. Legacy `ai_use_season_match` and `ai_use_episode_match` environment names lock `ai_use_metadata_match`.

## Automated evidence

Focused Release tests:

```powershell
dotnet test tests\AnimeGoNet.App.Tests\AnimeGoNet.App.Tests.csproj -c Release --filter "FullyQualifiedName~DeploymentConfigurationLocksTests|FullyQualifiedName~ConfigurationApiTests|FullyQualifiedName~ApplicationOverrideStoreTests" --no-restore
```

Result: 11 passed, 0 failed.

The API test proves that:

- lock names are case-insensitive and retain the actual deployed spelling for diagnostics;
- final effective values are returned but secret values are absent from the entire response;
- a different locked URL and an explicit locked secret write both fail;
- the rejected secret is absent from the error;
- an unlocked field can still be saved in the same full-snapshot request;
- applying the saved file after removing the simulated environment layer restores deployment values rather than the former environment values;
- a legacy lower private override remains hidden while the environment lock is active.

## Browser evidence

An isolated Debug process was started at `http://127.0.0.1:6184` with fake, non-production environment values and background workers disabled. The configuration dialog showed:

- `tmdb_base_url`, `tmdb_api_key`, `ai_use_metadata_match`, and `ai_http_timeout_seconds` in the environment-lock summary;
- TMDB URL disabled with the final environment value;
- TMDB API key and its clear checkbox disabled, with the key input empty;
- unified AI switch disabled and checked;
- AI timeout disabled with value `777`;
- unlocked Bangumi base URL still enabled.

The process was stopped only after resolving port `6184` to the expected worktree Debug executable. No real API key, passkey, downloader credential, or private Torrent was used.

## Release gates

Full solution Release tests were run serially to prevent Windows test-output file contention:

```powershell
dotnet test AnimeGoNet.slnx -c Release --no-restore -m:1
```

Result: 686 passed, 0 failed:

- Plugin abstractions: 11
- App: 335
- Core: 228
- Data: 112

The application was then published with NativeAOT:

```powershell
dotnet publish src\AnimeGoNet.App\AnimeGoNet.App.csproj -c Release -r win-x64 -p:PublishAot=true --self-contained true --no-restore -o artifacts\config-lock-native-win-x64
```

The native executable started on isolated port `6185` with fake environment overrides. `GET /api/v1/config` returned:

- `tmdb_base_url=https://native-environment.invalid/tmdb/`;
- `ai_http_timeout_seconds=654`;
- both fields in `editable.locked_fields`;
- no editable secret-value properties.

The listener path was resolved to the exact published NativeAOT executable before it was stopped.
