using AnimeGo.Plugin.Abstractions;
using AnimeGoNet.App.Scheduling;

namespace AnimeGoNet.App.Tests.Scheduling;

public sealed class PluginScheduleCoordinatorTests
{
    [Fact]
    public async Task StartRunExecutesImmediatelyWithUpstreamRetryContract()
    {
        var plugin = new RecordingPlugin(failuresBeforeSuccess: 2);
        var clock = new ImmediateClock(
            DateTimeOffset.Parse(
                "2026-07-29T10:00:01Z", System.Globalization.CultureInfo.InvariantCulture));
        using var coordinator = Coordinator(plugin, clock);

        await coordinator.AddAsync(new PluginScheduleRegistration(
            "refresh",
            "test-schedule",
            "0 * * * * ?",
            StartRun: true,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = "cache" },
            TimeZoneInfo.Utc));

        Assert.Equal(3, plugin.Contexts.Count);
        Assert.Equal(["0", "1", "2"], plugin.Contexts.Select(
            context => context.Arguments["__retry_count__"]));
        Assert.All(plugin.Contexts, context => Assert.Equal("cache", context.Arguments["scope"]));
        Assert.Equal(TimeSpan.FromSeconds(6), clock.TotalDelay);
        var snapshot = Assert.IsType<PluginScheduleSnapshot>(coordinator.Get("REFRESH"));
        Assert.Equal(
            DateTimeOffset.Parse(
                "2026-07-29T10:01:00Z", System.Globalization.CultureInfo.InvariantCulture),
            snapshot.NextTime);
        Assert.Null(snapshot.LastFailureCode);
        Assert.NotNull(snapshot.LastCompletedAt);
    }

    [Fact]
    public async Task RunTriggersAtNextTimeAndAdvancesSchedule()
    {
        var plugin = new RecordingPlugin();
        var clock = new ManualClock(
            DateTimeOffset.Parse(
                "2026-07-29T10:00:01Z", System.Globalization.CultureInfo.InvariantCulture));
        using var coordinator = Coordinator(plugin, clock);
        await coordinator.AddAsync(new PluginScheduleRegistration(
            "poll", "test-schedule", "*/10 * * * * ?", false, TimeZone: TimeZoneInfo.Utc));
        using var cancellation = new CancellationTokenSource();
        var run = coordinator.RunAsync(cancellation.Token);
        await clock.WaitForDelayAsync();

        clock.AdvanceTo(DateTimeOffset.Parse(
            "2026-07-29T10:00:10Z", System.Globalization.CultureInfo.InvariantCulture));
        await WaitUntilAsync(() => plugin.Contexts.Count == 1);

        var context = Assert.Single(plugin.Contexts);
        Assert.Equal("poll", context.TaskId);
        Assert.Equal(
            DateTimeOffset.Parse(
                "2026-07-29T10:00:10Z", System.Globalization.CultureInfo.InvariantCulture),
            context.TriggeredAt);
        Assert.Equal(
            DateTimeOffset.Parse(
                "2026-07-29T10:00:20Z", System.Globalization.CultureInfo.InvariantCulture),
            coordinator.Get("poll")!.NextTime);

        await cancellation.CancelAsync();
        await run;
    }

    [Fact]
    public async Task FailedRunRetainsSafeFailureAndStopsAfterThreeAttempts()
    {
        var plugin = new RecordingPlugin(failuresBeforeSuccess: int.MaxValue);
        var clock = new ImmediateClock(DateTimeOffset.UtcNow);
        using var coordinator = Coordinator(plugin, clock);

        await coordinator.AddAsync(new PluginScheduleRegistration(
            "failure", "test-schedule", "@hourly", true, TimeZone: TimeZoneInfo.Utc));

        Assert.Equal(PluginScheduleCoordinator.RetryCount, plugin.Contexts.Count);
        Assert.Equal("synthetic_failure", coordinator.Get("failure")!.LastFailureCode);
        Assert.Equal(TimeSpan.FromSeconds(6), clock.TotalDelay);
    }

    [Fact]
    public async Task ValidatesRegistrationAndRemoveIsIdempotent()
    {
        var plugin = new RecordingPlugin();
        using var coordinator = Coordinator(plugin, new ImmediateClock(DateTimeOffset.UtcNow));
        await coordinator.AddAsync(new PluginScheduleRegistration(
            "task", "test-schedule", "@daily", false, TimeZone: TimeZoneInfo.Utc));

        var conflict = await Assert.ThrowsAsync<PluginScheduleException>(() =>
            coordinator.AddAsync(new PluginScheduleRegistration(
                "TASK", "test-schedule", "@daily", false, TimeZone: TimeZoneInfo.Utc)));
        var invalidCron = await Assert.ThrowsAsync<PluginScheduleException>(() =>
            coordinator.AddAsync(new PluginScheduleRegistration(
                "bad", "test-schedule", "not cron", false, TimeZone: TimeZoneInfo.Utc)));
        var missing = await Assert.ThrowsAsync<PluginScheduleException>(() =>
            coordinator.AddAsync(new PluginScheduleRegistration(
                "missing", "unknown", "@daily", false, TimeZone: TimeZoneInfo.Utc)));

        Assert.Equal("schedule_name_conflict", conflict.Code);
        Assert.Equal("cron_field_count_invalid", invalidCron.Code);
        Assert.Equal("schedule_plugin_missing", missing.Code);
        Assert.True(coordinator.Remove(" TASK "));
        Assert.False(coordinator.Remove("task"));
        Assert.Empty(coordinator.List());
    }

    [Fact]
    public async Task HotAddWakesSchedulerTriggersConcurrentlyAndCancellationReachesCalls()
    {
        var plugin = new BlockingPlugin();
        var clock = new ManualClock(
            DateTimeOffset.Parse(
                "2026-07-29T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        using var coordinator = Coordinator(plugin, clock);
        using var cancellation = new CancellationTokenSource();
        var run = coordinator.RunAsync(cancellation.Token);

        await coordinator.AddAsync(new PluginScheduleRegistration(
            "hot", "blocking-schedule", "* * * * * ?", false, TimeZone: TimeZoneInfo.Utc));
        await clock.WaitForDelayAsync();
        clock.AdvanceTo(DateTimeOffset.Parse(
            "2026-07-29T10:00:01Z", System.Globalization.CultureInfo.InvariantCulture));
        await WaitUntilAsync(() => plugin.CallCount == 1);
        await clock.WaitForDelayAsync();
        clock.AdvanceTo(DateTimeOffset.Parse(
            "2026-07-29T10:00:02Z", System.Globalization.CultureInfo.InvariantCulture));
        await WaitUntilAsync(() => plugin.CallCount == 2);

        Assert.Equal(2, coordinator.Get("hot")!.RunningCount);
        await cancellation.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, plugin.CancellationCount);
        Assert.Equal(0, coordinator.Get("hot")!.RunningCount);
    }

    [Fact]
    public async Task ConcurrentUniqueRegistrationsCoalesceWakeSignalWithoutLosingTasks()
    {
        var plugin = new RecordingPlugin();
        using var coordinator = Coordinator(plugin, new ImmediateClock(DateTimeOffset.UtcNow));

        await Task.WhenAll(Enumerable.Range(0, 16).Select(index =>
            coordinator.AddAsync(new PluginScheduleRegistration(
                $"task-{index}",
                "test-schedule",
                "@daily",
                false,
                TimeZone: TimeZoneInfo.Utc))));

        Assert.Equal(16, coordinator.List().Count);
    }

    private static PluginScheduleCoordinator Coordinator(
        IScheduledPlugin plugin,
        IScheduleClock clock) =>
        new(new PluginCatalog([plugin]), clock);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
        Assert.True(condition());
    }

    private sealed class RecordingPlugin(int failuresBeforeSuccess = 0) : IScheduledPlugin
    {
        private int _remainingFailures = failuresBeforeSuccess;

        public PluginDescriptor Descriptor { get; } =
            new("test-schedule", "Test schedule", "1.0.0", PluginCategory.Schedule);

        public List<ScheduledContext> Contexts { get; } = [];

        public ValueTask<ScheduledResult> ExecuteAsync(
            ScheduledContext context,
            CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            return ValueTask.FromResult(_remainingFailures-- > 0
                ? new ScheduledResult(
                    false,
                    null,
                    [new PluginOperationError("synthetic_failure", "Synthetic failure.")],
                    null)
                : new ScheduledResult(true, "ok", [], null));
        }
    }

    private sealed class BlockingPlugin : IScheduledPlugin
    {
        private int _callCount;
        private int _cancellationCount;

        public PluginDescriptor Descriptor { get; } =
            new("blocking-schedule", "Blocking schedule", "1.0.0", PluginCategory.Schedule);

        public int CallCount => Volatile.Read(ref _callCount);
        public int CancellationCount => Volatile.Read(ref _cancellationCount);

        public async ValueTask<ScheduledResult> ExecuteAsync(
            ScheduledContext context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new ScheduledResult(true, "unreachable", [], null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Interlocked.Increment(ref _cancellationCount);
                throw;
            }
        }
    }

    private sealed class ImmediateClock(DateTimeOffset utcNow) : IScheduleClock
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public TimeSpan TotalDelay { get; private set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TotalDelay += delay;
            UtcNow += delay;
            return Task.CompletedTask;
        }
    }

    private sealed class ManualClock(DateTimeOffset utcNow) : IScheduleClock
    {
        private readonly Lock _sync = new();
        private readonly List<Waiter> _waiters = [];

        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var waiter = new Waiter(UtcNow + delay, completion);
            lock (_sync)
            {
                _waiters.Add(waiter);
            }
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            return completion.Task;
        }

        public void AdvanceTo(DateTimeOffset value)
        {
            Waiter[] due;
            lock (_sync)
            {
                UtcNow = value;
                due = _waiters.Where(waiter => waiter.Due <= value).ToArray();
                _waiters.RemoveAll(waiter => due.Contains(waiter));
            }
            foreach (var waiter in due) waiter.Completion.TrySetResult();
        }

        public async Task WaitForDelayAsync()
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                lock (_sync)
                {
                    if (_waiters.Count > 0) return;
                }
                await Task.Delay(10);
            }
            throw new TimeoutException("Scheduler did not request a delay.");
        }

        private sealed record Waiter(
            DateTimeOffset Due,
            TaskCompletionSource Completion);
    }
}
