# External C# plugin process protocol (2026-08-01)

## Scope

This module adds the executable boundary after package discovery. A caller creates
one explicit session for one validated RID-specific package. Discovery itself
still never executes third-party code.

The host revalidates the package immediately before launch, starts the exact
manifest entry point without a shell, clears the inherited environment and passes
only plugin ID, API version and a dedicated data path. A single long-lived process
uses bounded UTF-8 JSON Lines for `initialize`, `execute`, `health` and `shutdown`.
Every response must have strict fields, API version 1 and the exact 32-character
request ID. Invalid JSON, unknown/duplicate fields, dirty stdout, version or ID
mismatch, timeout, caller cancellation, unhealthy state and premature EOF fault
the session and kill the process tree. A well-formed plugin business error is
returned without poisoning the session.

Defaults are 10 seconds for initialize, 120 seconds for execute, 5 seconds for
health/shutdown, 1 MiB for each request/response and a bounded stderr drain buffer.
Calls are serialized so one process cannot produce ambiguous response ordering.
Host-level restart backoff, automatic disable, stderr log forwarding and typed
source/feed/parser/filter/rename/schedule adapters remain separate modules.

## Tests

`ExternalPluginProcessSessionTests` uses a scripted duplex process for lifecycle,
correlation, remote errors, strict response faults, request/response bounds,
timeout, cancellation, premature exit, large stderr, unhealthy state, concurrent
calls, shutdown deadline and manifest mutation.

`ExternalPluginSystemProcessTests` launches a real .NET executable through the
production process factory. It performs initialize/execute/health/shutdown over
redirected stdio, exits normally, receives its dedicated data path and proves a
secret marker from the host environment is absent. It also asserts the child sees
exactly the three documented AnimeGoNet environment variables. The fixture is
self-contained for the current SDK RID, so clearing `PATH`/`DOTNET_ROOT` is tested
consistently on developer machines and CI runners rather than relying on a global
.NET installation.

Targeted Release verification: 21/21 tests passed with no skipped tests. The real
process cleanup case was also repeated five times to verify bounded Windows image
release after shutdown.

Complete Release verification passed Plugin Abstractions 11/11, Core 324/324,
Data 173/173 and App 641/641: 1149/1149 total, 0 skipped. Exact local-secret scan
and `git diff --check` passed.

The final `win-x64` restore plus `PublishAot=true` publish completed `Generating
native code` with no trim/AOT warning. The published executable passed both
first-start and legacy YAML upgrade smoke at schema v36, reported
`native_aot=true`, initialized SQLite/static WebUI and released its exact process
and listener after each run.
