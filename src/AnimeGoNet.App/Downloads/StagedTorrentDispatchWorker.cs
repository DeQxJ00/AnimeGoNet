namespace AnimeGoNet.App.Downloads;

public sealed class StagedTorrentDispatchWorker(StagedTorrentDispatcher dispatcher) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            StagedDispatchResult result;
            try
            {
                result = await dispatcher.DispatchNextAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                continue;
            }

            var delay = result switch
            {
                StagedDispatchResult.NoWork => TimeSpan.FromSeconds(2),
                StagedDispatchResult.RetryScheduled => TimeSpan.FromSeconds(5),
                _ => TimeSpan.Zero,
            };
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
