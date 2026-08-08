# Local qBittorrent legal download E2E — 2026-08-08

## Safety boundary

- The test attaches only to the explicitly checked portable executable under the
  ignored local `TestSpace`; the WebUI listener PID and portable profile lock must
  match before xUnit starts.
- Credentials remain process environment values. No password, Cookie, API key,
  passkey, profile, Torrent file, or downloaded payload is committed.
- The sandbox task list must be empty before each run.
- Every run uses a unique `animegonet-integration-<runid>` category,
  `animegonet-test-<runid>` tag, filename, series directory, and SQLite root.

## Legal fixture

The test creates a deterministic 128 KiB byte array in memory and constructs a
BitTorrent v1 metainfo file at runtime. SHA-1 is used only because the v1 piece
format mandates it, not for a security decision. The announce URL is the
non-listening `127.0.0.1:9`; the only data source is a bounded HTTP server owned by
the test on a random `127.0.0.1` port. It supports qB range requests and serves only
the generated payload. No public tracker or peer is contacted.

## Verified flow

1. Built-in Mikan source normalization and `UnifiedIngestProcessor` stage the
   generated metainfo in an isolated data root.
2. `StagedTorrentDispatcher` adds the exact hash paused to the real qB instance and
   persists immutable download/save roots.
3. The test injects a synthetic already-verified TMDB Series/Season/Episode boundary
   into its isolated SQLite database; no real metadata endpoint is called.
4. `DownloadPreparationProcessor` validates the real qB file manifest, sets priority
   1, and resumes the task.
5. qB fetches the generated payload from the loopback web seed. Acceptance requires
   a real file with the exact expected length and byte content, not only qB progress.
6. `DownloadSnapshotSynchronizer` persists `downloaded`.
7. `MediaOrganizationProcessor` pauses qB, moves the source into canonical
   `Series/S01/E001.mkv`, writes NFO and all three JSON sidecars, and commits exactly
   one completion.
8. A second organization pass calls qB with `deleteFiles=false` and persists
   `organized`.

An initial diagnostic run demonstrated why the file gate matters: qB briefly exposed
`progress=1`/Complete while `downloaded=0` and no payload existed. The final test
therefore requires real on-disk length/content before snapshot synchronization;
AnimeGoNet business completion was already protected by the later safe file operation.

## Result and cleanup

- qBittorrent executable/API: v5.2.3;
- explicit integration result: sandbox smoke + legal download E2E, 2/2 passed;
- the corrected test then passed three consecutive complete download/organize runs;
- the isolated local-integration project built in Release with 0 warnings/errors;
- the default Release solution remained isolated and passed 1437/1437 tests;
- each `finally` path used only the exact info-hash with `deleteFiles=false`, removed
  the exact category/tag and run-owned paths, and left the sandbox task list,
  download root, media root, and integration data root without run-owned artifacts;
- the portable qB process remains running for subsequent explicit local tests.
