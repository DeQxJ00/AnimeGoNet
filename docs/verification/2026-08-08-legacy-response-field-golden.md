# Legacy API/WS response-field golden — 2026-08-08

The machine-readable golden at
`tests/AnimeGoNet.App.Tests/Api/Fixtures/legacy-response-fields.golden.json`
covers every operation in the pinned upstream OpenAPI snapshot: 11 HTTP operations
and one WebSocket operation.

The Kestrel test calls every HTTP operation with a deterministic success fixture and
compares the exact root/data/item field sets. It additionally locks a legacy failure
envelope with `code=300` and null data. The WebSocket test parses the log-frame header
and invalid-control response and compares their exact fields. Dynamic values such as
timestamps, generated IDs and hashes are type/behavior tested elsewhere and are not
stored as brittle literal values in this field golden.

The tested HTTP response root remains `code/msg/data`; API authentication failures are
transport-level HTTP 401 and therefore remain covered by the separate Access-Key
tests rather than being misrepresented as a legacy business envelope.

The first golden run found that Minimal API inferred the C# parameter name
`accessKey`, while the pinned Go/OpenAPI contract sends `access_key`. The endpoint now
uses an explicit query-name binding. The published NativeAOT smoke calls that exact
query and verifies the lowercase SHA-256 value, so the fix is covered beyond JIT.

## Local result

- response-field golden Kestrel/WebSocket tests: `2/2` passed;
- win-x64 NativeAOT restore, publish and first-start smoke: passed;
- Release solution build: 0 warnings, 0 errors;
- serial .NET solution tests: `1441/1441` passed
  (`13 + 16 + 23 + 826 + 352 + 211`).
- Docker was not executed and remains explicitly unverified per project-owner
  instruction.
