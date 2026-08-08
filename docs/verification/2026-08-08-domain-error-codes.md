# Domain constants and stable error-code verification — 2026-08-08

## Upstream baseline

`wetor/AnimeGo@develop` keeps downloader states, notify states, metadata endpoints, cache buckets,
plugin operation names and directory-database sidecar names under `internal/constant`. Its
`internal/exceptions` package exposes concrete errors plus `Exist`, `NotFound` and `ParseFailed`
marker checks. Error identity otherwise depends on Go types and localized messages.

AnimeGoNet preserves the behavior through typed enums/records and explicit state machines instead
of copying mutable package globals or localized error strings. Source/download/file/metadata state,
plugin operations, cache buckets and directory sidecar names are compile-time C# constants or
closed enums at their owning boundary. Duplicate/not-found/parse outcomes are explicit result or
failure states and are not inferred by parsing an exception message.

## Stable error-code contract

`StableErrorCode` is the common NativeAOT-safe boundary for error identities stored in SQLite or
returned through application contracts:

- length is 1 through 128 characters;
- only ASCII letters, digits, `_` and `-` are accepted;
- whitespace, dots, Unicode and unbounded input are rejected before persistence or projection;
- validation is a direct character loop with no regular expressions, reflection or dynamic code.

Core RSS/Mikan/Cron/data-manifest/AI/TMDB exceptions, data-package and directory-database errors,
and application download/organization/delete/schedule/plugin/data-update errors all use the same
contract. Torrent magnet and metainfo format errors now also expose stable generic codes while
retaining their detailed diagnostic messages.

Download dispatch, preparation, organization, deletion and ingest stores use the same validator
before writing failure codes. Metadata-resolution error codes and fallback-denial codes are also
bounded by it; non-error identifiers keep their separate validation rules.

## Verification

- `StableErrorCodeTests`: accepted alphabet, exact 128-character boundary, null/empty/whitespace,
  punctuation, Unicode and overflow rejection, plus all Core exception families.
- Data integration test: a data-layer exception rejects an unsafe code.
- App integration test: delete, move, schedule, data-update and Bangumi exceptions reject unsafe
  codes.
- Existing store and workflow suites exercise valid production codes through SQLite and API paths.
