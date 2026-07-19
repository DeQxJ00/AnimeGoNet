namespace AnimeGoNet.App.Downloads;

public sealed class DownloadSnapshotWorker(DownloadSnapshotSynchronizer synchronizer) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int activeJobs;
            try
            {
                activeJobs = await synchronizer.SyncOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                activeJobs = 0;
            }

            var delay = activeJobs > 0 ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(10);
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }
}
