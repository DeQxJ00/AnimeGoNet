# RSS completion alias dedup verification

## Scope

- Normal media organization now writes the canonical TMDB completion record, its source alias, and the completed Episode claim in one SQLite transaction.
- The alias preserves normalized source id, source work id (`mikanid` for Mikan), source Episode, Torrent info hash, and timestamps without replacing the original task/file audit fields.
- A Mikan RSS winner checks the same `source + mikanid + ordinary source EP` under an SQLite IMMEDIATE transaction before Bangumi page or Torrent network access, then repeats the transactional check immediately before staging. A match returns `already_completed`, records the matched completion/alias on the RSS batch entry, and does not fetch or persist the Torrent.
- Deleting the business completion cascades to its aliases and saved RSS match evidence. Reprocessing the same batch can then claim and stage the winner again.
- Fractional, special, missing, and ambiguous source Episodes do not use this early alias shortcut; they continue into the authoritative TMDB validation pipeline.

## Automated evidence

- `CompletionRecordStoreTests` cover normalized alias insertion, idempotency, lookup, missing-completion rejection, and FK cascade deletion.
- `SchemaMigrationTests` cover v34 to v35 preservation of historical duplicate aliases, new RSS evidence columns, and targeted indexes.
- `MikanRssIngestProcessorTests` cover early skip without Bangumi/Torrent requests, persisted batch evidence, completion deletion, and successful re-entry of the same batch.
- `MediaOrganizationProcessorTests` cover completion and alias creation after real temporary-file organization and before safe qB cleanup.

## Release gate

- `npm run web:check` and `npm run web:build` passed.
- Release solution build passed with zero warnings and zero errors.
- The complete suite passed 1054/1054: Plugin Abstractions 11, Core 317, Data 168, App 558.
- `win-x64` NativeAOT publish completed native code generation.
- The published executable passed isolated first-start and legacy-YAML-upgrade smoke modes at schema v35.
