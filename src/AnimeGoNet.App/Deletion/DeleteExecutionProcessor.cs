using AnimeGoNet.App.Downloads;
using AnimeGoNet.Data.Deletion;

namespace AnimeGoNet.App.Deletion;

public enum DeleteExecutionResult
{
    NoWork,
    Completed,
    RetryScheduled,
}

public sealed class DeleteExecutionProcessor(
    DeleteExecutionStore store,
    DownloadClientOperationCoordinator clients,
    SafeFileDeleter fileDeleter,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<DeleteExecutionResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var claim = await store.TryClaimNextAsync(
            _timeProvider.GetUtcNow(), LeaseDuration, cancellationToken).ConfigureAwait(false);
        if (claim is null)
        {
            return DeleteExecutionResult.NoWork;
        }

        foreach (var item in claim.Items)
        {
            try
            {
                await ExecuteItemAsync(claim, item, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await store.ReleaseAsync(
                    claim, item, Classify(exception), _timeProvider.GetUtcNow() + RetryDelay,
                    cancellationToken).ConfigureAwait(false);
                return DeleteExecutionResult.RetryScheduled;
            }
        }

        await store.CompleteAsync(claim, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return DeleteExecutionResult.Completed;
    }

    private async Task ExecuteItemAsync(
        DeleteExecutionClaim claim,
        DeleteExecutionItem item,
        CancellationToken cancellationToken)
    {
        switch (item.ItemKind)
        {
            case DeleteItemKinds.DownloaderTask:
                if (string.IsNullOrWhiteSpace(item.DownloaderId))
                {
                    throw new InvalidOperationException("Delete downloader target has no instance id.");
                }

                await clients.ExecuteAsync(
                    item.DownloaderId,
                    async (client, token) =>
                    {
                        await client.ConnectAsync(token).ConfigureAwait(false);
                        await client.DeleteAsync([item.TargetKey], deleteFiles: false, token).ConfigureAwait(false);
                        return true;
                    }, cancellationToken).ConfigureAwait(false);
                await store.CompleteItemAsync(
                    claim, item, true, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                break;
            case DeleteItemKinds.SourceFile:
            case DeleteItemKinds.MediaFile:
                if (string.IsNullOrWhiteSpace(item.RootPath))
                {
                    throw new SafeFileDeleteException("delete_root_missing", "Delete target has no captured root.");
                }

                var existed = await fileDeleter.DeleteAsync(
                    item.RootPath, item.TargetKey, cancellationToken).ConfigureAwait(false);
                await store.CompleteItemAsync(
                    claim, item, existed, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                break;
            case DeleteItemKinds.BusinessRecord:
                await store.CompleteBusinessRecordAsync(
                    claim, item, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new InvalidOperationException("Delete item kind is unsupported.");
        }
    }

    private static string Classify(Exception exception) => exception switch
    {
        SafeFileDeleteException file => file.Code,
        KeyNotFoundException => "downloader_unavailable",
        HttpRequestException => "qbittorrent_http_error",
        TaskCanceledException => "delete_execution_timeout",
        UnauthorizedAccessException => "delete_access_denied",
        IOException => "delete_file_io_error",
        _ => "delete_execution_error",
    };
}
