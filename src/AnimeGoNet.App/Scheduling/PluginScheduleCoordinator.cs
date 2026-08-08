using AnimeGo.Plugin.Abstractions;
using AnimeGoNet.Core.Diagnostics;
using AnimeGoNet.Core.Scheduling;

namespace AnimeGoNet.App.Scheduling;

public sealed record PluginScheduleRegistration(
    string Name,
    string PluginId,
    string Cron,
    bool StartRun,
    IReadOnlyDictionary<string, string>? Arguments = null,
    TimeZoneInfo? TimeZone = null);

public sealed record PluginScheduleSnapshot(
    string Name,
    string PluginId,
    string Cron,
    bool StartRun,
    DateTimeOffset NextTime,
    int RunningCount,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastCompletedAt,
    string? LastFailureCode);

public sealed class PluginScheduleException(string code, string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    public string Code { get; } = StableErrorCode.Require(code, nameof(code));
}

internal interface IScheduleClock
{
    DateTimeOffset UtcNow { get; }

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class SystemScheduleClock : IScheduleClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

public sealed class PluginScheduleCoordinator : IDisposable
{
    internal const int RetryCount = 3;
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    private readonly PluginCatalog _plugins;
    private readonly IScheduleClock _clock;
    private readonly Lock _sync = new();
    private readonly Dictionary<string, ScheduleEntry> _entries = new(StringComparer.Ordinal);
    private readonly HashSet<Task> _running = [];
    private readonly SemaphoreSlim _changed = new(0, 1);

    public PluginScheduleCoordinator(PluginCatalog plugins)
        : this(plugins, new SystemScheduleClock())
    {
    }

    internal PluginScheduleCoordinator(PluginCatalog plugins, IScheduleClock clock)
    {
        _plugins = plugins;
        _clock = clock;
    }

    public async Task AddAsync(
        PluginScheduleRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var name = NormalizeName(registration.Name);
        if (string.IsNullOrWhiteSpace(registration.PluginId))
        {
            throw new PluginScheduleException(
                "schedule_plugin_missing",
                "Scheduled plugin is not registered.");
        }
        var pluginId = registration.PluginId.Trim().ToLowerInvariant();
        IScheduledPlugin plugin;
        if (string.IsNullOrWhiteSpace(registration.Cron))
        {
            throw new PluginScheduleException(
                "cron_expression_empty",
                "Cron expression is required.");
        }
        try
        {
            plugin = _plugins.Require<IScheduledPlugin>(pluginId);
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            throw new PluginScheduleException(
                "schedule_plugin_missing",
                "Scheduled plugin is not registered.",
                exception);
        }

        SixFieldCronExpression cron;
        try
        {
            cron = SixFieldCronExpression.Parse(registration.Cron);
        }
        catch (CronExpressionException exception)
        {
            throw new PluginScheduleException(exception.Code, exception.Message, exception);
        }

        var timeZone = registration.TimeZone ?? TimeZoneInfo.Local;
        var next = cron.GetNextOccurrence(_clock.UtcNow, timeZone)
            ?? throw new PluginScheduleException(
                "schedule_next_time_missing",
                "Cron expression has no future occurrence.");
        var arguments = registration.Arguments is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(registration.Arguments, StringComparer.Ordinal);
        var entry = new ScheduleEntry(
            name,
            pluginId,
            registration.Cron.Trim(),
            registration.StartRun,
            cron,
            timeZone,
            plugin,
            arguments,
            next);

        lock (_sync)
        {
            if (!_entries.TryAdd(name, entry))
            {
                throw new PluginScheduleException(
                    "schedule_name_conflict",
                    "A scheduled task with the same name already exists.");
            }
        }
        SignalChanged();

        if (registration.StartRun)
        {
            await ExecuteWithRetryAsync(
                entry, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
        }
    }

    public bool Remove(string name)
    {
        var normalized = NormalizeName(name);
        bool removed;
        lock (_sync)
        {
            removed = _entries.Remove(normalized);
        }
        if (removed) SignalChanged();
        return removed;
    }

    public PluginScheduleSnapshot? Get(string name)
    {
        var normalized = NormalizeName(name);
        lock (_sync)
        {
            return _entries.TryGetValue(normalized, out var entry)
                ? Snapshot(entry)
                : null;
        }
    }

    public IReadOnlyList<PluginScheduleSnapshot> List()
    {
        lock (_sync)
        {
            return _entries.Values
                .OrderBy(entry => entry.Name, StringComparer.Ordinal)
                .Select(Snapshot)
                .ToArray();
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = _clock.UtcNow;
                List<ScheduleEntry> due;
                DateTimeOffset? next;
                lock (_sync)
                {
                    due = _entries.Values.Where(entry => entry.NextTime <= now).ToList();
                    foreach (var entry in due)
                    {
                        entry.NextTime = entry.CronExpression.GetNextOccurrence(now, entry.TimeZone)
                            ?? throw new PluginScheduleException(
                                "schedule_next_time_missing",
                                "Cron expression has no future occurrence.");
                    }
                    next = _entries.Count == 0
                        ? null
                        : _entries.Values.Min(entry => entry.NextTime);
                }

                foreach (var entry in due)
                {
                    Track(ExecuteWithRetryAsync(entry, now, cancellationToken));
                }

                var delay = next is null
                    ? Timeout.InfiniteTimeSpan
                    : next.Value - _clock.UtcNow;
                if (delay != Timeout.InfiniteTimeSpan && delay <= TimeSpan.Zero)
                {
                    continue;
                }
                await WaitForDelayOrChangeAsync(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            Task[] running;
            lock (_sync)
            {
                running = _running.ToArray();
            }
            try
            {
                await Task.WhenAll(running).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private async Task ExecuteWithRetryAsync(
        ScheduleEntry entry,
        DateTimeOffset triggeredAt,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            entry.RunningCount++;
            entry.LastStartedAt = _clock.UtcNow;
            entry.LastFailureCode = null;
        }

        try
        {
            for (var attempt = 0; attempt < RetryCount; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var arguments = new Dictionary<string, string>(
                    entry.Arguments, StringComparer.Ordinal)
                {
                    ["__retry_count__"] = attempt.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                };
                ScheduledResult result;
                try
                {
                    result = await entry.Plugin.ExecuteAsync(
                        new ScheduledContext(entry.Name, triggeredAt, arguments),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    result = new ScheduledResult(
                        false,
                        null,
                        [new PluginOperationError(
                            "schedule_execution_failed",
                            "Scheduled plugin execution failed.")],
                        null);
                }

                if (result.Succeeded)
                {
                    lock (_sync)
                    {
                        entry.LastFailureCode = null;
                    }
                    return;
                }

                lock (_sync)
                {
                    entry.LastFailureCode = result.Errors.Count > 0
                        && !string.IsNullOrWhiteSpace(result.Errors[0].Code)
                        ? result.Errors[0].Code
                        : "schedule_execution_failed";
                }
                if (attempt < RetryCount - 1)
                {
                    await _clock.DelayAsync(RetryDelay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            lock (_sync)
            {
                entry.RunningCount--;
                entry.LastCompletedAt = _clock.UtcNow;
            }
        }
    }

    private async Task WaitForDelayOrChangeAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = delay == Timeout.InfiniteTimeSpan
            ? Task.Delay(Timeout.InfiniteTimeSpan, waitCancellation.Token)
            : _clock.DelayAsync(delay, waitCancellation.Token);
        var changedTask = _changed.WaitAsync(waitCancellation.Token);
        var completed = await Task.WhenAny(delayTask, changedTask).ConfigureAwait(false);
        await waitCancellation.CancelAsync().ConfigureAwait(false);
        try
        {
            await completed.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    private void Track(Task task)
    {
        lock (_sync)
        {
            _running.Add(task);
        }
        _ = ObserveAsync(task);
    }

    private async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lock (_sync)
            {
                _running.Remove(task);
            }
        }
    }

    private void SignalChanged()
    {
        try
        {
            _changed.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim().ToLowerInvariant();
        if (normalized.Length > 64
            || normalized.Any(character => character is not (
                >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-' or '_')))
        {
            throw new PluginScheduleException(
                "schedule_name_invalid",
                "Scheduled task name must be a lowercase stable identifier.");
        }
        return normalized;
    }

    private static PluginScheduleSnapshot Snapshot(ScheduleEntry entry) =>
        new(
            entry.Name,
            entry.PluginId,
            entry.Cron,
            entry.StartRun,
            entry.NextTime,
            entry.RunningCount,
            entry.LastStartedAt,
            entry.LastCompletedAt,
            entry.LastFailureCode);

    public void Dispose() => _changed.Dispose();

    private sealed class ScheduleEntry(
        string name,
        string pluginId,
        string cron,
        bool startRun,
        SixFieldCronExpression cronExpression,
        TimeZoneInfo timeZone,
        IScheduledPlugin plugin,
        IReadOnlyDictionary<string, string> arguments,
        DateTimeOffset nextTime)
    {
        public string Name { get; } = name;
        public string PluginId { get; } = pluginId;
        public string Cron { get; } = cron;
        public bool StartRun { get; } = startRun;
        public SixFieldCronExpression CronExpression { get; } = cronExpression;
        public TimeZoneInfo TimeZone { get; } = timeZone;
        public IScheduledPlugin Plugin { get; } = plugin;
        public IReadOnlyDictionary<string, string> Arguments { get; } = arguments;
        public DateTimeOffset NextTime { get; set; } = nextTime;
        public int RunningCount { get; set; }
        public DateTimeOffset? LastStartedAt { get; set; }
        public DateTimeOffset? LastCompletedAt { get; set; }
        public string? LastFailureCode { get; set; }
    }
}
