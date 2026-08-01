# External plugin private configuration (2026-08-01)

## Scope

External process packages remain disabled unless their canonical ID has an
explicit enabled entry in `data/config/external-plugins.private.json`. The
source-generated JSON store has global and per-plugin revisions, bounded object
values, duplicate-property rejection, atomic replacement and Unix `0600` mode.
Missing configuration is an explicit disabled/empty default and does not create
a file.

Before a save, the service reloads the read-only package, verifies that its
manifest identity still matches discovery, and validates `vars` against the
declared schema. The supported deterministic schema subset covers primitive and
container types, required/additional properties, items, enum, ranges, lengths,
counts and non-backtracking string patterns. Failure occurs before persistence.

Configured execution fills missing task payload fields from `args`, preserves
task values on collision, passes `vars` as protocol config, and rejects disabled
plugins before a process is created. Saving or deleting configuration resets an
old session without starting the plugin.

## Tests

- Release build: 0 warnings and 0 errors.
- Targeted store/schema/service/host-manager result: 28/28 passed, 0 skipped.
- Full Release result: 1185/1185 passed, 0 skipped (plugin abstractions 11,
  core 324, data 173, app 677).
- `win-x64` NativeAOT publish completed without trim/AOT warnings. First-start
  and legacy-YAML-upgrade smoke both loaded a pre-existing revision-1 private
  plugin configuration and passed schema 36, status/reset, static WebUI and
  plugin directory isolation checks. The exact native executable had no live
  process after either run.
