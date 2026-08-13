# Plaintext cache browser verification — 2026-08-13

## Scope

- The primary workspace label is `系统缓存`.
- `bolt` and read-only `bolt_sub` bucket names and entry keys are shown as plaintext.
- A single-entry detail endpoint returns the complete, untruncated JSON value on demand.
- The paged entry list does not carry values, so up to 100 entries of the 8 MiB maximum cannot amplify one list response.
- The existing opaque IDs and value-bound delete token remain internal targeting/concurrency controls; only `bolt` can be deleted.

## Boundary

The plaintext view remains protected by the existing same-origin Access-Key middleware. The WebUI assigns bucket, key and value through `textContent`, so cached text is never interpreted as markup or script. No SQLite path, SQL execution, whole-bucket deletion, business-table mutation, filesystem action or downloader action was added.

## Verification

- TypeScript strict check and deterministic build passed.
- Cache store tests: 10/10 passed, including plaintext names, on-demand full value, expiry, paging and exact deletion.
- Relevant API/static WebUI tests: 161/161 passed, including Access-Key enforcement, full-value response, stale delete token and `bolt_sub` read-only behavior.
- win-x64 NativeAOT publish passed and the running local sandbox remained on schema v46. Its real `bolt` namespace returned plaintext bucket names; browser verification showed 25 plaintext keys on the first page, opened one detail dialog, and confirmed the complete-value status without copying the value into the report.
