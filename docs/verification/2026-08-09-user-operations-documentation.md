# User migration, plugin and operations documentation — 2026-08-09

## Delivered guides

- `USER_MIGRATION.md` provides a stop/backup/isolate/validate/switch workflow
  for fresh installs, legacy YAML, optional Bolt JSON export/import and media
  sidecars. It explicitly blocks silent Transmission migration, concurrent old
  and new writers, Python/JavaScript execution and binary-only database
  rollback.
- `PLUGIN_OPERATIONS.md` covers RID-specific package validation, synthetic
  fixture execution, install/enable, package and plugin-data isolation,
  revisioned private configuration, upgrade/rollback, reset and safe uninstall.
- `OPERATIONS.md` covers health/status/OpenAPI checks, graceful shutdown,
  routine status review, stopped full-data backup, SQLite `quick_check`,
  forward-only schema upgrades, coordinated restore and stable failure
  recovery.

The README links all three user-facing entry points. Existing detailed design
documents remain the source of field-level and protocol-level contracts.

## Automated evidence

`OperationsDocumentationContractTests` verifies:

- every guide is linked from README and every relative Markdown link resolves;
- migration retains backup, isolation, Transmission, cache importer, status and
  database rollback boundaries;
- plugin instructions retain all five RIDs, validate/run/pack, default-disable,
  private configuration, data isolation and explicit reset semantics;
- operations retain public liveness, protected status, OpenAPI, stopped SQLite
  validation, migration-history protection and `deleteFiles=false` cleanup;
- user-facing documents do not mention the local TestSpace, LAN endpoint or
  workspace path.

The focused Release suite passed 5/5. The complete solution then passed
1454/1454 with zero failures and zero skips. `git diff --check` and the
whitespace formatter restricted to the new C# contract test passed. Production
code is unchanged, so this module does not alter the previously verified
NativeAOT application surface.

Docker artifacts are documented as generated but explicitly unverified. No
Docker command was run or claimed successful.
