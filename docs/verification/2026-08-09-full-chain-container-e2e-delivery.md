# Full-chain container E2E delivery — 2026-08-09

## Scope and status

The full-chain Docker gate is generated and intentionally **unverified**. The
owner requested that Docker not be executed in this development pass and will
run it later. No Docker build, Compose start, container exec or remote workflow
was run while producing this increment.

The generated gate uses only deterministic project-owned test data. A small
.NET 10 NativeAOT fixture serves a valid BitTorrent v1 metainfo file, a 128 KiB
legal WebSeed payload, and fake Bangumi/TMDB endpoints. It records request
counters without accepting private trackers, cookies, passkeys or user
credentials. Its local JIT child-process test performs real HTTP requests,
parses the emitted Torrent, verifies its info hash and payload SHA-256, and
checks the metadata graph and credential counter.

## Generated container path

`eng/smoke-qbittorrent-compose.sh` and
`docker-compose.qbittorrent-integration.yml` now describe this sequence:

1. Build the amd64 NativeAOT fixture image and start it read-only/non-root on an
   isolated synthetic public-address Docker subnet.
2. Create a test-only Mikan SourceProfile bound to the `bt` qBittorrent instance
   with `move`, a fixed category/tag and a single allowed fixture hostname.
3. Submit the real fixture Torrent URL through `POST /api/v1/ingest` and verify
   the staged task identity without exposing the URL.
4. Allow the background workers to dispatch, resolve Bangumi and TMDB
   Series/Season/Episode, prepare the file, download it from the WebSeed, move it
   into the library, write `tvshow.nfo`, `anime.a_json`, `anime.s_json` and
   `E001.e_json`, commit completion state, and remove the qB task safely with
   `deleteFiles=false`.
5. Verify final download, metadata and library APIs, exact media size/SHA-256,
   fixture request counters, and absence of the task from both qB instances.
6. Optionally run Playwright against the same live container and require the
   downloads, metadata and library sections to show the canonical completed
   state with no browser console/page errors. CI enables this option.

The script owns only a `mktemp` integration root, uniquely tagged fixture image,
fixed test category/tag and project-scoped Compose name. Cleanup removes those
exact resources. It does not reference the local `TestSpace`, a private Torrent,
passkey, Cookie, local qB profile or user WebUI credential.

## Local validation performed

- `ContainerE2EFixtureProcessTests`: real local JIT child process and HTTP graph.
- Delivery contract tests: Dockerfile/Compose isolation, generated full-chain
  steps, qB cleanup, Playwright and workflow wiring.
- Bash syntax, YAML parsing and Node syntax checks.
- Focused Release delivery/fixture suite: 14/14 passed.
- Complete Release solution: 1467/1467 passed with zero skips. The App project
  contributed 844/844; the other five test projects contributed 623/623.
- Formatting, `git diff --check`, Bash syntax, YAML parsing and Node syntax all
  passed.

Docker/AOT container behavior remains unverified by explicit owner instruction.
