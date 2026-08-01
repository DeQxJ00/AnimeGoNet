# External plugin stderr forwarding (2026-08-01)

## Scope

External process stderr remains a separate asynchronous pipe and is never parsed
as JSON Lines protocol output. The production host manager supplies the normal
application logger to each lazily-created session. Non-empty stderr lines are
logged at Warning with stable event 1901 and the canonical plugin ID as a
structured property.

The reader has a fixed per-line byte ceiling (4 KiB by default, configurable only
inside the bounded session options). It discards the remainder of an oversized
line, appends an explicit truncation marker, replaces invalid UTF-8, normalizes
control characters and applies the same URL/credential redactor used by the file
and WebSocket logs. A fixed per-session window allows 20 lines per 10 seconds by
default. Excess lines are not retained; stable event 1902 reports only their
bounded counter when the window rolls over or the pipe closes. A failing logging
provider is isolated from plugin protocol and lifecycle processing.

No stderr text is added to runtime snapshots, status/configuration APIs or the
WebUI. This forwarding improves trusted-plugin diagnostics but does not turn an
external executable into a security sandbox; plugins must still avoid printing
unlabelled credentials.

## Verification

- Targeted stderr/session/manager/real-process tests: 43/43 passed, 0 skipped.
- The real child-process fixture emitted a labelled dummy password; the captured
  structured event included its plugin ID and redacted value, while the exact
  dummy secret was absent.
- Release solution build: 0 warnings and 0 errors.
- Full Release suite: 1216/1216 passed, 0 skipped (plugin abstractions 11,
  core 324, data 173, app 708).
- `win-x64` NativeAOT publish completed with no trim/AOT warning. Both isolated
  first-start and legacy-YAML-upgrade smoke passed at schema 36. The exact native
  executable had no live process afterward and its generated artifact directory
  was removed.
