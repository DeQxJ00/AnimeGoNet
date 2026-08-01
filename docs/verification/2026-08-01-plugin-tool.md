# PluginTool verification

## Scope

`AnimeGo.PluginTool` is an AOT-compatible .NET tool and standalone executable with
`validate`, `run`, and `pack` commands. It shares the production manifest loader,
configuration schema validator, JSON Lines process session, exact operation names, and
six typed result validators instead of maintaining a permissive test-only protocol.

Package audit rejects links/reparse points, unsafe Unix write permissions, invalid paths,
and bounded-entry/file/size violations. It hashes every file and a canonical ordered tree.
Packing rechecks each audited file while writing a deterministic, uncompressed ZIP to a
GUID temporary file, hashes the completed temporary archive, and only then atomically
moves it to the requested output. `--force` applies only to that exact output path.

## Deterministic tests

`AnimeGo.PluginTool.Tests` covers:

- help, invalid/duplicate options, stable exit codes and stdout/stderr separation;
- valid and invalid manifest validation plus canonical package metadata;
- strict UTF-8, duplicate/unknown fixture fields, exact typed operation and config schema;
- fake initialize/execute/health/shutdown/dispose lifecycle, timeout propagation,
  owned temporary data cleanup and explicit data retention;
- original filter-item coverage validation, unhealthy plugins, and protocol-message
  redaction;
- byte-identical ZIP output, sorted/fixed entry metadata, output containment/existence,
  explicit force, mutation-after-audit detection, temporary cleanup, and content digest.

The targeted Release suite passed 23/23 with no skips. Release solution build passed with
zero warnings and zero errors. The complete suite passed 1270/1270 with no skips:
PluginTool 23, Plugin SDK 16, Plugin Abstractions 12, Core 324, Data 173, and App 722.

## Native process verification

`eng/verify-plugin-template.ps1` still generates and builds all six SDK templates. For the
selected native RID it publishes the filter plugin and PluginTool with NativeAOT, rewrites
only the temporary published manifest to the selected RID/entry point, and performs:

1. the direct initialize → execute → health → shutdown protocol smoke;
2. native PluginTool `validate` against the generated package;
3. native PluginTool `run` with an empty synthetic filter fixture and explicit isolated
   data directory;
4. native PluginTool `pack` plus archive existence and SHA-256 shape checks.

The repository five-RID NativeAOT workflow runs this on native `win-x64`, `win-arm64`,
`linux-x64`, `linux-arm64`, and `osx-arm64` runners. No qBittorrent, TMDB, Bangumi, Cookie,
passkey, credential, or user plugin data is accessed.

Local `win-x64` execution passed all four stages against the generated native filter and
the native PluginTool. The verifier removed its exact GUID-named temporary directory in
`finally`.

The project also packed as `AnimeGo.PluginTool.1.0.0.nupkg`, installed into an isolated
tool path, and its `animego-plugin --help` command ran successfully. TypeScript strict
checking and deterministic WebUI compilation remained clean.

The final main application `win-x64` NativeAOT publish completed without trim/AOT
warnings and passed both isolated first-start and legacy YAML upgrade smokes.
