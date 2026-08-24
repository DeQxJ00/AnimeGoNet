# Movie file disposition verification

## Outcome

- Verified movie main files and uniquely associated subtitles persist as
  `task_files.disposition=movie` with no `other_reason`.
- Movie files remain wanted by download preparation, are planned under
  `movie_save_path`, write `movie.nfo`, and create one Movie completion record.
- Movie files no longer contribute to TV Other counts, notifications, filters, or
  Other re-adaptation eligibility.

## Migration

Schema v59 rebuilds the constrained `task_files` table while foreign-key updates
are suspended. It converts only rows whose parent task is `media_type=movie` and
whose legacy state is `other + movie/movie_subtitle`. Indexes and Episode evidence
triggers are recreated, then `foreign_key_check` must be empty before commit.

## Automated acceptance

- `SchemaMigrationTests.MovieDispositionMigrationConvertsLegacyMovieRowsAndPreservesReferences`
  upgrades a v58 database, preserves a referencing file operation, verifies no
  stale `task_files_v58` reference remains, and checks all foreign keys.
- `MovieMetadataResolutionProcessorTests` verifies movie matching, distinct file
  classification, subtitle association, filesystem organization, NFO, completion,
  and the multiple-video rejection boundary.
- Delete-plan and download-management API suites cover the new classification.
