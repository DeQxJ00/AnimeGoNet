# Dynamic download tag verification — 2026-08-01

Scope: upstream-compatible metadata tag templates applied to qBittorrent only after canonical metadata resolution.

## Behavior

- `SourceProfile.dynamic_tag_template` is validated without reflection and frozen in every ingest route snapshot. Default Mikan value is `{year}年{quarter}月新番`; an empty value disables the feature.
- Supported placeholders match the upstream helper: `{year}`, `{quarter}`, `{quarter_index}`, `{quarter_name}`, `{ep}`, `{week}`, and `{week_name}`. Multiple tags are comma-separated and use the same count/value safety policy as static qB tags.
- Dispatch never sends a raw template. The download preparation lease runs only for `metadata_resolved` tasks, keeps qB paused, chooses the first canonical ordinary Episode by Season/EP/path order, renders from its verified Season air date and EP, calls qB `/api/v2/torrents/addTags`, then applies file priorities and resumes wanted files.
- Missing required metadata, invalid rendered values and all-duplicate tasks persist an auditable `skipped` code without blocking the remaining download flow. A qB HTTP failure leaves `dynamic_tag_state=pending`, keeps the task stopped and schedules the normal preparation retry. Repeating `addTags` is idempotent.
- Schema v34 stores the template, actual applied tag array, state and failure code. Download list/detail API and the static TypeScript WebUI expose the result. Legacy `setting.tag` upgrades into the dedicated field and never into static `tags`.

## Verification

- Core tests cover exact upstream quarter/week semantics, multiple tags, required date/EP gates, invalid placeholders and disabled templates.
- SQLite migration tests cover v33→v34 Mikan-only backfill, unchanged revisions, safe job defaults, index creation and state constraints.
- SourceProfile/API/route snapshot tests cover CRUD, omission preservation, explicit clearing, invalid input rejection and immutable task projection.
- qB contract tests cover exact `addTags` endpoint/form data and pre-network hash/tag validation.
- Download preparation tests cover apply-before-resume, durable applied/skipped states and events, metadata-unavailable continuation, qB failure retry and all-duplicate behavior.
- WebUI TypeScript strict checking and deterministic compilation cover the editor, route preview and download status projection.
- NativeAOT first-start and legacy-upgrade smoke require schema v34 and verify that the legacy template reaches `dynamic_tag_template` while static tags remain empty.

Final gate: TypeScript strict check/build passed; Release solution build passed with zero warnings and zero errors; the complete suite passed 1049/1049 (Plugin Abstractions 11, Core 317, Data 165, App 556). `win-x64` NativeAOT published with native code generation and the resulting executable passed both isolated first-start and legacy-YAML-upgrade smoke modes at schema v34.
