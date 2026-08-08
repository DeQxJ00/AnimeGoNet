# RSS-to-file candidate audit verification

## Scope

- Mikan RSS already persisted each batch, candidate, rule revision, Legacy filter decision, evaluated priority groups and the winner's `ingest_task_id`. Metadata task detail now follows that durable relation instead of inferring history from the current rule configuration.
- One unified ingest task may be linked by multiple RSS requests. `rss_evidence` returns every linked batch in stable creation order, then the existing `files` projection shows the actual Torrent-relative file name, source Episode and locally parsed file Episode candidate.
- The safe projection contains an opaque batch ID plus non-secret entry ordinal, SourceProfile, revisions and switches, Mikan/source Episode identity, stable decisions/groups, Legacy state and effect state. It deliberately excludes the candidate ID (it is derived from a source URL), stored Mikan URL, Torrent URL fingerprint, batch fingerprint, request URL, passkey and filesystem paths.
- Manual Mikan/U2/TTG submissions without an RSS batch return an empty `rss_evidence` array; their existing task/file audit remains unchanged.

## Automated evidence

- `MikanRssBatchStoreTests.TaskEvidenceLinksPersistedRssDecisionWithoutReturningSourceUrls` covers the persisted winner-to-task relation, revision, source Episode, decision, evaluated group, stable ordering inputs and missing-task behavior.
- `MetadataTaskDetailApiTests.ShowsSourceToVerifiedTmdbFileMappingAndAiTrustBasis` covers the API projection from a real task relation through RSS evidence and per-file candidates, including explicit negative assertions for private Mikan URL text, URL-derived candidate ID and both stored URL/batch fingerprints.
- `StaticWebUiTests` covers the compiled audit section and styles. The Node WebUI suite covers deterministic TypeScript compilation and the existing DOM/accessibility contracts.

## Release gate

- `npm run web:test`: 13/13 passed.
- Targeted Data tests: 8/8 passed.
- Targeted App/API/WebUI tests: 118/118 passed.
- Complete .NET suite: 1393/1393 passed (Plugin Abstractions 13, Plugin SDK 16, Core 339, Plugin Tool 23, Data 197, App 805).
- Solution build completed with zero warnings and zero errors.
- `win-x64` NativeAOT restore/publish completed native code generation with no trim/AOT warnings.
- The exact published executable passed isolated first-start and legacy-YAML-upgrade smoke modes at schema v38.
- The same executable passed the native AI metadata smoke fixture at schema v38.

Scoped formatting, `git diff --check` and exact local-secret scans are run before this module is committed. Tests use temporary SQLite databases and fake transports; no local qBittorrent process, TestSpace task, real Torrent URL, credential, passkey or media file was accessed.
