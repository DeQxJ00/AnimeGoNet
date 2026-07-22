# Mikan episode identity parser verification — 2026-07-22

## Upstream baseline

The `develop` implementation locates the Episode page anchor with class `mikan-rss` and reads `bangumiId` plus `subgroupid` from its `href`. MikanTool uses `sub_group_id`; `/Home/PublishGroup/{id}` is a different identifier and must not be substituted.

## Implemented boundary

- A dependency-free, NativeAOT-compatible HTML anchor/attribute scanner accepts ordinary attribute reordering, single or double quotes, additional class tokens, and encoded `&amp;` query separators.
- `bangumiId` must be a positive integer. A missing or invalid `subgroupid` becomes zero, matching the upstream parser.
- Empty, oversized (over 2 MiB), invalid UTF-8, missing-link, missing-href, and invalid-ID inputs have stable non-secret failure codes.
- No DOM package, reflection, dynamic code, network access, or Python runtime is used by the parser.

## Verification

```powershell
dotnet test tests\AnimeGoNet.Core.Tests\AnimeGoNet.Core.Tests.csproj -c Release --no-restore
dotnet test AnimeGoNet.slnx -c Release --no-restore --artifacts-path artifacts\mikan-identity-validation
dotnet publish src\AnimeGoNet.App\AnimeGoNet.App.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true --no-restore -o artifacts\mikan-identity-win-x64
```

Observed totals: Core 166, Data 60, App 143, total 369/369. The win-x64 NativeAOT publish completed without trimming or AOT warnings. Safe profile-bound Episode page fetching, per-batch caching, and RSS filter audit are deliberately left for the next module.
