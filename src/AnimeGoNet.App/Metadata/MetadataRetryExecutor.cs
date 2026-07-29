namespace AnimeGoNet.App.Metadata;

internal static class MetadataRetryExecutor
{
    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TimeSpan attemptTimeout,
        int retryCount,
        TimeSpan retryDelay,
        Func<Exception, bool> isServiceFailure,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(isServiceFailure);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var timeout =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(attemptTimeout);
                return await operation(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
                when (attempt < retryCount
                    && IsRetryable(exception, isServiceFailure))
            {
                if (retryDelay > TimeSpan.Zero)
                {
                    await Task.Delay(retryDelay, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
    }

    private static bool IsRetryable(
        Exception exception,
        Func<Exception, bool> isServiceFailure) =>
        exception is HttpRequestException or OperationCanceledException
        || isServiceFailure(exception);
}
