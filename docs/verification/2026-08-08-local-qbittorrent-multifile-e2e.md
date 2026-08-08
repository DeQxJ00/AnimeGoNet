# Local qBittorrent multi-file E2E — 2026-08-08

## Scope and isolation

The explicit `-DownloadFixture` mode now includes a second, dynamically generated
BitTorrent v1 fixture with four piece-aligned files. It uses the same checked portable
qBittorrent process and ignored TestSpace roots as the single-file legal E2E. The only
data source is an in-process HTTP server on a random `127.0.0.1` port; the tracker is
the non-listening `127.0.0.1:9`. It reads no external Torrent, tracker, credential,
passkey, metadata key, or existing qB task.

## File decisions

| Torrent file | Durable decision | Real qB priority | Expected result |
|---|---:|---:|---|
| `Episode 01.mkv` | verified Episode | 1 | downloaded and moved to `S01/E001.mkv` |
| `Episode 01.zh-Hans.forced.ass` | associated subtitle | 1 | downloaded and moved to `S01/E001.zh-Hans.forced.ass` |
| `Episode 02.mkv` | duplicate | 0 | progress remains zero; no media output |
| `poster.jpg` | ignored | 0 | progress remains zero; no media output |

The metadata boundary is injected only into the fixture SQLite database as an already
verified TMDB Series/Season/Episode identity. This test deliberately verifies the
real downloader and organizer continuation after that boundary; fake/loopback TMDB
tests separately verify the official identity calls and their failure behavior.

## Acceptance evidence

- qB returns exactly four manifest entries and persists priority `1,1,0,0`;
- only the wanted video and subtitle are fetched, with exact length and bytes;
- the qB snapshot advances the durable task to `downloaded`;
- Mikan `move` preserves the normalized subtitle language/track suffix;
- one video plus one subtitle creates exactly one Episode completion and one EP sidecar;
- downloader cleanup uses `deleteFiles=false` and the task reaches `organized`;
- `finally` removes only the exact info-hash, category, tag, Torrent root, media series,
  and isolated SQLite root created by that run.

## Result

- The explicit local integration command passed three consecutive runs, each with
  sandbox smoke + single-file legal E2E + multi-file E2E (`3/3`).
- The local-integration Release project built with 0 warnings and 0 errors.
- The default Release solution built with 0 warnings and 0 errors and passed
  `1437/1437` tests; default tests remain unable to start or inspect TestSpace.
- A final read-only sandbox run passed with an empty qB task list, and a bounded scan
  found no run-owned download, media, or integration-data artifacts.
