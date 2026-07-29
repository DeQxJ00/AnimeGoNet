# Mikan RSS Bangumi Subject discovery verification

## Scope

This increment ports the upstream Mikan work-page lookup into the .NET RSS winner path. The upstream behavior reads `/Home/Bangumi/{mikanid}` and extracts the Bangumi Subject link from `p.bangumi-info`. The .NET implementation keeps that business relationship while adding strict host, payload, ambiguity, audit, and retry boundaries.

## Implemented behavior

- The resolver derives the Mikan origin from an RSS item's Episode URL and fetches the canonical work page through the existing profile-bound SSRF-safe HTTP pipeline.
- The AOT-safe parser accepts only a unique positive `/subject/{id}` link on `bgm.tv`, `www.bgm.tv`, `bangumi.tv`, or `www.bangumi.tv` inside a paragraph whose class list contains `bangumi-info`.
- Schema v26 stores `bangumi_subject_id`, discovery state, and a stable failure code on `mikan_rss_batches`.
- A resolved batch is immutable and reused on repeated processing. Failed/not-found discovery may be retried by the next explicit RSS processing call.
- A winner is not staged until discovery succeeds. The resolved ID is passed through the unified ingest path and persisted in `ingest_tasks.bangumi_subject_id`.
- The modern and legacy RSS responses and the static WebUI expose the resolved `bgmid`, discovery state, and safe failure code without returning secret URLs.

## Verification

- Core parser fixtures cover valid official hosts, class lists, HTML entities, duplicate identical links, links outside the target paragraph, spoof hosts, invalid paths/IDs, conflicting IDs, invalid UTF-8, empty input, and the 2 MiB limit.
- App fake-transport tests cover canonical URL construction, missing inputs, missing links, transport failure classification, failure-without-staging, explicit retry, and resolved batch reuse.
- Data tests cover schema v25→v26 preservation, discovery state transitions, resolved identity downgrade protection, and `mikanid` participation in batch fingerprints.
- API tests cover modern and AnimeGoHelper-compatible response fields and passkey non-disclosure.
- No live Mikan, Bangumi, Torrent, or qBittorrent request is used by automated tests.

Final local gates: TypeScript strict check/build passed; all 747 .NET tests passed
(Plugin 11, Core 240, Data 121, App 375); `win-x64` NativeAOT publish completed
with no warnings, and the isolated native smoke initialized schema v26 and reported
`native_aot=true`.
