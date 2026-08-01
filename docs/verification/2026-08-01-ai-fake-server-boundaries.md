# AI fake-server boundary verification (2026-08-01)

## Scope

This module closes the transport and trust-boundary fixture item for the unified
task-level AI metadata flow. Tests use an in-memory fake OpenAI-compatible/MCP
HTTP handler and fake TMDB authority; they do not use a live provider, a user API
key, qBittorrent, Torrent URLs, passkeys, cookies, or the local integration sandbox.

## Proven behavior

- `ai_use_metadata_match=false` makes no matcher request and creates no
  `ai_metadata` audit attempt.
- Missing provider configuration fails before any HTTP request.
- Provider timeout is classified as `Network/ai_http_timeout`.
- HTTP 429 retries according to policy; exhaustion is classified as
  `RemoteService/ai_rate_limited` without exposing the response body.
- Authentication failure uses the stable safe classification and does not expose
  the configured API key.
- Malformed outer Chat Completions JSON and malformed model-result JSON are
  distinguished by stable protocol codes.
- A Chat Completions response must contain exactly one `choice`. Multiple choices
  are rejected as `ai_chat_response_ambiguous`; the first result is never selected
  implicitly.
- A model-provided nonexistent TMDB Series ID remains only a candidate and is
  rejected by the authoritative TMDB validation step.
- A changed output filename is rejected before any TMDB request.
- MCP sessions are initialized for each matcher operation, while `tools/list`
  schema discovery is cached once per source and endpoint.

## Commands

```powershell
dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj -c Release --filter "FullyQualifiedName~OpenAiCompatibleMetadataMatcherTests|FullyQualifiedName~AutomaticMetadataResolutionProcessorTests.DisabledUnifiedAiPerformsNoRequestOrAiAudit" --no-restore
```

Result: 15 passed, 0 failed, 0 skipped.

## Release gate

- TypeScript check and static WebUI build passed.
- Release solution build passed with zero warnings and zero errors.
- The complete suite passed 1085/1085: Plugin Abstractions 11, Core 324,
  Data 171, App 579.
- `win-x64` NativeAOT publish generated native code. The published executable
  started on isolated port 6192 with disposable paths and background workers
  disabled; `/api/v1/status` reported `native_aot=true`, RID `win-x64`, and schema
  v36. The exact published process was identified by executable path and stopped
  after the smoke.
