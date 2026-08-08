# Bangumi Archive production pipeline — 2026-08-08

## Authoritative input

The official `bangumi/Archive` repository documents weekly exports and the
root JSON Lines files. Its `aux/latest.json` supplies the current GitHub release
asset URL, exact file name, UTC creation time, size and `sha256:` digest. The
AnimeGoNet workflow accepts only the fixed official release URL shape, validates
the file name/timestamp/digest syntax, downloads the ZIP and verifies SHA-256
before starting the builder.

## Build boundary

`AnimeGoNet.DataBuilder` is a separate .NET 10 AOT-compatible executable. It:

- requires the declared upstream asset name and SHA-256 to match the local ZIP;
- reads exactly one root `subject.jsonlines` and `episode.jsonlines` entry;
- retains anime Subjects (`type=2`) and normal Episodes (`type=0`), excluding
  unrelated archive tables without extracting them;
- normalizes title controls/whitespace, nullable dates and invariant fractional
  Episode numbers;
- sorts IDs deterministically, assigns an integer storage order per Subject and
  calculates the normal-Episode count used by archive completeness checks;
- writes each Episode asset in strict Episode-ID order required by the importer,
  while preserving the separately calculated per-Subject episode ordering;
- shards Subjects and their Episodes by Subject range, writes LF-only UTF-8
  JSONL.gz, then records size, count, range and SHA-256 in schema-v1 manifest;
- parses its own manifest through the production `DataManifestParser`;
- creates a strict offline ZIP containing only `manifest.json` and declared
  assets, with a fixed timestamp and byte-identical output for identical input;
- refuses to expose output when retained anime Subjects or normal Episodes fall
  below caller-configured count floors; the official workflow fixes these at
  30,000 and 300,000 respectively;
- builds in a unique sibling partial directory and exposes the destination only
  by final atomic directory rename. Hash/format failures remove partial output.

The workflow runs daily at 23:00 UTC and supports manual dispatch. It has
read-only repository permission and uploads a seven-day Actions artifact. It
does not create or overwrite a GitHub Release in the main program repository;
publishing to the independent AnimeGoNetData repository remains a separate
repository-owner action.

## Test evidence

`BangumiArchivePackageBuilderTests` constructs safe miniature ZIPs using the
official field shapes. It proves filtering, out-of-order sorting, fractional EP
preservation, invalid-date nulling, Subject-range shards, unique retained
Subject/Episode IDs, output Episode-to-Subject references, configured count
floors, reverse ID/episode ordering, hashes, strict offline ZIP entries,
deterministic bytes, failure cleanup
and daily/manual workflow safety. The generated assets are imported by the real
`DataPackageStore`, activated in a temporary SQLite database and read through
`BangumiArchiveStore`, proving the publisher and consumer schemas agree.

Revalidated on 2026-08-08: builder/workflow tests passed 9/9, including the
8 MiB streaming JSONL line boundary; the complete solution passed 1364/1364
with zero failures and zero skips; changed-file
`dotnet format --verify-no-changes`, workflow YAML parsing and
`git diff --check` passed. A fresh win-x64 `PublishAot=true` publish completed
`Generating native code`, and the resulting native DataBuilder executable
returned both count-floor options in its complete `--help` contract. The tests
used only generated miniature Archive ZIPs and did not download the current
public release or access any user data.
