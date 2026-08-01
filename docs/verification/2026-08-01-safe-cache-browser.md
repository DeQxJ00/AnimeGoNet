# Safe cache browser verification

## Scope

The modern cache browser exposes only opaque SHA-256 bucket/key IDs, entry counts, JSON
byte length, expiry, and update time. It never returns the raw bucket, key, JSON value,
SQLite path, or application business tables. `bolt_sub` is read-only. An exact `bolt`
delete requires an opaque token bound to the current key, value, TTL, and update time.

The implementation reuses schema 36 `cache_buckets/cache_entries`; it does not add a
migration, arbitrary SQL endpoint, bucket-wide delete, or cascade into business records,
downloaders, source files, or media files. The upstream `/api/bolt*` compatibility surface
remains unchanged.

## Deterministic tests

Data tests cover opaque projections with deliberately secret-looking bucket/key/value,
expiry cleanup, stable paging, changed-token conflict, exact deletion, missing entry, and
the read-only namespace. The targeted cache store suite passed 10/10.

Kestrel tests cover Access-Key middleware, invalid database/query/digest handling, absence
of raw identifiers and values in serialized responses, paging metadata, stale-token 409,
read-only 409, exact 200 delete, and repeated 404. Together with generated OpenAPI and
static WebUI assets, the targeted App suite passed 108/108.

TypeScript strict checking and five shared API-client tests pass. The committed static
page includes namespace switching, bucket/entry empty/loading/error states, pagination,
read-only presentation, explicit confirmation, conflict refresh, and DOM-safe text-only
rendering.

The complete Release suite passed 1285/1285 with no skips: PluginTool 23, Plugin SDK 16,
Plugin Abstractions 12, Core 324, Data 176, and App 734.
Release solution build passed with zero warnings and zero errors.

## NativeAOT

The shared native smoke requests the modern bucket endpoint, checks its namespace and
read-only projection, and verifies that the published HTML/JavaScript contain the cache
browser and modern API route. It therefore runs on every native RID in the existing
five-platform workflow.

Local `win-x64` NativeAOT publish completed without trim or AOT warnings. Its exact native
executable passed isolated first-start and legacy YAML upgrade smokes, including the safe
cache API and static-page assertions, and both processes released cleanly.
