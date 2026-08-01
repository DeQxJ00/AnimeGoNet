# Web API client verification

## Scope

The static TypeScript WebUI now has a shared native-ES-module JSON client. It centralizes
the legacy Access-Key header, typed request body serialization, typed response parsing,
abort propagation, stable structured errors, and same-origin path enforcement without a
browser runtime framework.

The status/external-plugin bootstrap and directory-database status/refresh flows use the
client. Existing feature requests remain compatible and can migrate incrementally.

## Deterministic tests

`npm run web:test` compiles the committed browser modules and runs five tests with the
Node built-in test runner. They cover:

- Access-Key and Accept headers on a same-origin GET;
- rejection of absolute, protocol-relative, and backslash-host paths before `fetch`;
- JSON mutation serialization, caller headers, and `AbortSignal` propagation;
- typed HTTP status/code/message/errors and safe fallback for malformed or HTML bodies;
- invalid success JSON and typed 204 responses.

`npm run web:check` passes with TypeScript 7 strict mode. CI runs both commands and rejects
any diff in the committed `app.js` or `api-client.js` output. Kestrel static-asset tests
also request the new module with the expected JavaScript media type.

The targeted Kestrel static WebUI suite passed 97/97. The complete Release suite passed
1275/1275 with no skips: PluginTool 23, Plugin SDK 16, Plugin Abstractions 12, Core 324,
Data 173, and App 727. Release solution build passed with zero warnings and zero errors.

Local `win-x64` NativeAOT publish completed without trim or AOT warnings. Its exact native
executable passed isolated first-start and legacy YAML upgrade smokes; both fetched the
new module, checked the import and same-origin failure marker, and released cleanly. The
shared smoke keeps this assertion in the native five-RID workflow.
