using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Scheduling;

public sealed class PluginScheduleHostedService(
    PluginScheduleCoordinator coordinator,
    AnimeGoOptions options)
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
        if (options.DataUpdate.Enabled)
        {
            await coordinator.AddAsync(
                new PluginScheduleRegistration(
                    "animegonet-data-update",
                    "animegonet-data-update",
                    options.DataUpdate.Cron,
                    StartRun: false),
                stoppingToken).ConfigureAwait(false);
        }
        await coordinator.RunAsync(stoppingToken).ConfigureAwait(false);
    }
}
