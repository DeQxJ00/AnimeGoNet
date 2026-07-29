# Mikan work-rule management verification — 2026-07-29

## Implemented boundary

- The static TypeScript WebUI reads and manages the one highest-priority metadata rule shared by a `mikanid`.
- Create, update/disable, delete and explicit rematch all use the last loaded rule revision; a missing rule is an explicit revision 0 create.
- Optional `sample_source_episode` keeps the existing online TMDB Series → Season → target Episode validation before a rule is saved.
- The impact API returns authoritative counts and a bounded task projection classified as future, retryable failed, active protected, resolved protected, completed protected or other.
- Saving, disabling and deleting a rule do not rewrite existing tasks or files.
- Explicit rematch only resets `metadata_failed` tasks without a running lease. Resolved/organized tasks, Episode claims, completion records and media files remain unchanged.

## Automated verification

```text
dotnet test tests/AnimeGoNet.Data.Tests/AnimeGoNet.Data.Tests.csproj -c Release \
  --filter "FullyQualifiedName~MikanWorkImpactStoreTests" --no-restore
Passed: 2, Failed: 0

dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj -c Release \
  --filter "FullyQualifiedName~MikanWorkRuleApiTests|FullyQualifiedName~StaticWebUiTests" \
  --no-restore
Passed: 44, Failed: 0

dotnet test AnimeGoNet.slnx -c Release --no-restore
Plugin: 11 passed
Core: 215 passed
Data: 111 passed
App: 332 passed
Total: 669 passed, 0 failed
```

The data fixtures prove complete category totals with a truncated detail list, failed-only rematch, preservation of resolved/organized tasks and atomic revision conflict handling. The Kestrel API fixture proves impact serialization, one eligible retry, protected organized state and stale-revision conflict.

`dotnet format --verify-no-changes`, TypeScript 7 strict checking and `git diff --check` passed. A repeated TypeScript build produced the same `wwwroot/app.js` SHA-256:

```text
A5EB5B1FB0AE84096A372D4E8906CC436C6B1946AA215EE190595EABD057C5B2
```

## Browser verification

An isolated local JIT instance with empty temporary data/download/save directories was exercised using fake `mikanid=987654` and `bgmid=547888`; no Torrent, passkey, qBittorrent task or real TMDB request was involved.

- reading an absent rule displayed “revision 0” and enabled only safe creation;
- save created revision 1;
- disabling and saving produced revision 2;
- delete confirmation cleared the rule and restored the revision 0 state;
- an impact total of zero kept explicit rematch disabled;
- the responsive two-column rule/impact panel rendered without overlap.

The browser-control workflow exposed the confirmation-dialog behavior and proved the runtime state transitions rather than relying on static HTML markers alone.

## NativeAOT verification

```text
dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishAot=true \
  -o artifacts/mikan-work-rule-native-win-x64
Generating native code
```

The published executable started with isolated directories and background workers disabled. `/ping` returned HTTP 200 and the embedded WebUI returned HTTP 200 with the Mikan work-rule panel present.
