# Metadata HTTP retry verification — 2026-07-30

## Behavior

TMDB and Bangumi now expose independent retry settings beside their existing
API base URL, proxy and timeout settings:

- `retry_count`: 0 through 10 extra attempts; default 3;
- `retry_wait_seconds` in deployment YAML and
  `retry_delay_seconds` in the runtime API: 0 through 300 seconds; default 5.

The original Go global request defaults were three extra attempts and a
five-second wait. AnimeGoNet keeps those defaults while narrowing unsafe
behavior: only connection failures, per-attempt timeouts, HTTP 429 and 5xx are
retried. A 404, authentication failure, other 4xx, invalid JSON, identity
mismatch and deterministic no-match are not retried.

Every attempt creates a new GET request, including the TMDB API key or Bearer
header and Bangumi User-Agent. Caller cancellation is distinguished from a
per-attempt timeout and interrupts both an active request and the retry delay.
Failure messages remain stable safe codes and never include credentials or
request URLs.

## Configuration surfaces

The settings are available through:

- canonical deployment YAML for TMDB and Bangumi;
- `--tmdb_retry_count`, `--tmdb_retry_wait_second`,
  `--bangumi_retry_count` and `--bangumi_retry_wait_second`;
- matching environment variables, which lock the WebUI fields;
- `/api/v1/config`, its redacted editable projection and two-step diff;
- the static TypeScript WebUI configuration editor and summary cards.

Legacy `advanced.request.retry_num` and `retry_wait_second` migrate to both
metadata clients. Older private JSON and older API update payloads that omit
the new fields continue to inherit existing deployment values rather than
silently changing them to zero.

## Fault-injection coverage

Tests verify:

- network exception, 429 and 5xx retry before success;
- a fresh credentialed request and Bangumi User-Agent on every attempt;
- a timed-out attempt followed by success;
- no retry for 404, authentication or protocol errors;
- immediate caller cancellation during a long retry delay;
- option bounds, YAML/legacy migration, command-line binding;
- runtime private override persistence, environment locking and old-payload
  compatibility;
- redacted API projection and static TypeScript/HTML controls.

## Results

```text
focused Core configuration tests       17 passed
focused App/network/config/API tests   76 passed
TypeScript 7 strict check              passed
complete Release suite:
  Plugin abstractions                  11
  Core                                281
  Data                                152
  App                                 517
  total                               961 passed, 0 failed, 0 skipped
```

`win-x64` NativeAOT publish completed without AOT or trimming warnings. The
published executable started from an isolated directory, listened on an
isolated loopback port, generated both YAML retry sections, and its native
`/api/v1/config` response returned TMDB and Bangumi defaults of 3 attempts and
5 seconds. The exact AOT smoke processes were stopped; ports 6280 and 6281
were confirmed no longer listening.
