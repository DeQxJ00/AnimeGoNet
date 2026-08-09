# AI / MCP WebUI configuration closure — 2026-08-09

## Scope

- The application configuration editor now exposes the OpenAI-compatible Base URL, model, AI API Key, TMDB MCP URL and Bangumi MCP URL.
- The API Key uses the same private tri-state contract as TMDB credentials: values are never returned, an empty password field preserves the existing value, and an explicit checkbox clears it.
- All five fields participate in deployment environment/command-line locks, preview diffs, restart detection and `application.private.json` revision storage.
- The provider remains the compiled `openai_compatible` implementation; the UI does not present a provider selector that has no alternative implementation.

## Verification

- TypeScript strict build and 17/17 static WebUI tests pass.
- Configuration API tests cover save, preserve, explicit clear, response redaction and the five rendered controls.
- Deployment lock tests cover independent AI provider/MCP locks.
- Targeted App configuration/WebUI regression: 159/159 passed; solution build: 0 warnings / 0 errors.
- Full App regression: 859/859 passed.
- `win-x64` NativeAOT publish completed with 0 AOT/trim warnings.

No real AI key or MCP response is used by these default tests.
