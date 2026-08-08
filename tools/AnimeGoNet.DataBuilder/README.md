# AnimeGoNet.DataBuilder

This AOT-compatible release tool converts the official
[`bangumi/Archive`](https://github.com/bangumi/Archive) ZIP into the bounded
AnimeGoNetData schema consumed by `DataPackageStore`.

It verifies the ZIP SHA-256 from `aux/latest.json`, requires the declared asset
name to match the local file, reads only the root `subject.jsonlines` and
`episode.jsonlines`, keeps anime Subjects (`type=2`) and normal Episodes
(`type=0`), normalizes dates/text and preserves fractional Episode numbers. It
sorts records, calculates normal Episode counts, shards by Subject range, emits
deterministic JSONL.gz assets and `manifest.json`, then creates the strict
offline-import ZIP. Output is staged beside the destination and renamed only
after every hash and manifest validation succeeds.

Example:

```powershell
dotnet run --project tools/AnimeGoNet.DataBuilder -- `
  --input dump-2026-08-04.210502Z.zip `
  --output artifacts/animegonet-data-2026.08.04.1 `
  --data-version 2026.08.04.1 `
  --asset-base-url https://github.com/example/AnimeGoNet/releases/download/animegonet-data-2026.08.04.1/ `
  --upstream-release archive `
  --upstream-asset dump-2026-08-04.210502Z.zip `
  --upstream-sha256 <sha256-from-latest-json> `
  --generated-at-utc 2026-08-04T21:05:03.0000000+00:00
```

The builder never downloads data itself and never accepts credentials. The
scheduled workflow performs the official metadata/download step and publishes
only a short-lived Actions artifact; creating a public release remains an
explicit repository-owner operation.
