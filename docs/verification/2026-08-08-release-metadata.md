# NativeAOT release metadata — 2026-08-08

## Delivery boundary

Every `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64` and `osx-arm64`
NativeAOT job now generates release metadata after the published-binary and
plugin-template smokes, but before `upload-artifact`. The generator receives
only the current RID publish directory, the exact application
`project.assets.json`, the RID and a tag-or-commit version.

The uploaded directory contains:

- `SHA256SUMS`, with lowercase SHA-256 for every regular release file except
  the checksum file itself, sorted by ordinal normalized relative path;
- `sbom.cdx.json`, a deterministic CycloneDX 1.5 application BOM containing
  every package from the exact restore graph, NuGet name/version, package
  SHA-512, purl, declared license and safe project URL;
- `THIRD-PARTY-LICENSES.txt`, sorted by NuGet identity with SPDX expressions,
  project URLs and package hashes. A package declaring a license file includes
  its bounded strict-UTF-8 text.

The script rejects missing packages/nuspecs, missing or unsupported license
declarations, invalid package URLs, malformed hashes, release symlinks/reparse
points and duplicate normalized paths. Atomic temporary files are kept inside
the publish directory and removed on failure. Absolute package-cache, source,
publish and user paths never enter the generated files.

## Test evidence

`ReleaseMetadataContractTests` executes the real PowerShell script twice against
a temporary nested publish fixture and the actual restored AnimeGoNet package
graph. It requires byte-identical outputs, ordinal component/path ordering,
CycloneDX identity, lower hexadecimal package SHA-512 values, both MIT and
Apache-2.0 declarations, no absolute path disclosure, and recomputes every
`SHA256SUMS` entry from disk. A separate workflow contract proves generation
occurs before artifact upload and applies to the RID matrix.

The targeted contract passed 2/2 and the complete solution passed 1366/1366
with zero failures and zero skips. The script was also run against a freshly
published win-x64 NativeAOT directory and emitted 10 NuGet components plus 23
verified release checksums. Final changed-file format, workflow YAML, diff and
secret scans passed. No network service, TestSpace file or user credential was
used.
