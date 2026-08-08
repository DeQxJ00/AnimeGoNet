# Pure-function parity verification

## Scope

- The fixed upstream helper surface is mapped in `docs/PURE_FUNCTION_PARITY.md` by observable business use. Helpers that only existed for Python, reflection, panic recovery or an unsafe filesystem shortcut are tied to their approved NativeAOT-safe replacement instead of being copied as dead compatibility APIs.
- A shared `StableHash` now owns UTF-8 lowercase SHA-256. Legacy `/sha256` and Access-Key validation, unified ingest URL fingerprinting, RSS candidate identity and RSS batch fingerprinting all call this implementation.
- Existing production components remain authoritative for dynamic tag/date variables, four-step TMDB title cleanup, UTF-8 byte similarity, date-only Season selection, publication timestamps, canonical media paths, safe file actions and NFO output.

## Automated evidence

- `StableHashTests` fixes empty, ASCII, Unicode and arbitrary byte SHA-256 vectors and null rejection.
- Existing Core golden suites cover upstream tag variables, title suffix fixtures and similarity; time/path tests cover the approved typed and fail-closed differences.
- Existing API, ingest and RSS batch tests exercise every migrated `StableHash` production call site and the legacy Access-Key contract.

## Release gate

- Targeted parity/call-site tests: Core 64/64, Data 8/8 and App 15/15 passed.
- `npm run web:test`: 13/13 passed and deterministic compilation produced no unrelated WebUI diff.
- Complete .NET suite: 1398/1398 passed (Plugin Abstractions 13, Plugin SDK 16, Core 344, Plugin Tool 23, Data 197, App 805).
- Solution build completed with zero warnings and zero errors.
- `win-x64` NativeAOT restore/publish completed native code generation with no trim/AOT warnings.
- The exact published executable passed isolated first-start and legacy-YAML-upgrade smoke modes at schema v38.
- The same executable passed the native AI metadata smoke fixture at schema v38.

Scoped formatting, `git diff --check` and exact local-secret scans are run before commit. No local qBittorrent process, TestSpace task, real Torrent URL, credential, passkey or media file was accessed.
