using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Data.Ingest;

namespace AnimeGoNet.App.Downloads;

public enum StagedDispatchResult
{
    NoWork,
    Completed,
    RetryScheduled,
}

public sealed class StagedTorrentDispatcher(
    IngestTaskStore tasks,
    ITorrentStagingService staging,
    DownloadClientOperationCoordinator clients,
    AnimeGoOptions options,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ConfirmationDelay = TimeSpan.FromMilliseconds(200);
    private const int ConfirmationAttempts = 5;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<StagedDispatchResult> DispatchNextAsync(CancellationToken cancellationToken = default)
    {
        var claim = await tasks.TryClaimNextStagedAsync(
            _timeProvider.GetUtcNow(),
            LeaseDuration,
            cancellationToken).ConfigureAwait(false);
        if (claim is null)
        {
            return StagedDispatchResult.NoWork;
        }

        try
        {
            if (!options.Downloaders.TryGetValue(claim.DownloaderId, out var downloader)
                || !downloader.Enabled
                || !string.Equals(downloader.Type, DownloaderTypes.Qbittorrent, StringComparison.OrdinalIgnoreCase))
            {
                throw new DispatchFailureException("downloader_unavailable");
            }

            var snapshot = await clients.ExecuteAsync(
                claim.DownloaderId,
                (client, token) => DispatchToClientAsync(client, claim, downloader, token),
                cancellationToken).ConfigureAwait(false);

            if (snapshot is null)
            {
                throw new DispatchFailureException("qbittorrent_confirmation_missing");
            }

            await tasks.CompleteDispatchAsync(
                claim,
                snapshot,
                downloader.DownloadPath,
                options.Paths.SavePath,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            try
            {
                await staging.DeleteAsync(claim.StagingFileName, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // The lifecycle transaction is authoritative; TTL cleanup reclaims an orphaned file.
            }
            catch (UnauthorizedAccessException)
            {
                // The lifecycle transaction is authoritative; TTL cleanup reclaims an orphaned file.
            }

            return StagedDispatchResult.Completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failureCode = Classify(exception);
            await tasks.ReleaseDispatchAsync(
                claim,
                failureCode,
                _timeProvider.GetUtcNow() + RetryDelay,
                cancellationToken).ConfigureAwait(false);
            return StagedDispatchResult.RetryScheduled;
        }
    }

    private async Task<DownloadTaskSnapshot?> DispatchToClientAsync(
        IDownloadClient client,
        ClaimedStagedTorrentRecord claim,
        QbittorrentInstanceOptions downloader,
        CancellationToken cancellationToken)
    {
        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = FindByHash(await client.ListAsync(cancellationToken).ConfigureAwait(false), claim.InfoHash);
        if (snapshot is not null)
        {
            await client.PauseAsync([claim.InfoHash], cancellationToken).ConfigureAwait(false);
            return AsPaused(snapshot);
        }

        await using var torrent = staging.OpenRead(claim.StagingFileName);
        await client.AddTorrentAsync(
            new AddTorrentCommand(
                torrent,
                claim.StagingFileName,
                downloader.DownloadPath,
                Rename: null,
                Category: claim.Category,
                Tags: new[] { "animegonet", claim.SourceId, claim.FileStrategy }
                    .Concat(claim.Tags)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StartPaused: true,
                SeedingTimeMinutes: claim.SeedingTimeMinutes),
            cancellationToken).ConfigureAwait(false);
        snapshot = await ConfirmAsync(client, claim.InfoHash, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return null;
        }

        await client.PauseAsync([claim.InfoHash], cancellationToken).ConfigureAwait(false);
        return AsPaused(snapshot);
    }

    private async Task<DownloadTaskSnapshot?> ConfirmAsync(
        IDownloadClient client,
        string infoHash,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < ConfirmationAttempts; attempt++)
        {
            var snapshot = FindByHash(await client.ListAsync(cancellationToken).ConfigureAwait(false), infoHash);
            if (snapshot is not null)
            {
                return snapshot;
            }

            if (attempt + 1 < ConfirmationAttempts)
            {
                await Task.Delay(ConfirmationDelay, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
    }

    private static DownloadTaskSnapshot? FindByHash(
        IReadOnlyList<DownloadTaskSnapshot> snapshots,
        string infoHash) =>
        snapshots.FirstOrDefault(snapshot => string.Equals(snapshot.Hash, infoHash, StringComparison.OrdinalIgnoreCase));

    private static DownloadTaskSnapshot AsPaused(DownloadTaskSnapshot snapshot) => snapshot with
    {
        State = DownloadTaskState.Paused,
        DownloadSpeedBytesPerSecond = 0,
        EtaSeconds = null,
    };

    private static string Classify(Exception exception) => exception switch
    {
        DispatchFailureException dispatch => dispatch.Code,
        KeyNotFoundException => "downloader_unavailable",
        FileNotFoundException => "staging_file_missing",
        UnauthorizedAccessException => "staging_file_unreadable",
        HttpRequestException => "qbittorrent_http_error",
        TaskCanceledException => "qbittorrent_timeout",
        _ => "download_dispatch_error",
    };

    private sealed class DispatchFailureException(string code) : Exception
    {
        public string Code { get; } = code;
    }
}
