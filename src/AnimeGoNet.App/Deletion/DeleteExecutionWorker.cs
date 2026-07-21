namespace AnimeGoNet.App.Deletion;

public sealed class DeleteExecutionWorker(DeleteExecutionProcessor processor) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await processor.RunOnceAsync(stoppingToken).ConfigureAwait(false);
                if (result is DeleteExecutionResult.NoWork or DeleteExecutionResult.RetryScheduled)
                {
                    await Task.Delay(TimeSpan.FromSeconds(result == DeleteExecutionResult.NoWork ? 2 : 5), stoppingToken)
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
