namespace AnimeGoNet.App.Scheduling;

public sealed class PluginScheduleHostedService(
    PluginScheduleCoordinator coordinator,
    DataUpdateScheduleManager dataUpdateSchedules,
    SourceRssScheduleManager sourceRssSchedules,
    AnimeGoNet.Core.Configuration.AnimeGoOptions options)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await coordinator.AddAsync(
            new PluginScheduleRegistration(
                "refresh-database",
                "refresh-directory-database",
                options.Schedule.RefreshDatabaseCron,
                StartRun: false),
            stoppingToken).ConfigureAwait(false);
        await dataUpdateSchedules.ApplyCurrentAsync(stoppingToken).ConfigureAwait(false);
        await sourceRssSchedules.ApplyAllAsync(stoppingToken).ConfigureAwait(false);
        await coordinator.RunAsync(stoppingToken).ConfigureAwait(false);
    }
}
