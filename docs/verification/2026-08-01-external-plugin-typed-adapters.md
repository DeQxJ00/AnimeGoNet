# External plugin typed adapters (2026-08-01)

## Scope

Every valid discovered external package is now explicitly constructed as exactly
one of the six public C# contracts and registered in the same deterministic
`PluginCatalog` as built-ins. There is no assembly scanning or dynamic DLL load.
The stable wire operations are:

- `source.normalize`
- `feed.fetch`
- `parser.parse`
- `filter.all`
- `rename.plan`
- `schedule.execute`

Inputs and results use a NativeAOT source-generated camelCase JSON context.
Persisted non-secret args are merged as defaults before the typed task payload;
schema-validated vars are sent only as protocol config. A missing or disabled
configuration returns the category's normal error result without starting the
process. A well-formed remote business error is redacted and leaves the healthy
session reusable.

Result projection happens while the host manager still owns the per-plugin gate.
Unknown or duplicate properties, malformed DTOs, invalid error codes, excessive
collections/text, mismatched source URL fingerprints, incomplete filter indexes,
unsafe rename paths and invalid schedule delays are protocol faults: the session
is disposed and the plugin enters normal backoff/automatic-disable accounting.

The source-profile API accepts a discovered external source adapter instead of a
hardcoded three-value list. The static WebUI adds discovered source packages to
the adapter selector; disabled and missing packages remain explicit and cannot be
silently substituted. External filters require an explicit ordered ID list; the
legacy null/default chain continues to execute built-ins only.

## Verification

- TypeScript strict check and deterministic WebUI build passed.
- Targeted abstraction plus adapter/manager/host-catalog/source-API/static-WebUI
  tests: 159/159 passed, 0 skipped (abstractions 12, app 147).
- Production DI discovery registered all six package types under the correct
  interface with `IsBuiltIn=false`.
- Fake process tests exercised all six operations, camelCase payloads, args/vars
  merging, disabled zero-start behavior, redacted business errors, strict unknown
  and duplicate fields, filter index coverage, exact URL fingerprint binding and
  path traversal rejection.
- Release solution build passed with 0 warnings and 0 errors. Full Release suite:
  1231/1231 passed, 0 skipped (plugin abstractions 12, core 324, data 173,
  app 722).
- `win-x64` NativeAOT publish completed without trim/AOT warnings. The isolated
  first-start and legacy-YAML-upgrade smoke both passed at schema 36 with a
  discovered external filter package/configuration. The exact native executable
  had no live process afterward and the generated artifact directory was removed.
