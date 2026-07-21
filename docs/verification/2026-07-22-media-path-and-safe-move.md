# Canonical media path and safe move verification

Date: 2026-07-22

## Scope

- Generate episode destinations exclusively as `<TMDB canonical series>/Sxx/Eyyy.ext` from verified positive Season/Episode values.
- Place confirmed-season `other` files below `Other/` while preserving a sanitized original basename.
- Normalize Unicode and deterministically replace cross-platform invalid characters, control characters, Windows device names, and trailing dots/spaces.
- Reject source/target paths outside their captured roots, equal source/target paths, and existing symbolic/reparse traversal.
- Prefer same-volume atomic move; fall back to a task-owned partial copy, durable flush, exact-size and SHA-256 verification, atomic target commit, then source deletion.
- Recover a prior committed target only when source/target content matches; preserve both sides on conflict.

## Automated evidence

- `MediaPathPlannerTests` covers TMDB paths, Other paths, sanitation, and rejection of unverified/non-organizable files.
- `SafeFileMoverTests` covers atomic move, verified-copy path, idempotent recovery, conflict preservation, and root escape rejection.
- Full solution: Core 93, Data 44, App 102; total 239 passed.
- Release build completed with 0 warnings/0 errors; win-x64 NativeAOT generation and schema v9 binary smoke passed.

All filesystem tests use disposable directories below the OS temporary root. No local qBittorrent process, TestSpace directory, downloaded content, credential, Cookie, or passkey is read or changed. The persistent download-complete worker is intentionally the next module; these primitives do not move any production file by themselves.
