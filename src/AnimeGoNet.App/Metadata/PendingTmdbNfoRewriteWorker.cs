namespace AnimeGoNet.App.Metadata;

public sealed class PendingTmdbNfoRewriteWorker(
    PendingTmdbNfoRewriteProcessor processor) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await processor.RunOnceAsync(stoppingToken).ConfigureAwait(false);
                if (result is PendingTmdbNfoRewriteResult.NoWork
                    or PendingTmdbNfoRewriteResult.RetryScheduled)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(
                            result == PendingTmdbNfoRewriteResult.NoWork ? 2 : 5),
                        stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
