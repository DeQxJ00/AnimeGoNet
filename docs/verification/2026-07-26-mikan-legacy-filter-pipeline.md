# Mikan legacy filter pipeline — 2026-07-26

## Runtime order

`POST /api/rss` now executes the preserved business order:

1. authenticate and safely fetch/parse the RSS URL;
2. apply exact `ep_links` selection when requested;
3. load the immutable SourceProfile and legacy filter revision;
4. run `Filiter0..4` for every original candidate;
5. run the newer lowercase blacklist/whitelist/ordered priority stage only for legacy-eligible candidates;
6. persist schema v16 audit and atomically stage winners through unified ingest.

`POST /api/download/manager` remains the upstream-compatible fast path and does not invoke the ordered RSS filter.

## Network and failure boundary

- `Filiter0` and `Filiter4` never fetch an Episode page.
- Any configured `Filiter1`, `Filiter2`, or `Filiter3` enables page identity lookup through the existing profile host allowlist, public-DNS check, pinned-address transport, redirect revalidation, HTTPS downgrade rejection, and response-size limit.
- The same absolute Episode URL is fetched at most once per batch, including shared failures.
- Invalid URLs, unsafe redirects, transport failures, invalid UTF-8, oversized HTML, missing identity links, and invalid IDs become stable per-candidate `FilterEvaluationFailed` decisions without exposing the URL or blocking unrelated candidates.
- Disabling `mikan_rss_filter_enabled` makes zero page requests, records `SkippedByConfiguration`, preserves all rules, and continues the remaining pipeline.

## Contract evidence

Kestrel/service tests verify:

- Filiter0/4 rejection and acceptance with zero page requests;
- Filiter1 identity (`mikanid=3951`, `groupid=370`) and one request for two candidates sharing a page URL;
- one malformed page candidate fails while another candidate stages normally;
- HTTPS redirect to loopback is rejected without a second request;
- disabled profile skips filtering without clearing the uploaded configuration;
- an AnimeGoHelper-compatible Base64 config upload immediately changes the following `/api/rss` result;
- the same reject-all config does not affect `/api/download/manager` fast download;
- response fields use explicit snake_case and readable decision/state strings.

## Verification commands

```powershell
dotnet test tests\AnimeGoNet.App.Tests\AnimeGoNet.App.Tests.csproj -c Release --no-restore
dotnet test AnimeGoNet.slnx -c Release --no-restore
dotnet publish src\AnimeGoNet.App\AnimeGoNet.App.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true --no-restore -o artifacts\mikan-legacy-filter-pipeline-win-x64
```

Observed totals: Core 168, Data 63, App 150, total 381/381. The win-x64 NativeAOT publish completed without trimming or AOT warnings.
