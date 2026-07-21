namespace AnimeGoNet.App.Metadata;

public sealed class EpisodeMetadataResolutionWorker(EpisodeMetadataResolutionProcessor processor) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = false;
            try
            {
                processed = await processor.RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // The active lease is recovered by MetadataResolutionStore after expiry.
            }

            if (processed)
            {
                await Task.Yield();
                continue;
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
        }
    }
}
