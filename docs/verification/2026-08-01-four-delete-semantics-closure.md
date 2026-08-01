# Four-delete semantics closure — 2026-08-01

## Checklist reconciliation

The porting checklist still described preview and execution as pending, while
the current source already contains the complete schema-v12 flow:

1. a read-only fingerprinted preview separates business completion records,
   downloader tasks, captured download-source files and captured media files;
2. confirmation freezes only explicitly selected targets in SQLite;
3. a leased worker executes downloader → source → media → business targets;
4. qBittorrent deletion is always `deleteFiles=false`;
5. file deletion accepts only an exact regular file below the captured root,
   rejects root/directory/outside/symbolic traversal and is idempotent when the
   file is absent;
6. failure stops later classes, persists a stable code and schedules retry;
7. deleting a completion cascades its aliases and releases only the completed
   claim with the same TMDB Series/Season/Episode identity.

Minimal API preview/confirm/status routes and the static WebUI deletion dialog
already expose the four independent choices and per-item result state. This
increment adds the previously missing explicit regression fixture: deleting
TMDB 100 S01E001 leaves the S01E002 completion and completed claim intact. The
same fixture proves a preceding qB failure leaves both episodes and both files
untouched.

## Verification

Focused Data/App deletion tests use disposable SQLite databases, a fake
download client and temporary files. No local qBittorrent, TestSpace file or
credential is accessed. The production source is unchanged from the immediately
preceding tree, whose full 1329/1329 Release suite and win-x64 NativeAOT
first-start/legacy-upgrade smokes passed.
