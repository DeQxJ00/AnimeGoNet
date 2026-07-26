# AI HTTP and local MCP client verification

## Scope

- OpenAI-compatible Chat Completions transport implemented with `HttpClient`.
- Default 600 second cancellation boundary and configurable retry count.
- The only production prompt is copied from and loaded out of
  `docs/TMDB_AI_MATCH_PROMPT.md` (`tmdb-ai-match-v8`).
- TMDB Streamable HTTP MCP is mandatory and uses protocol `2025-03-26`.
- Bangumi MCP is initialized only when `bgmid` is present.
- The fixed AniDB mapping tool is registered only when `anidbid` is present.
- MCP tool names are namespaced, tool schemas are cached per endpoint, and the model
  is limited to eight tool rounds.
- Model, MCP and AniDB responses have explicit size limits and support cancellation.
- AI API keys are sent only as an Authorization header and are not included in
  prompts, tool arguments, exceptions or API projections.

## Acceptance

- Fake OpenAI-compatible server performs a two-round function-call exchange.
- Fake TMDB MCP covers initialize, initialized notification, tools/list and
  tools/call.
- Tests assert no Bangumi connection when `bgmid` is null.
- Tests cover missing configuration, 429 retry, authentication classification,
  legacy response fields and prompt placeholder completeness.
- The disabled publication-date gate replaces both date evidence fields with
  `null`.
- Full solution tests pass.
- `win-x64` NativeAOT publish succeeds with reflection-based JSON serialization
  disabled.

## Remaining integration

The client is registered as `IAiMetadataMatcher`, but the automatic season and
post-season Episode processors are intentionally connected in the next independent
commit. Their output must pass `AiMetadataResultValidator` and TMDB API validation
before any metadata state or filesystem target is changed.
