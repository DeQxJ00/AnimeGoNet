# Parser parity closure — 2026-08-08

## Upstream baseline

`wetor/AnimeGo@develop` uses `internal/animego/parser.ParseEp` for three
active integer-Episode fixtures: bracketed `04`, bracketed `11`, and dashed
`- 11`. Its `ParseSp` is an unimplemented stub. The legacy Mikan filter's
Python `raw_parser.py` additionally extracts title variants, Season, Episode,
subtitle language, group, resolution and source.

## AnimeGoNet replacement

- `TorrentEpisodeCandidateParser` covers every active Go `ParseEp` fixture and
  the compatible bracket/version, dash, `EP`, `E` and Chinese Episode forms.
- `MikanRssEpisodeParser` selects the last reliable title marker so release
  dates and earlier numeric tags do not replace the actual Episode.
- Fractional Episodes and special material are explicit non-integer result
  kinds. They can be routed to `Other`; they cannot become ordinary Episode
  progress.
- `AutoBangumiRawParser` is compile-time C# and is checked against 19 golden
  fixtures generated from the develop-branch Python parser. All returned
  fields are compared, including the first-bracket group convention and
  full-width bracket normalization.
- The Mikan rule engine's production group parser is cross-checked against the
  same 19 fixtures, preventing the RSS filter identity from drifting from the
  raw title parser.
- `FileEpisodeCandidateResolver` remains a separate persistence safety layer.
  It rejects source adapters outside Mikan, year-like/resolution-like numbers,
  multiple Episode markers, non-feature material, unsupported special or
  fractional values, and compatibility-parser failures.

## Automated evidence

- `TorrentEpisodeCandidateParserTests`: all upstream fixtures plus supported
  integer forms and non-integer classification.
- `MikanRssEpisodeParserTests`: title-level marker selection and exclusion of
  dates, special material and fractional Episodes.
- `AutoBangumiRawParserTests`: complete 19-fixture field comparison, production
  group-parser cross-check and persistence candidate policy.
- `LegacyMikanFilterEngineTests`: first-bracket and full-width-bracket group
  semantics used by the compatibility filter.

No Python runtime, dynamic plugin loading or reflection-based serialization is
introduced. The parser remains compatible with NativeAOT.

Revalidated on 2026-08-08: the focused parser and group suite passed 60/60;
the complete solution passed 1350/1350 with zero failures and zero skips;
`dotnet format --verify-no-changes` and `git diff --check` passed. This closure
changes only tests and documentation, so the production source tree is
identical to the previously successful win-x64 NativeAOT publish.
