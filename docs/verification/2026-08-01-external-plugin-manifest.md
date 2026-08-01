# External C# plugin manifest boundary (2026-08-01)

## Scope

This module establishes package discovery and validation before AnimeGoNet is
allowed to start an external C# executable. It does not load DLLs, scan assemblies
or execute a plugin. The JSON Lines process lifecycle is the next separately
reviewable module.

`data/plugins` contains one direct child directory per RID-specific package. The
host validates:

- lowercase reverse-domain ID, strict SemVer, API version 1 and one of the six
  source/feed/parser/filter/rename/schedule types;
- one of win-x64, win-arm64, linux-x64, linux-arm64 or osx-arm64, exactly matching
  the current host;
- unique lowercase capabilities, strict/duplicate-free JSON, 64 KiB manifest and
  256 KiB config-schema bounds;
- relative entry point and schema paths that remain inside the package, exist and
  contain no symlink/reparse component;
- `.exe` on Windows; executable bit plus no group/world writable package path on
  Unix;
- global ID uniqueness. Every colliding package is rejected so directory order
  cannot choose an attacker-controlled winner. A broken package produces a stable
  diagnostic without hiding an independent valid package.

The loader is registered through DI against `DirectoryLayout.PluginsPath` and
produces one startup discovery snapshot. `/api/v1/status.external_plugins` exposes
only safe manifest metadata and stable package-directory diagnostics. Neither
config files nor executables are uploaded or copied by the Web API.

## Tests

`ExternalPluginManifestLoaderTests` has 26 passing cases covering the exact five
release RIDs/six categories, valid discovery,
DI wiring, strict SemVer with hyphenated identifiers, invalid IDs/versions/API/type,
unsupported and mismatched RID, traversal, unknown/duplicate JSON fields, duplicate
capabilities/schema fields, missing and oversized files, duplicate package IDs,
outside-root packages, symlink escape, Unix write permissions and cancellation.
On Windows hosts without symbolic-link privilege the link creation case verifies
the platform restriction and returns; Linux CI executes the real symlink rejection
path.

Release verification passed with 0 warnings/0 errors. Complete solution results:
Plugin Abstractions 11/11, Core 324/324, Data 173/173 and App 620/620
(1128/1128 total, 0 skipped). The final gate also includes an exact-secret scan
and NativeAOT publish/startup smoke.

Final `win-x64` `PublishAot=true` completed `Generating native code` with no
trim/AOT warning. The smoke script installs a non-executed RID-specific fixture
package, then proves the published process discovers exactly that package through
`/api/v1/status`, reports no errors, passes schema v36/SQLite/WebUI/legacy config
checks and shuts down cleanly. Both first-start and legacy YAML upgrade modes
passed; ports 6196/6197 and their exact processes were released.
