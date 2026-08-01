# Legacy configuration API parity (2026-08-01)

## Upstream evidence

AnimeGo `develop` registers `GET /api/config` and `PUT /api/config` in
`internal/web/router.go`. `internal/web/api/config.go` defines four reads
(`all`, `default`, `comment`, `raw`) and two writes (`all`, `raw`), always using
the legacy HTTP 200 response envelope with business `code=200/300`. `raw` is a
Base64 representation of the YAML file; writes optionally preserve the old file
and require a restart.

The checked-in upstream OpenAPI contains 11 REST operations and one WebSocket
operation. The earlier “10 HTTP API + WebSocket” wording was an inventory error;
the method/path baseline, not that count, is authoritative.

## AnimeGoNet boundary

- `/api/config` is protected by the same direct or SHA-256 Access-Key middleware
  as every legacy API.
- GET supports `all/default/comment/raw`; PUT supports `all/raw`; query `key` and
  `backup` override body values as in the upstream multi-bind behavior.
- `all` converts between JSON and YAML with explicit `YamlNode` and
  `Utf8JsonWriter` traversal. It has depth, node and byte limits and does not use
  reflection serialization.
- `raw` performs strict Base64 and UTF-8 decoding. Both write forms validate YAML
  shape, duplicate keys, version range, legacy upgrade and the complete typed
  `AnimeGoOptions` candidate in an isolated same-directory file.
- Only after validation succeeds can a CreateNew original-byte backup be written
  and the target atomically replaced. A failed request leaves both the active file
  and backup set unchanged.
- The active process does not pretend the deployment file was hot-applied. The
  legacy success message explicitly requires restart.

Authenticated `all/raw` deliberately retain upstream behavior and can expose
deployment secrets. The static WebUI never calls these routes; it uses the
redacted, revision-safe `/api/v1/config` surface instead.

## Tests

- `LegacyConfigurationApiTests` covers authentication; four GET modes; unsupported
  keys; Base64 raw writes; query precedence; restart response; first write; exact
  original-byte backup; JSON all round-trip; typed validation before replacement;
  and failed-write preservation.
- `LegacyApiSurfaceTests` parses `docs/baseline/openapi-upstream.json` and proves
  every one of its 12 method/path operations exists in the running Kestrel route
  table.
- Targeted result: 9/9 passed (including four malformed-request theory cases and
  the delivery-script contract).

Release verification passed with 0 warnings/0 errors. Complete solution results:
Plugin Abstractions 11/11, Core 324/324, Data 173/173 and App 594/594
(1102/1102 total, 0 skipped). The final gate also includes exact-secret scans and
a NativeAOT publish/startup smoke because this module adds source-generated JSON
and an explicit YAML/JSON bridge.

Final `win-x64` `PublishAot=true` completed `Generating native code` with no
trim/AOT warning. The published executable passed first-start schema v36,
`native_aot=true`, legacy config `all/raw` GET, validated `raw` PUT, SQLite,
static WebUI and normal process cleanup through `eng/smoke-native.ps1`. The same
published executable also passed the `LegacyYamlUpgrade` smoke, including exact
1.6.1 original-byte backup, canonical 1.7.1 rewrite and clean shutdown.
