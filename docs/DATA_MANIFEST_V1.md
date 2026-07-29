# AnimeGoNetData manifest and JSONL v1

This document is the wire contract between the independent `AnimeGoNetData`
publisher and AnimeGoNet clients. The publisher may add optional fields without
changing `schema_version`. Renaming, removing or changing the meaning/type of a
required field requires a new schema major and a new asset name prefix.

## `manifest.json`

```json
{
  "schema_version": 1,
  "data_version": "2026.07.29.1",
  "generated_at_utc": "2026-07-29T12:00:00.0000000+00:00",
  "minimum_client_version": "0.1.0",
  "upstream": {
    "repository": "https://github.com/bangumi/Archive",
    "release": "archive-2026-07-29",
    "asset": "bangumi-json-20260729.zip",
    "sha256": "64 lowercase hexadecimal characters"
  },
  "assets": [
    {
      "kind": "subjects",
      "file_name": "bangumi-subjects-v1-000001-100000.jsonl.gz",
      "url": "https://host.example/release/bangumi-subjects-v1-000001-100000.jsonl.gz",
      "size_bytes": 123456,
      "sha256": "64 lowercase hexadecimal characters",
      "record_count": 50000,
      "subject_id_min": 1,
      "subject_id_max": 100000
    }
  ],
  "totals": {
    "subjects": 50000,
    "episodes": 600000
  }
}
```

Required invariants:

- UTF-8 JSON, at most 1 MiB, no comments or trailing commas;
- `schema_version=1`; unknown schema major is rejected before downloading;
- `data_version` is a stable lowercase ID (`[a-z0-9._-]`, at most 64 chars);
- timestamps use the round-trip UTC `O` form with offset `+00:00`;
- `minimum_client_version` is a numeric .NET-style version;
- upstream and asset SHA-256 values are exactly 64 lowercase hex characters;
- every asset name is a unique basename ending in `.jsonl.gz`;
- asset URLs use HTTP(S), contain no user info or fragment, and are not logged;
- assets are non-empty and at most 8 GiB each;
- each asset declares a positive record count and inclusive positive Subject ID
  range; both `subjects` and `episodes` kinds are required;
- totals exactly equal the sum of asset record counts by kind.

The release is immutable: an existing `data_version`, manifest or named asset
must never be replaced. Publish all versioned assets first and the `latest`
manifest last.

## Subject JSONL

Each decompressed line is one object:

```json
{
  "id": 51,
  "name": "CLANNAD",
  "name_cn": "CLANNAD",
  "air_date": "2007-10-05",
  "episode_count": 23
}
```

`id` is a unique positive Bangumi Subject ID. `name` is required; `name_cn`
may be null. `air_date` is null or ISO `yyyy-MM-dd`. `episode_count` is a
non-negative integer. Assets are sorted by `id` ascending and their IDs must
stay inside the manifest range.

## Episode JSONL

Each decompressed line is one normal Episode:

```json
{
  "id": 1423,
  "subject_id": 51,
  "sort": 1,
  "episode": "1",
  "air_date": "2007-10-05"
}
```

`id` is globally unique and positive. `subject_id` must reference a Subject in
the same data version. `sort` is a positive stable ordering integer.
`episode` is the invariant decimal string reported by Bangumi and must be
positive; non-integer values remain data evidence but never become unverified
TMDB Episode identity. `air_date` is null or ISO `yyyy-MM-dd`.

## Determinism and validation

Publisher output is sorted, UTF-8 without BOM, one LF per line, JSON properties
in the order shown, and gzip metadata is normalized. The same upstream input
must produce byte-identical assets. Publication requires unique IDs, valid
ranges, Subject references, exact counts, SHA-256/size checks and configured
minimum count thresholds.
