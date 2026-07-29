# Auto_Bangumi raw parser verification — 2026-07-29

## Compatibility boundary

- `AutoBangumiRawParser` is a NativeAOT-safe C# port of AnimeGo develop
  `assets/plugin/filter/Auto_Bangumi/raw_parser.py`.
- The compatibility type retains all upstream output fields: English/Chinese/Japanese title,
  season and raw season text, episode, subtitle text, release group, resolution and source.
- The original regular-expression behavior is not silently broadened. In particular,
  `E04`/`EP04` remain unrecognized, greedy later numeric markers remain observable, and
  upstream year/resolution-like outputs remain present in the compatibility result.
- No Python file, interpreter, dynamic DLL scan or reflection-based registration is used at runtime.

The golden fixture contains 19 inputs evaluated directly by the develop-branch Python source,
including the upstream sample, Chinese season numerals, full-width brackets, multilingual names,
version/END markers, missing/regex-significant groups, dates, multiple numeric markers,
E/EP, fractional and special tokens. The C# test compares every output field.

## Candidate safety boundary

`FileEpisodeCandidateResolver` is deliberately separate from the compatibility parser.
It uses the normalized Torrent internal basename and:

- runs only when the immutable SourceProfile adapter is exactly `mikan`;
- requires one distinct upstream-compatible positive integer marker;
- rejects year-like and resolution-like values;
- rejects special/Menu/Logo/PV/OVA/OAD/NCOP/NCED and ambiguous markers;
- catches malformed upstream-compatible dynamic regex input without failing task staging.

The data integration fixture proves that Mikan `[04]` and the upstream double-space `-  7`
produce local candidates, while Mikan year/ambiguous names and an otherwise identical U2
`[04]` do not. `source_episode` remains a descriptive local parse, but only the Mikan-only
`file_episode_candidate` can participate in trusted offset learning.

## Automated verification

```text
dotnet test tests/AnimeGoNet.Core.Tests/AnimeGoNet.Core.Tests.csproj -c Release \
  --filter "FullyQualifiedName~AutoBangumiRawParserTests" --no-restore
Passed: 13, Failed: 0

dotnet test tests/AnimeGoNet.Data.Tests/AnimeGoNet.Data.Tests.csproj -c Release \
  --filter "FullyQualifiedName~IngestTaskStoreTests" --no-restore
Passed: 6, Failed: 0

dotnet test AnimeGoNet.slnx -c Release --no-restore
Plugin: 11 passed
Core: 228 passed
Data: 112 passed
App: 332 passed
Total: 683 passed, 0 failed
```

`dotnet format --verify-no-changes` and `git diff --check` passed.

## NativeAOT verification

```text
dotnet publish src/AnimeGoNet.App/AnimeGoNet.App.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishAot=true \
  -o artifacts/raw-parser-native-win-x64
Generating native code
```

The published executable started with isolated directories and background workers disabled;
both `/ping` and `/api/v1/status` returned HTTP 200.
