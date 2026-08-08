# AnimeGoHelper browser compatibility — 2026-08-08

## Pinned source

- Repository: `DeQxJ00/AnimeGoHelper`
- Commit: `78a9d0d832801d38efd6294841e9962e0bc791cf`
- File: `AnimeGoHelper.js`
- SHA-256: `d165c33c9692530da3d81032a49d1cdf42a815b7469e3438ff8457201a804576`

The script is checked out separately and executed without source changes. The browser
fixture supplies only the Tampermonkey APIs, the page globals expected by Mikan, a
deterministic Mikan page, and an isolated AnimeGoNet response dispatcher.

## Covered browser flows

1. The visible `单` control fetches the Episode page, reads `bangumiId/subgroupid`,
   then submits the discovered Torrent and Mikan URLs to `/api/download/manager`.
2. The visible `全` control submits the expected RSS URL to `/api/rss` with
   `is_select_ep=false`.
3. `上传过滤配置` sends `filter/mikan_tool.py` and UTF-8 Base64 `Filiter0`–`Filiter4`
   JSON to `/api/plugin/config`.
4. After clearing browser state, `获取过滤配置` decodes the legacy response envelope
   and restores the same JSON without loss.
5. Every protected request carries the lowercase SHA-256 Access-Key expected by the
   legacy API, and both cases require zero console/page errors.

The existing Kestrel contract tests continue to execute the same request/response
shapes against the real C# endpoints and verify RSS filtering, exact Episode
selection, and quick-download filter bypass. This browser fixture covers the missing
original-userscript side of that boundary; it does not contact Mikan or create a real
download task.

## Local result

- unmodified userscript Chromium suite: `2/2`, three consecutive runs passed;
- delivery contract plus real legacy RSS API tests: `4/4` passed;
- clean `npm ci`: 25 packages installed, 0 vulnerabilities;
- WebUI TypeScript check and deterministic browser-client tests: `14/14` passed;
- Release solution build: 0 warnings, 0 errors;
- serial .NET solution tests: `1439/1439` passed
  (`13 + 16 + 23 + 824 + 352 + 211`).
