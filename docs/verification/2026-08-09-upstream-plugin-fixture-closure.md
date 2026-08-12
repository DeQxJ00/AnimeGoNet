# Upstream plugin/parser/filter fixture closure — 2026-08-09

> Status update (2026-08-11): the historical verification section below is
> retained as written. The linux-x64 external plugin fixture has since passed
> in the Ubuntu 24.04 x86_64 CT full-container chain; see
> `2026-08-11-ubuntu-ct-docker-validation.md`. The parser/filter parity evidence
> remains the focused fixture suite recorded in this report.

## Baseline and traceability

The only behavioral baseline is the separate repository
`wetor/AnimeGo@develop`, pinned at
`c7475dfc55a374cd0dd08821bf17125dab1e3145`. The new
`UPSTREAM_PLUGIN_FIXTURE_CONTRACTS.psv` maps all 59 tracked files in these
surfaces exactly once:

- `assets/plugin/**`;
- `test/testdata/feed/**`, `filter/**`, `parser/**` and `python/**`;
- the upstream feed/filter/parser `*_test.go` entry points.

Each row has one explicit disposition: 26 `ported`, 14 `replaced`, 13
`removed`, or 6 `documentation`. The contract test verifies the pinned Git
HEAD, exact tracked-file inventory, every mapped evidence path and the SHA-256
baseline for every asset/fixture file. A new tracked file, missing mapping,
duplicate mapping, changed byte or nonexistent evidence target fails the test.

`removed` does not mean silently ignored. It records the approved product
boundary: AnimeGoNet never embeds or executes Python. Equivalent built-in
behavior is compile-time C#, while extensibility uses the typed external C#
JSON-Lines protocol and its existing process-failure suite.

## Direct fixture execution

`RssFeedUpstreamParityTests` reads the fixture bytes from the pinned upstream
repository. It compares every field of all 13 `Mikan.xml` items with
`Mikan.json`, verifies the `2822_370.xml` Mikan identity and enclosures, locks
the malformed XML error code, and confirms missing-enclosure/invalid-length
behavior.

`UpstreamFilterFixtureParityTests` passes the pinned `filter/Mikan.xml` titles
through the production C# RSS and AutoBangumi parsers. It reproduces all
observable upstream fixture totals: 13 pass-through items, 4 `NC-Raws`
matches, 9 parsed 1080p matches, and the one inline regex candidate
`1108011`. This verifies the retained behavior without loading a Python
interpreter or dynamically executing source files.

The earlier parser closure remains authoritative for the three active Go
`ParseEp` cases and all 19 develop-branch AutoBangumi output goldens. Torrent
fixture bytes remain covered by `TorrentUpstreamParityTests`.

## Verification

With `ANIMEGO_UPSTREAM_REPO=E:\WorkSpaceAI\AnimeGoNet\AnimeGo`, the focused
suite passed 8/8 in Release. The complete solution then passed 1449/1449 with
zero failures and zero skips. `git diff --check` and the whitespace formatter
restricted to the three new C# files passed. The repository-wide whitespace
check continues to report two unrelated pre-existing files
(`MikanIdentityCookieTests.cs` and the protocol fixture `Program.cs`); this
module does not modify them or claim that baseline issue as its own result.

No Docker command was run. Docker artifacts remain generated but explicitly
unverified, per the project owner's instruction.
