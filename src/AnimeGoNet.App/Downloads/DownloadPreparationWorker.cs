namespace AnimeGoNet.App.Downloads;

public sealed class DownloadPreparationWorker(DownloadPreparationProcessor processor) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            DownloadPreparationResult result;
            try
            {
                result = await processor.RunOnceAsync(stoppingToken).ConfigureAwait(false);
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
                DownloadPreparationResult.NoWork => TimeSpan.FromSeconds(2),
                DownloadPreparationResult.RetryScheduled => TimeSpan.FromSeconds(5),
                _ => TimeSpan.Zero,
            };
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
