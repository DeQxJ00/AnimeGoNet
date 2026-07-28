# Mikan manual rule sample Episode verification

## Contract

- `sample_source_episode` is optional for backward compatibility.
- When present, positive TMDB Series/Season and an Episode offset are mandatory.
- The target is calculated with checked arithmetic as
  `sample_source_episode + episode_offset` and must be positive.
- The server validates TV Series, ordinary Season and target Episode through the
  configured TMDB client before persisting the rule.
- Missing or mismatched TMDB identities return stable safe error codes and do not
  create or revise the rule. Runtime Episode validation remains mandatory.
- API error serialization uses the source-generated context and remains
  NativeAOT-safe.

## Acceptance

- Source EP4 with offset `+13` validates TMDB Series 72517, Season 2, Episode 17
  and then saves revision 1.
- A missing Episode returns `mikan_rule_tmdb_episode_not_found`; a subsequent GET
  remains 404, proving no partial rule write.

## Result

- Mikan work rule API target: 4 passed.
- Full solution: 510 passed (`Core 199`, `Data 75`, `App 236`), 0 failed,
  0 skipped.
- `win-x64` NativeAOT publish: passed with no warnings or errors.
