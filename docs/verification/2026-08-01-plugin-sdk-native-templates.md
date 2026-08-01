# Plugin SDK and NativeAOT template verification

## Scope

- `AnimeGo.Plugin.Sdk` exposes six typed entry points and requires caller-owned
  source-generated request/result metadata.
- Its bounded JSON Lines loop validates process environment identity, exact API/plugin
  identity, request IDs, duplicate/unknown wire fields, operation/category pairing,
  payload/config object boundaries, shutdown reason, and request/response sizes.
- A typed business error is returned on the wire without faulting the session. An
  unexpected handler exception terminates the process without exposing its message or
  stack through stdout/stderr.
- `AnimeGo.Plugin.Templates` generates source/feed/parser/filter/rename/schedule sample
  handlers, an AOT project, manifest/schema, and a five-RID GitHub Actions workflow.

## Deterministic verification

`AnimeGo.Plugin.Sdk.Tests` covers a complete filter lifecycle, raw merged args and vars,
business-error continuation, initialize mismatch, malformed/duplicate/oversized/
truncated input, invalid shutdown, unexpected exception redaction, oversized output,
cancellation, strict explicit-null/EOF handling, metadata and limit validation. The
targeted suite passed 16/16.

`eng/verify-plugin-template.ps1` packs Abstractions, SDK and Templates into an isolated
local feed, installs the template into a temporary custom hive, generates all six types,
checks file selection and manifest replacement, restores and builds every generated
project with zero warnings, then publishes a generated filter as NativeAOT and runs the
four-operation protocol against the real native executable. Local `win-x64` verification
passed.

Release solution build passed with zero warnings and zero errors. TypeScript strict
check/build was deterministic. The complete suite passed 1247/1247 with no skips:
Plugin SDK 16, Plugin Abstractions 12, Core 324, Data 173 and App 722.

The main `win-x64` NativeAOT publish completed native code generation without trim/AOT
warnings; the resulting application passed isolated first-start and legacy YAML upgrade
smokes. The generated filter plugin was then repacked from the final SDK sources,
published NativeAOT, and passed initialize/execute/health/shutdown as a real process.

The repository NativeAOT matrix invokes the same verifier on `win-x64`, `win-arm64`,
`linux-x64`, `linux-arm64` and `osx-arm64`, using native GitHub-hosted runner labels for
each architecture.

## Safety

All protocol input is synthetic and uses an empty filter fixture. Verification creates
only a GUID-named system temporary directory, validates that resolved path remains under
the system temp root, and removes that exact directory in `finally`. It does not access
qBittorrent, TMDB, Bangumi, user plugin packages, cookies, passkeys or private data.
