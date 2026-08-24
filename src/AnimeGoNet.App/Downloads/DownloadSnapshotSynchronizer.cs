using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Data.Downloads;

namespace AnimeGoNet.App.Downloads;

public sealed class DownloadSnapshotSynchronizer(
    DownloadJobStore jobs,
    DownloadClientOperationCoordinator clients,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<int> SyncOnceAsync(CancellationToken cancellationToken = default)
    {
        var activeJobs = await jobs.CountActiveAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(clients.InstanceIds.Select(
            instanceId => SyncInstanceAsync(instanceId, cancellationToken))).ConfigureAwait(false);
        return activeJobs;
    }

    private async Task SyncInstanceAsync(string instanceId, CancellationToken cancellationToken)
    {
        try
        {
            var snapshots = await clients.ExecuteAsync(
                instanceId,
                async (client, token) =>
                {
                    await client.ConnectAsync(token).ConfigureAwait(false);
                    return await client.ListAsync(token).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
            var sync = await jobs.ApplyInstanceSnapshotAsync(
                instanceId,
                snapshots,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            foreach (var candidate in sync.DeadTorrentCandidates)
            {
                try
                {
                    await clients.ExecuteAsync(
                        instanceId,
                        async (client, token) =>
                        {
                            await client.PauseAsync([candidate.InfoHash], token).ConfigureAwait(false);
                            return true;
                        },
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    await jobs.RecordControlFailureAsync(
                        candidate.JobId,
                        "dead_torrent_pause",
                        Classify(exception),
                        _timeProvider.GetUtcNow(),
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await jobs.MarkInstanceUnavailableAsync(
                instanceId,
                Classify(exception),
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static string Classify(Exception exception) => exception switch
    {
        DownloadClientCircuitOpenException => "qbittorrent_circuit_open",
        KeyNotFoundException => "downloader_unavailable",
        HttpRequestException => "qbittorrent_http_error",
        TaskCanceledException => "qbittorrent_timeout",
        _ => "download_sync_error",
    };
}
