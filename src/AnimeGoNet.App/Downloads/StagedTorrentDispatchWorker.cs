using AnimeGo.Plugin.Abstractions;

namespace AnimeGoNet.App.Downloads;

public sealed class StagedTorrentDispatchWorker(PluginCatalog plugins) : BackgroundService
{
    private static readonly Dictionary<string, string> EmptyArguments =
        new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var schedule = plugins.Require<IScheduledPlugin>("staged-torrent-dispatch");
        while (!stoppingToken.IsCancellationRequested)
        {
            ScheduledResult result;
            try
            {
                result = await schedule.ExecuteAsync(
                    new ScheduledContext(
                        "dispatch-next-staged",
                        DateTimeOffset.UtcNow,
                        EmptyArguments),
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                result = new ScheduledResult(
                    false,
                    null,
                    [new PluginOperationError("schedule_execution_failed", "Schedule execution failed.")],
                    TimeSpan.FromSeconds(5));
            }

            var delay = result.NextDelay ?? (result.Succeeded ? TimeSpan.Zero : TimeSpan.FromSeconds(5));
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
