# Upstream historical YAML golden verification — 2026-08-01

## Boundary

The configuration migration baseline is the pinned upstream repository
`wetor/AnimeGo@c7475dfc55a374cd0dd08821bf17125dab1e3145`.
Its `configs/update.go` recognizes exactly these versions:

- 1.1.0, 1.2.0, 1.3.0, 1.4.0, 1.4.1;
- 1.5.0, 1.5.1, 1.5.2;
- 1.6.0, 1.6.1, 1.6.2;
- 1.7.0 and 1.7.1.

AnimeGoNet now uses the same exact allow-list. A numerically in-range version
that never existed upstream, such as 1.1.1 or 1.6.3, fails before backup or
replacement. This closes the former range-only acceptance gap.

## Historical fixture proof

`DeploymentYamlUpstreamParityTests` reads the 12 historical upstream files
`animego_110.yaml` through `animego_170.yaml` from the pinned checkout. Every
input is first checked against a committed SHA-256 expectation, then migrated
without a backup inside a disposable directory.

Each migrated result proves:

- canonical version 1.7.1 and removal of the legacy `setting`/`plugin` layout;
- data, download and media-library path preservation;
- qBittorrent endpoint and effective download-path preservation across the
  1.5.2 and 1.7.0 layout transitions;
- Mikan file strategy, category, dynamic tag template and seeding duration;
- Access Key, request timeout/retry values and database refresh Cron;
- a second-generation canonical document rather than continued dependence on
  Python or JavaScript plugin declarations.

The upstream YAML files are not copied into AnimeGoNet Git history. They are
loaded only from the already pinned public upstream checkout used by CI, so
their historical example credentials and API-key text are not duplicated in
this repository.

## Targeted result

The Release targeted run passed 16/16:

- 12 pinned historical golden migrations;
- 4 unsupported in-range version rejection cases.

The test leaves the original file byte-identical and creates no backup when an
unsupported version is rejected.

The complete Release suite also passed 1301/1301:

```text
AnimeGo.Plugin.Abstractions.Tests   12
AnimeGo.Plugin.Sdk.Tests            16
AnimeGo.PluginTool.Tests            23
AnimeGoNet.Core.Tests              324
AnimeGoNet.Data.Tests              176
AnimeGoNet.App.Tests               750
```

## NativeAOT

The final `win-x64` Release publish completed `Generating native code` with
`PublishAot=true` and no trim/AOT warning. The exact published executable then
passed both `eng/smoke-native.ps1` modes:

1. a clean first start with canonical YAML generation;
2. a 1.6.1 legacy YAML upgrade, exact-byte backup verification, canonical
   rewrite and normal application startup.

Both modes also exercised the current schema v36, generated OpenAPI, static
WebUI, WebSocket control, safe cache API and process cleanup gates.
