using AnimeGo.Plugin.Abstractions;
using AnimeGoNet.App.Configuration;
using AnimeGoNet.App.Scheduling;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Tests.Scheduling;

public sealed class DataUpdateScheduleManagerTests
{
    [Fact]
    public async Task EnabledPolicyAddsAndReplacesScheduleWithoutRestart()
    {
        using var coordinator = Coordinator();
        var initial = Options(enabled: false, cron: "0 0 4 * * ?");
        var runtimeOptions = new DataUpdateRuntimeState(initial);
        using var manager = new DataUpdateScheduleManager(
            coordinator,
            runtimeOptions,
            new RuntimeConfigurationState(false, true, false));
        var first = Options(enabled: true, cron: "0 15 4 * * ?");
        var second = first with { Cron = "0 45 5 * * ?", KeepVersions = 4 };

        await manager.ApplyAsync(first);
        var firstSchedule = Assert.IsType<PluginScheduleSnapshot>(
            coordinator.Get(DataUpdateScheduleManager.ScheduleName));
        Assert.Equal(first.Cron, firstSchedule.Cron);
        Assert.Equal(first, runtimeOptions.Value);

        await manager.ApplyAsync(second);
        var secondSchedule = Assert.IsType<PluginScheduleSnapshot>(
            coordinator.Get(DataUpdateScheduleManager.ScheduleName));
        Assert.Equal(second.Cron, secondSchedule.Cron);
        Assert.Equal(second, runtimeOptions.Value);
        Assert.Single(coordinator.List());
    }

    [Fact]
    public async Task DisableRemovesScheduleButKeepsManualRuntimePolicyCurrent()
    {
        using var coordinator = Coordinator();
        var enabled = Options(enabled: true, cron: "0 15 4 * * ?");
        var runtimeOptions = new DataUpdateRuntimeState(enabled);
        using var manager = new DataUpdateScheduleManager(
            coordinator,
            runtimeOptions,
            new RuntimeConfigurationState(false, true, false));
        await manager.ApplyAsync(enabled);
        var disabled = enabled with
        {
            Enabled = false,
            AutoDownload = false,
            AutoImport = false,
        };

        await manager.ApplyAsync(disabled);

        Assert.Null(coordinator.Get(DataUpdateScheduleManager.ScheduleName));
        Assert.Equal(disabled, runtimeOptions.Value);
    }

    [Fact]
    public async Task BackgroundWorkersDisabledUpdatesRuntimeWithoutRegisteringSchedule()
    {
        using var coordinator = Coordinator();
        var initial = Options(enabled: false, cron: "0 0 4 * * ?");
        var runtimeOptions = new DataUpdateRuntimeState(initial);
        using var manager = new DataUpdateScheduleManager(
            coordinator,
            runtimeOptions,
            new RuntimeConfigurationState(false, false, false));
        var updated = Options(enabled: true, cron: "0 30 4 * * ?");

        await manager.ApplyAsync(updated);

        Assert.Empty(coordinator.List());
        Assert.Equal(updated, runtimeOptions.Value);
    }

    [Fact]
    public async Task InvalidReplacementRollsBackPreviousScheduleAndRuntimePolicy()
    {
        using var coordinator = Coordinator();
        var initial = Options(enabled: false, cron: "0 0 4 * * ?");
        var runtimeOptions = new DataUpdateRuntimeState(initial);
        using var manager = new DataUpdateScheduleManager(
            coordinator,
            runtimeOptions,
            new RuntimeConfigurationState(false, true, false));
        var valid = Options(enabled: true, cron: "0 15 4 * * ?");
        await manager.ApplyAsync(valid);

        var exception = await Assert.ThrowsAsync<PluginScheduleException>(
            () => manager.ApplyAsync(valid with { Cron = "invalid cron" }));

        Assert.Equal("cron_field_count_invalid", exception.Code);
        Assert.Equal(valid, runtimeOptions.Value);
        Assert.Equal(
            valid.Cron,
            coordinator.Get(DataUpdateScheduleManager.ScheduleName)!.Cron);
        Assert.Single(coordinator.List());
    }

    [Fact]
    public async Task StartupRegistrationReadsLatestRuntimeSnapshotInsideManagerGate()
    {
        using var coordinator = Coordinator();
        var latest = Options(enabled: true, cron: "0 35 4 * * ?");
        var runtimeOptions = new DataUpdateRuntimeState(latest);
        using var manager = new DataUpdateScheduleManager(
            coordinator,
            runtimeOptions,
            new RuntimeConfigurationState(false, true, false));

        await manager.ApplyCurrentAsync();

        Assert.Equal(
            latest.Cron,
            coordinator.Get(DataUpdateScheduleManager.ScheduleName)!.Cron);
        Assert.Equal(latest, runtimeOptions.Value);
    }

    private static PluginScheduleCoordinator Coordinator() =>
        new(new PluginCatalog([new DataUpdateScheduleStub()]));

    private static DataUpdateOptions Options(bool enabled, string cron) =>
        new()
        {
            Enabled = enabled,
            Cron = cron,
            ManifestUrl = new Uri("https://updates.test.invalid/manifest.json"),
            AutoDownload = true,
            AutoImport = true,
            KeepVersions = 2,
            HttpTimeout = TimeSpan.FromSeconds(300),
        };

    private sealed class DataUpdateScheduleStub : IScheduledPlugin
    {
        public PluginDescriptor Descriptor { get; } = new(
            "animegonet-data-update",
            "Data update test",
            "1.0.0",
            PluginCategory.Schedule);

        public ValueTask<ScheduledResult> ExecuteAsync(
            ScheduledContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ScheduledResult(true, "ok", [], null));
    }
}
