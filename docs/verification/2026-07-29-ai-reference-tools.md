# AI reference-ID tools

Date: 2026-07-29

## Scope

- Keep `anidbid` and `imdbid` as optional work-level references; neither value proves a TMDB Series, Season, or Episode.
- Register each reference lookup only when its normalized ID exists.
- Expose zero-argument model tools so the model cannot replace the task-bound ID or provide a URL.
- Keep every returned candidate behind the existing `AiMetadataResultValidator` and official TMDB Series/Season/Episode reads.

## AniDB safety boundary

- The mapping template is a compile-time constant. The legacy flat deployment key is accepted only when it is byte-for-byte equal to that constant; another host/path fails startup without echoing the supplied URL.
- Lookup substitutes only a positive integer already validated by the ingest/AI input layer.
- The production HTTP client disables proxy, cookies, and redirects.
- Its connection callback accepts only the fixed IDN host, resolves DNS itself, removes loopback/private/link-local/documentation/multicast addresses, and connects directly to one of the validated public addresses.
- The response remains limited to 20,000 bytes and only a positive `tmdbtv` value is returned as a reference.

## IMDb → TMDB boundary

TMDB documents `GET /3/find/{external_id}` with required `external_source`; IMDb can return both Movie and TV objects. The OpenAPI MCP server used by the local TMDB MCP documents `invoke-api-endpoint` as `endpoint + method + flat params`.

AnimeGoNet therefore registers `lookup_imdb_tmdb_tv` only for a normalized `tt...` ID. The tool:

1. takes no model arguments;
2. calls TMDB MCP `invoke-api-endpoint` with `/3/find/{external_id}`, `GET`, fixed task ID, and `external_source=imdb_id`;
3. parses bounded MCP text content;
4. returns sorted unique positive `tv_results[].id`;
5. reports only the number of rejected Movie results and never returns their IDs.

The generic namespaced TMDB invoke tool remains available for normal Series/Season/Episode work. If it targets `/3/find`, AnimeGoNet enforces the same task-bound IMDb ID and `external_source=imdb_id` before sending the call; a task without IMDb context or a model-supplied replacement ID is rejected locally.

Primary references:

- <https://developer.themoviedb.org/reference/find-by-id>
- <https://github.com/ivo-toby/mcp-openapi-server>

## Prompt and tests

The only production prompt is now `tmdb-ai-match-v9`. It requires the zero-argument IMDb tool and still requires full TMDB validation.

Focused Release tests cover:

- fixed AniDB destination despite hostile model arguments and a hostile configured template;
- exact deployed AniDB-template rejection with a redacted startup error;
- IMDb fixed-ID MCP arguments;
- rejection of generic TMDB Find calls that omit or replace the task-bound IMDb ID;
- sorted/deduplicated TV candidates and program-side Movie removal;
- absence of both local tools when their IDs are null;
- Prompt v9 content;
- existing OpenAI-compatible loop, MCP namespace/session, rate-limit retry, authentication redaction, input normalization, and TMDB result validation regressions.

Focused results:

- App AI/configuration tests: 17 passed;
- Core configuration/ingest/AI validation tests: 34 passed.

The four Release test projects were then run separately with `-m:1` so Windows output-file ownership and the 60-second command window remained explicit:

- Plugin abstractions: 11 passed;
- App: 338 passed;
- Core: 228 passed;
- Data: 112 passed;
- total: 689 passed, 0 failed.

NativeAOT:

```powershell
dotnet publish src\AnimeGoNet.App\AnimeGoNet.App.csproj -c Release -r win-x64 -p:PublishAot=true --self-contained true --no-restore -o artifacts\ai-reference-tools-native-win-x64
```

The native executable started on isolated port `6186` with background workers disabled and disposable paths. `/api/v1/status` reported `native_aot=true`, `runtime_identifier=win-x64`, and schema v23. The published bundle contained the only production Prompt with `tmdb-ai-match-v9`; `/api/v1/config` reported no configured AI secret. The listener path was resolved to the exact published executable before it and its launcher were stopped.
