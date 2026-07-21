# Persistent media organization store verification

Date: 2026-07-22

## Scope

- SQLite schema v10 adds `pending/organizing/cleanup/completed` media-organization state, exclusive lease, expiry recovery, attempt/retry fields, and a safe failure code to each download job.
- Move tasks start pending at confirmed dispatch; unsupported file strategies remain `not_required` until their own implementation lands.
- Each task file has at most one immutable `file_operations` plan. A retry must reproduce exactly the persisted source/target paths.
- Completion records and active episode claims are finalized in the same transaction, only after every wanted operation is completed.
- qB task cleanup is a separate claimed stage. A crash or failure cannot roll file moves back or silently mark cleanup complete.

## Automated evidence

- `MediaOrganizationStoreTests.MovesAndCleanupAreSeparateCrashRecoverableStages`
- `MediaOrganizationStoreTests.ConcurrentWorkersClaimMoveStageOnceAndRetryHonorsTime`
- `MediaOrganizationStoreTests.CannotWriteCompletionBeforeEveryFileOperationCompletes`
- Full solution: Core 93, Data 47, App 102; total 242 passed.
- Win-x64 NativeAOT generation and schema v10 binary smoke passed.

This data-layer module performs no filesystem or qB operation. Temporary SQLite databases only are used; no TestSpace content or credential is touched. The next module connects the safe mover, NFO writer, qB pause, and `deleteFiles=false` cleanup to this store.
