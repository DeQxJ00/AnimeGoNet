# Six-field Cron scheduler verification

## Upstream behavior retained

The Go baseline constructs `robfig/cron` with seconds enabled, supports `StartRun`,
reports `NextTime`, executes different triggers concurrently, and retries a failed
task three times with a three-second delay. It places the zero-based attempt number
in `__retry_count__`.

## .NET implementation

- `SixFieldCronExpression` is dependency-free and NativeAOT-safe. It supports six
  fields (second through day-of-week), `*`, day-field `?`, lists, ranges, steps,
  English month/day names, and the standard yearly/monthly/weekly/daily/hourly
  descriptors.
- Restricted day-of-month and day-of-week fields use Cron OR semantics. Next-time
  calculation is calendar-based rather than a second-by-second scan.
- Caller-selected time zones are honored. Invalid spring-forward wall times are
  skipped and both fall-back occurrences are ordered by their UTC instant.
- `PluginScheduleCoordinator` binds stable names to compile-time registered
  `IScheduledPlugin` IDs, immutable arguments, a Cron expression, a time zone and
  `StartRun`. Add/remove wakes the scheduler immediately.
- Each trigger advances `NextTime` before launching an independently tracked call.
  Failure is retried exactly three times at three-second intervals with
  `__retry_count__=0/1/2`; exceptions become `schedule_execution_failed` without
  exposing exception text.
- The application cancellation token reaches waits, retry delays and plugin calls.
  The hosted service waits for tracked calls to observe cancellation before exit.

## Tests and safety

- Core tests cover upstream examples (`*/3`, `0 0/20`), lists/ranges/steps,
  descriptors, DOM/DOW OR, leap day, invalid expressions and DST transitions.
- App tests use controlled clocks and fake scheduled plugins for StartRun, retry
  count/delay, NextTime advancement, registration conflicts, missing plugins,
  removal and cancellation.
- No network, Torrent, qBittorrent, user data or wall-clock sleep is used by these
  scheduler tests.

Final local gates: all 766 .NET tests passed (Plugin 11, Core 253, Data 121,
App 381), TypeScript strict checking passed, and `win-x64` NativeAOT publish
completed without warnings. The isolated native smoke initialized schema v26,
reported `native_aot=true`, served the static WebUI and exited cleanly.
