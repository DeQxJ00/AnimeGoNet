# WebUI AccessKey dialog verification

Date: 2026-08-20

## Scope

- Open AnimeGoNet from a bare URL without manually appending `webui_access_key`.
- On a WebUI management API 401, show one credential dialog even when startup requests fail concurrently.
- Accept the plaintext `web.webui_access_key`, hash it in the browser, validate it, and retry the original requests.
- Keep WebUI and `inner_plugin_mikan.access_key` authentication separate.
- Do not display or generate a dedicated authenticated URL in the configuration card; use the dialog or top AccessKey entry. Existing URL query credentials remain backward compatible.
- Edit and round-trip `web.host` and `web.port` in the same card; reject malformed hosts and ports outside 0–65535 before replacing the deployment YAML.

## Deterministic checks

- `npm run web:check`
- `npm run web:test`
- `dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj -c Release --filter FullyQualifiedName~WebUi`
- Release application build

The WebUI unit suite covers URL/session/persistent precedence, non-persistent sessions,
the known SHA-256 value for `123456`, 401 retry behavior, and the rule that external URLs
never receive the key or open the dialog. Static delivery tests pin the dialog controls,
stylesheet, generated authentication module, and cache-busted assets.

## Runtime acceptance

Run the local application with a non-empty `web.webui_access_key`, open the bare root URL,
confirm the dialog appears, reject a wrong value, then enter the correct plaintext value.
The dialog must close only after `/api/v1/status` succeeds, the page must populate without a
manual reload, and a subsequent bare-page load must reuse the remembered hash without exposing
the plaintext in the URL or browser storage.
