namespace AnimeGoNet.App.Library;

public sealed class MediaOrganizationWorker(MediaOrganizationProcessor processor) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await processor.RunOnceAsync(stoppingToken).ConfigureAwait(false);
                if (result is MediaOrganizationResult.NoWork or MediaOrganizationResult.RetryScheduled)
                {
                    await Task.Delay(TimeSpan.FromSeconds(result == MediaOrganizationResult.NoWork ? 2 : 5), stoppingToken)
                        .ConfigureAwait(false);
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
