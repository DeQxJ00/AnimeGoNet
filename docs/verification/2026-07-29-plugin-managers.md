# Parser and ordered filter managers verification — 2026-07-29

## Upstream baseline

- `wetor/AnimeGo@develop cmd/animego/main.go` selects the first enabled parser plugin at startup.
- A parser execution failure does not select a later parser.
- `internal/animego/filter/filter.go` executes filter plugins in configured order and replaces the current item set with each plugin result.
- A filter error immediately returns and prevents later filters from running.

## AnimeGoNet implementation

- `TitleParserManager` uses the first parser in deterministic catalog order or one explicit stable ID. It never performs implicit fallback.
- `OrderedFeedFilterManager` accepts an explicit ordered ID list, built-in filters in catalog order when the list is omitted, or no filters for an explicitly empty list. External filters must be explicitly configured, so installing one cannot silently alter RSS behavior.
- Only accepted items flow to the next filter and their original indexes are retained.
- Each result must contain exactly one decision for every current input index; duplicate, missing, foreign, or malformed decisions stop the chain with `filter_result_invalid`.
- Plugin-returned errors stop the chain and preserve the input set at the failing stage for diagnostics.
- Unexpected plugin exceptions propagate to the caller and prevent every later filter from running; they are not mistaken for a successful skip.
- Unknown/empty/duplicate configured IDs are rejected before execution.
- The Mikan RSS production path now uses both managers (`mikan-title` and the explicit `mikan-tool` chain).

## Focused tests

- Parser first-by-order, explicit selection, unknown ID, and no fallback.
- Ordered accepted-item propagation across two filters.
- Plugin error short-circuit and later-plugin non-execution.
- Unexpected exception propagation and later-plugin non-execution.
- Duplicate output index rejection.
- Duplicate/unknown configured ID rejection.
- Explicit empty chain bypass.
- Existing Mikan RSS ingest, MikanTool compatibility, and legacy RSS API tests pass through the manager-backed production path.

Observed Release totals: Plugin Abstractions 11, Core 215, Data 100, App 283; total 609/609, with zero failures and zero skips.

Revalidated on 2026-08-01 after adding explicit unexpected-exception coverage: Plugin Abstractions 13/13 and the full solution 1339/1339 passed, with zero failures and zero skips.

`win-x64` NativeAOT publish completed without trim/AOT warnings. The full ignored TestSpace bundle started with the isolated data/download/save paths and background workers disabled; `GET /ping` returned `pong`, while `/api/v1/status` reported `native_aot=true` and `runtime_identifier=win-x64`. The exact smoke process was stopped afterward.
