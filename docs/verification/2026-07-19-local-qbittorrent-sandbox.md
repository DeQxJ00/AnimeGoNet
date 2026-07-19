# Local qBittorrent sandbox verification — 2026-07-19

## Environment observed

- Executable: the qBittorrent binary under the external `TestSpace` sandbox.
- Product/API target: qBittorrent 5.2.3.
- Runtime: one process from that exact executable owns WebUI port 8080.
- Startup/profile: the executable is launched directly and uses its sibling portable `profile`; the expected profile lock exists.
- Paths: `download_temp`, `jellyfin_data`, and the separately created `animegonet_data` all remain outside the repository.

No binary, profile file, credential, API key, Cookie, passkey, Torrent, or downloaded content was copied into the worktree.

## Code evidence

```text
PowerShell parser: eng/qbittorrent-local-integration.ps1 OK

dotnet restore tests/AnimeGoNet.LocalIntegration.Tests/AnimeGoNet.LocalIntegration.Tests.csproj
dotnet build tests/AnimeGoNet.LocalIntegration.Tests/AnimeGoNet.LocalIntegration.Tests.csproj -c Release --no-restore
0 warnings, 0 errors
```

The local integration project is deliberately absent from `AnimeGoNet.slnx`, so default CI and ordinary `dotnet test AnimeGoNet.slnx` do not start or require a local qBittorrent instance.

## Runtime status

The first credential smoke exposed two environment/compatibility conditions without creating a Torrent:

- qBittorrent 5.2.3 rejects WebUI passwords shorter than six characters.
- A successful 5.2.3 login returns HTTP 204 with no body, while the upstream client accepted only HTTP 200 containing `Ok.`.

After configuring a valid local password, updating the adapter for the 204 response, and setting qBittorrent's default/temp path to the external `download_temp`, the explicit smoke passed:

```text
AnimeGoNet.LocalIntegration.Tests: 1 passed, 0 failed
qBittorrent local integration smoke passed: v5.2.3
startup=existing sandbox executable with sibling portable profile
download_path=E:\WorkSpaceAI\AnimeGoNet\TestSpace\download_temp
save_path=E:\WorkSpaceAI\AnimeGoNet\TestSpace\jellyfin_data
data_path=E:\WorkSpaceAI\AnimeGoNet\TestSpace\animegonet_data
```

The authenticated wire contract uses the official qBittorrent 5 username/password login with an exact-origin Referer and SID Cookie. Authentication, task-list read, executable/API-version equality, default-save-path equality, directory separation, and active-port ownership all passed. No Torrent, category, tag, or download file was created. A stale non-listening `--shutdown` helper process created during diagnosis was removed after its command line and executable path were verified; the active WebUI process remained unchanged.

## Upstream parity review

Baseline: `upstream/develop` at `c7475dfc55a374cd0dd08821bf17125dab1e3145`.

- Upstream `third_party/qbapi` creates a cookie jar and performs form username/password login. AnimeGoNet preserves that session behavior with one cookie-capable `HttpClientHandler` per named instance.
- Upstream `Manager.Download` passes `client.Config().DownloadPath` to qBittorrent as add `SavePath`; its notifier later treats the same directory as rename `SrcDir` and uses a separate `SavePath` as `DstDir`. The local mapping preserves this boundary: `download_temp` is the qBittorrent/organizer source and `jellyfin_data` is the organized destination.
- Upstream only accepts HTTP 200 and a response body containing `ok`, and sends no Referer/Origin header. That is not sufficient for the observed qBittorrent 5.2.3 behavior, which returns HTTP 204 for successful login.
- Upstream's real qBittorrent suites are unconditionally skipped (`TestMain` exits before tests, and individual tests call `t.Skip`). AnimeGoNet keeps mock/fake unit coverage and adds this explicit, opt-in real sandbox test outside the default solution.

qBittorrent 5.2 also supports API keys, but that mode is explicitly outside this first local smoke.
