# WebUI / plugin authentication split verification

## Boundary

- `web.access_key` protects AnimeGoHelper compatibility routes and exact unified import
  `POST /api/v1/ingest`.
- `web.webui_access_key` independently protects the remaining WebUI management APIs and
  `/websocket`; it is empty by default.
- Plugin and WebUI credentials use different direct headers, hashed headers and query
  names and cannot authorize the other boundary.
- WebUI-only Mikan URL resolution and manual RSS ingestion remain on the WebUI boundary.

## Automated verification

- TypeScript strict check and 29/29 Node WebUI tests passed.
- 286/286 broad App/API/WebUI tests passed after the split.
- 241/241 deployment/default/static/authentication tests passed after the new YAML and
  WebUI fields were added.
- 21/21 focused middleware/OpenAPI tests passed, including cross-boundary rejection and
  exact `/api/v1/ingest` classification.
- 18/18 AnimeGoHelper legacy plugin/RSS/download-manager/golden API tests passed.
- 10/10 Docker/Playwright delivery contract tests passed; all three shell smoke scripts
  pass `bash -n`.
- Release solution and final App builds completed with zero warnings and zero errors.

## Local runtime smoke

The local Release process on port 6180 used plugin AccessKey `123456` and an empty WebUI
AccessKey. Observed results:

- `/ping`: 200;
- bare `/api/v1/status`: 200;
- bare WebUI Mikan resolve and manual RSS ingest reached endpoint validation (400), not
  authentication rejection;
- bare `/api/plugin/config` and bare exact `/api/v1/ingest`: 401;
- both plugin endpoints accepted the configured plugin credential.

Chromium loaded the bare settings page, displayed the independent WebUI authentication
card and current effective configuration, and reported zero console errors.
