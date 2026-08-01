# External C# plugin host manager (2026-08-01)

## Scope

This module roots the external process session in the ASP.NET Core host without
executing packages during discovery or startup. Each discovered plugin ID owns an
independent lazy session, async gate, failure counter and retry deadline. The first
execute/health call starts the process; successful calls reuse it.

Protocol, startup and health failures dispose only that plugin session and use a
2-second exponential retry window capped at 2 minutes. Five consecutive failures
auto-disable the plugin by default. A successful operation or a well-formed remote
business error proves the process is alive and clears the sequence. Host-side
validation errors leave a ready session untouched. Caller cancellation replaces
the faulted process immediately without increasing the plugin failure count.
Explicit reset clears backoff/auto-disable state.

Package content remains under `data/plugins`; plugin writes are limited by
contract to `data/plugin-data/<reverse-domain-id>`. Runtime status exposes only
plugin ID, stopped/starting/ready/backoff/auto_disabled, consecutive count, retry
time and the last stable failure code. It does not expose package/data paths,
stderr, config or environment values.

## Tests

`ExternalPluginHostManagerTests` covers DI/lazy startup, separate data root,
single-session reuse, exact host version/data path, per-plugin call serialization,
2/4-second exponential retry, isolation between plugins, threshold auto-disable,
explicit reset, successful recovery, initialize failure, unhealthy result,
business error, host validation failure, caller cancellation and missing IDs.

Targeted host-manager tests: 11/11 passed. The combined external plugin manifest,
protocol, real-process and host-manager set passed 58/58 with no skipped tests.

Complete Release verification passed Plugin Abstractions 11/11, Core 324/324,
Data 173/173 and App 652/652: 1160/1160 total, 0 skipped.

The final `win-x64` `PublishAot=true` run completed `Generating native code`
without trim/AOT warnings. Both first-start and legacy YAML upgrade smoke passed
schema v36 and asserted one discovered package plus one `stopped`/zero-failure
runtime, the separate `plugin-data` directory and `native_aot=true`. The exact
published process was absent after both runs.
