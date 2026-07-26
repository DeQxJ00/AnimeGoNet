# AI provider configuration verification

## Scope

- AI season and Episode switches remain independent and disabled by default.
- The OpenAI-compatible endpoint, model, API key, retry count and local tool endpoints
  are represented in the main application configuration.
- The default HTTP timeout remains 600 seconds.
- Runtime configuration exposes only whether an AI API key is configured; it never
  returns the key.

## Native configuration keys

The deployment configuration loader accepts:

- `ai_base_url`
- `ai_api_key`
- `ai_model`
- `ai_provider`
- `ai_use_season_match`
- `ai_use_episode_match`
- `ai_timeout_second`
- `ai_retry_count`
- `ai_use_bangumi_pubdate_first`
- `ai_tmdb_mcp_url`
- `ai_bangumi_mcp_url`
- `ai_anidb_mapping_url_template`

The AI feature switches are still controlled by the private application override
model and default to `false`. Missing `ai_base_url` or `ai_model` does not prevent
the application from starting: the matching processor must classify that attempt as
a configuration failure and continue the lower-priority deterministic chain. An API
key is optional so a local compatible provider can be used without inventing a
credential.

## Acceptance

- Core defaults and configuration validation tests.
- Minimal API redaction test with an actual in-memory AI secret.
- Full solution tests.
- `win-x64` NativeAOT publish.
