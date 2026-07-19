# TMDB authority client verification — 2026-07-19

## Upstream baseline and scope

- Baseline: `upstream/develop:internal/animego/anisource/themoviedb/{api.go,models.go,themoviedb.go,utils.go,themoviedb_test.go}`.
- The first ported boundary preserves the upstream TV discover query semantics: Chinese language, Asia/Shanghai timezone, animation genre, first-air-date sorting and text query.
- Strongly typed, source-generated JSON contracts cover TV Series, Series detail season summaries, ordinary Season and Episode authority endpoints without reflection-based serialization.
- `TmdbAuthority` accepts only positive Series/Season/Episode identities, calls all three official levels and rejects any returned identity mismatch. Canonical naming uses the TMDB `zh-CN` series name, falling back only to TMDB `original_name`.
- A successful authoritative 404 is represented as `SemanticNoMatch` with `tmdb_access_confirmed=true`. Network/timeout, 429/5xx, authentication, missing credential, invalid input and malformed/protocol responses never set that flag and therefore cannot enable the `tmdbid=0` fallback.
- API key query authentication remains compatible with the upstream transport; a TMDB Read Access Token is preferred as a Bearer header when configured. Stable exceptions never retain the raw transport exception or credential-bearing URI.

Title suffix removal, similarity candidate selection, air-date season selection, cache persistence, retry orchestration and metadata-resolution SQLite timelines remain separate later modules.

## Automated evidence

```text
dotnet test AnimeGoNet.slnx --no-restore
Core: 38 passed
Data: 17 passed
App: 52 passed

dotnet build AnimeGoNet.slnx -c Release --no-restore
0 warnings, 0 errors

dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj --configuration Release \
  --runtime win-x64 --self-contained true --no-restore \
  --output artifacts/tmdb-authority-aot-win-x64 -p:PublishAot=true
Generating native code

eng/smoke-native.ps1 -Executable artifacts/tmdb-authority-aot-win-x64/AnimeGoNet.App.exe
Native smoke passed (schema v5, secure ingest rejection, static WebUI)
```

Fake HTTP tests cover upstream query parameters, API key and Bearer modes, localized/original names, Series detail season summaries, Series→Season→Episode endpoint order, authoritative missing Episode, 400/401/429/503, timeout, malformed JSON, network sanitization, missing credentials and status capability redaction. No live TMDB credential or request is used by default tests or CI.
