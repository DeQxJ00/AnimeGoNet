# CI / NativeAOT / Docker verification — 2026-07-19

## Scope

- .NET 10 Windows/Linux/macOS build and test workflow.
- NativeAOT publish matrix for the five first-release RIDs.
- NativeAOT Docker builds for `linux/amd64` and `linux/arm64`.
- Exact Docker directory and shared-volume contract.
- Container access-key startup guard.

## Local evidence

```text
dotnet build AnimeGoNet.slnx --configuration Release --no-restore
0 warnings, 0 errors

dotnet test AnimeGoNet.slnx --configuration Release --no-build
AnimeGoNet.Core.Tests: 9 passed
AnimeGoNet.Data.Tests: 9 passed
AnimeGoNet.App.Tests: 7 passed
Total: 25 passed, 0 failed

dotnet publish ... --runtime win-x64 -p:PublishAot=true
eng/smoke-native.ps1 .../AnimeGoNet.App.exe
Native smoke passed

bash -n eng/smoke-container.sh
YAML parser: all three new workflows and Compose passed
git diff --check: passed
```

The NativeAOT smoke verifies legacy ping, runtime status, schema version 1, `native_aot=true`, SQLite initialization, and the static WebUI. It also asserts that its process is reclaimed. Container smoke separately sends the mandatory access key.

## Deferred runner evidence

Docker is not installed on the local host. The image build, container smoke, four non-win-x64 NativeAOT publishes, and all non-Windows runtime smokes remain explicitly pending until the new GitHub Actions workflows run. No local success is claimed for those targets.

## NativeAOT boundary

The application project enables AOT only when publishing for an explicit RID. Normal solution build/test stays portable; RID-specific restore and publish happen independently per runner. JSON uses source generation and SQLite access remains explicit SQL.
