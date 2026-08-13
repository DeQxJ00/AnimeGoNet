# Bangumi archive usage audit verification — 2026-08-13

## Scope

- The primary navigation and workspace title use `Bangumi缓存`.
- Schema v45 persists AnimeGoNetData archive hits by data version.
- The data-update status API and WebUI expose cumulative Subject, complete Episode-set, and relation hit counts plus the last hit time.

## Counting contract

- A Subject hit is recorded only when the active local archive returns the Subject.
- An Episode hit is recorded only when the local archive contains a complete Episode set and avoids the online request.
- A relation hit includes an authoritative empty relation list from a schema v2 archive.
- Missing Subjects, schema v1 relations, and incomplete Episode sets that fall back online are not counted.
- Counters are atomically incremented in SQLite and retained when an old data version is pruned.

## Verification

- `npm run web:check` and `npm run web:build` passed.
- Focused Data tests: 37/37 passed.
- Focused App/API/WebUI tests: 17/17 passed.
- Web DOM tests: 19/19 passed; full Core 399/399 and Data 225/225 passed.
- win-x64 NativeAOT publish passed. The running local sandbox migrated from schema v44 to v45 without changing active `2026.08.11.2`; `/api/v1/data-update` returned the new zeroed usage object before the first post-upgrade hit.
- Browser verification at `#/bangumi-cache/versions` confirmed the `Bangumi缓存` navigation/workspace title and the five visible usage fields.

Historical hits before schema v45 cannot be reconstructed reliably and are intentionally not guessed; counting starts after this migration.
