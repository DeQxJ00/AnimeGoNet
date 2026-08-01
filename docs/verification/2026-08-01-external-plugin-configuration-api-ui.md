# External plugin configuration API and WebUI (2026-08-01)

## Scope

The Access-Key protected management surface exposes safe package metadata,
global/per-entry revisions, non-secret default args, schema and redacted vars.
Write-only schema properties are represented only by configured JSON Pointer
paths; their schema default/example/const annotations are also removed. Omitting
them on PUT retains the stored value; a validated explicit clear path removes
it. Undeclared vars are omitted from responses. Revision, schema,
manifest and value failures occur before the private file changes.
Unsupported root types, required/property mismatches and invalid writeOnly
declarations are rejected per package during discovery instead of breaking the
whole management view later.

The static TypeScript panel now shows persisted enabled state, generates controls
for primitive schema properties, uses JSON editors for containers, retains nested
write-only values, supports explicit secret clearing and restores a package to
its unconfigured/default-disabled state. Plugin-controlled labels and errors are
inserted with `textContent`; no package path, process environment or secret value
is placed in the DOM.

## Tests

- TypeScript strict check and deterministic production build passed.
- Release build: 0 warnings and 0 errors.
- Targeted API/secret-projection/service/static-WebUI/schema-discovery result:
  115/115 passed,
  0 skipped.
- Full Release result: 1207/1207 passed, 0 skipped (plugin abstractions 11,
  core 324, data 173, app 699).
- `win-x64` NativeAOT publish completed without trim/AOT warnings. First-start
  and legacy-YAML-upgrade smoke both loaded a revision-1 write-only value,
  verified GET redaction, PUT retention, explicit clear, status enablement,
  reset, compiled WebUI assets and schema 36. The exact native executable had
  no live process after either run.
