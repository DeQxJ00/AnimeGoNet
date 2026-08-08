# WebUI Playwright E2E — 2026-08-08

## Implemented gate

- `@playwright/test` is pinned to `1.62.0`; the suite uses one Chromium worker and
  keeps trace, screenshot and HTML-report artifacts only under ignored `.artifacts`.
- The desktop test calls the real status API with plaintext
  `X-AnimeGo-Access-Key`, opens the WebUI with the legacy lowercase SHA-256 query
  value, and requires a NativeAOT runtime, live status, loaded async regions and a
  connected log WebSocket.
- The configuration dialog must expose the exact visual order
  `P4 → P3 → independent AI → P2 → P1` and the confirmed Backtrace/title/S01/Bangumi
  explanations.
- The 390×844 test requires exact viewport-width layout, keyboard-only skip-link
  entry, an in-bounds configuration dialog and no dialog horizontal overflow.
- Both cases fail on browser console errors or uncaught page errors.

## NativeAOT result

The current win-x64 NativeAOT publish was started against a unique temporary data,
download and save root with workers disabled and a fixture Access-Key. Playwright
Chromium passed `2/2` in 1.6 seconds after the test was corrected to respect the
upstream Access-Key boundary: plaintext is valid only in the direct header, while the
legacy query/header uses SHA-256. The temporary process stopped and its exact run root
was removed. A final run after adding the explicit plaintext-URL assertion also passed
`2/2` in 1.5 seconds, and its NativeAOT process was stopped.

The Codex in-app browser still rejected the random localhost port through its own URL
policy; no bypass was attempted. This does not affect the independent Playwright
Chromium result.

## Regression result

- clean `npm ci`: 25 packages installed, 0 vulnerabilities;
- TypeScript no-emit check: passed;
- deterministic WebUI unit/contract tests: `14/14` passed;
- Release solution build: 0 warnings, 0 errors;
- serial .NET solution tests: `1438/1438` passed
  (`13 + 16 + 23 + 823 + 352 + 211`).

## Docker status

`eng/smoke-webui-container.sh` and the Docker workflow now build the hardened fixture,
install Chromium according to Playwright's official CI guidance, run the same suite,
and upload failure artifacts. Per the project owner's instruction, Docker execution is
explicitly **not verified** here and remains for later owner-run validation.
