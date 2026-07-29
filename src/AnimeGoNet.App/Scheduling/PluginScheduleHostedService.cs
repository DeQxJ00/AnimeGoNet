namespace AnimeGoNet.App.Scheduling;

public sealed class PluginScheduleHostedService(PluginScheduleCoordinator coordinator)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        coordinator.RunAsync(stoppingToken);
}
