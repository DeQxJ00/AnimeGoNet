# Fallback pre-download claim and dedup verification

## Scope

- The existing schema `fallback_claims` unique key is now acquired in the same
  transaction that completes the authoritative TMDB no-match fallback.
- Scope selection is shared by metadata and organization:
  - Mikan with a reliable source Episode uses `mikan_episode`.
  - Other sources with work identity and source Episode use
    `source_work_episode`.
  - Inputs without a reliable Episode use a `torrent_file` SHA-256 fingerprint
    over source item, info-hash, relative path and size.
- Source Episode whitespace, case and decimal formatting are normalized. Scope
  kind remains a separate namespace.
- A completed scope becomes `fallback_already_completed`; an active scope owned
  by another task becomes `fallback_claimed_by_another_task`. Both are persisted
  as `duplicate` before download preparation can resume qBittorrent.
- Files in the same task and scope share one claim. A different Episode remains
  independent.
- Successful organization inserts the fallback completion and changes the owned
  claim to `completed` in one transaction.
- Transient organization retries retain the claim. An explicit owner-file release
  enables a later task; no time-only takeover is implemented.

## Automated acceptance

- Scope resolver normalization and stable per-file fingerprint tests.
- Same `mikanid` plus normalized source Episode conflict across tasks.
- Existing fallback completion stops a later task before qB resume.
- Different source Episode remains wanted.
- Same-task files share one claim.
- Explicit release permits a later owner.
- Real temporary filesystem organization finalizes the active claim alongside
  the completion record.

## Result

- `dotnet test AnimeGoNet.slnx --no-restore`: passed
  - Core: 199
  - Data: 82
  - App: 239
  - Total: 520
- `dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64
  --self-contained true /p:PublishAot=true --no-restore`: passed
