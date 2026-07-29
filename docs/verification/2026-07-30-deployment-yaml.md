# Deployment YAML / NativeAOT verification — 2026-07-30

## Scope

This increment adds the actual deployment YAML loader used by the application,
first-start generation, canonical named downloader/source binding, legacy
`1.1.0`–`1.7.1` read compatibility, and command-line/environment precedence.
It does not claim that legacy YAML is rewritten or backed up.

YamlDotNet 18.1.0 is centrally pinned. Production code uses only the
representation-model `YamlStream`/`YamlNode` AST and explicitly flattens nodes
to `IConfiguration` keys; it does not invoke the reflective serializer. Input
is restricted to a strict UTF-8 mapping document of at most 1 MiB, 32 levels
and 4096 nodes. Duplicate/non-scalar keys and unsupported versions fail with
diagnostics that do not include configuration values.

## Automated verification

Targeted configuration tests:

```text
dotnet test tests/AnimeGoNet.App.Tests/AnimeGoNet.App.Tests.csproj \
  -c Release --no-restore --no-build \
  --filter FullyQualifiedName~DeploymentYamlConfigurationTests|FullyQualifiedName~DeploymentConfigurationLocksTests

22 passed, 0 failed, 0 skipped
```

The tests cover:

- first-start atomic annotated UTF-8 YAML with empty secrets;
- exact Docker `/data`, `/download/incomplete`, `/download/anime` contract;
- complete canonical downloader/source/metadata/Torrent/schedule/data-update binding;
- command-line path/language precedence over YAML;
- all recognized versions from 1.1.0 through 1.7.1 mapping legacy qB/Mikan keys;
- unsupported version and duplicate-key redacted failure;
- command-line downloader password remaining authoritative over the Web private
  downloader override;
- `ANIMEGO_THEMOVIEDB_KEY` participating in WebUI environment locking.

Complete Release suite:

```text
AnimeGo.Plugin.Abstractions.Tests  11 passed
AnimeGoNet.Core.Tests             264 passed
AnimeGoNet.Data.Tests             152 passed
AnimeGoNet.App.Tests              500 passed
total                             927 passed, 0 failed, 0 skipped
```

## NativeAOT evidence

After a `win-x64` runtime restore, Release publish completed `Generating native
code` and produced:

```text
AnimeGoNet.App.exe  32,662,528 bytes
```

`eng/smoke-native.ps1` then started that exact native executable with an
isolated temporary data/download/save tree and schema version 30. It passed the
existing `/ping`, API, SQLite, static WebUI, WebSocket, qB capability and secure
ingest checks, plus the new assertion that first start created
`data_path/animego.yaml` containing version 1.7.1, all three effective paths
and `use_metadata_match: false`. The process and smoke data directory were
removed by the script.

## Dependency audit note

The complete tests passed before the separate
`dotnet list package --vulnerable --include-transitive` step. On this host the
NuGet vulnerability snapshot URL timed out after 60 seconds and produced
`NU1900`; no vulnerability result was returned, so this is not recorded as a
successful audit. NativeAOT restore/publish was repeated with
`NuGetAudit=false` solely to avoid treating that unavailable network service as
a compiler error. The official NuGet package page identifies YamlDotNet 18.1.0
as a .NET 10-compatible package with no package dependencies:
<https://www.nuget.org/packages/YamlDotNet/18.1.0>.

## Secret and local-environment boundary

No TestSpace binary, profile, Cookie, credential, API key, passkey, generated
YAML, SQLite database or download payload was read into or added to this
increment. Unit tests use disposable roots and placeholder secrets. The
already-running preview and isolated qBittorrent process were not restarted or
modified by these tests.
