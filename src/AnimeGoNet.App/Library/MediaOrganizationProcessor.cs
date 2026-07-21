using AnimeGoNet.App.Downloads;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Library;
using AnimeGoNet.Data.Library;

namespace AnimeGoNet.App.Library;

public enum MediaOrganizationResult
{
    NoWork,
    FilesCompleted,
    CleanupCompleted,
    RetryScheduled,
}

public sealed class MediaOrganizationProcessor(
    MediaOrganizationStore store,
    DownloadClientOperationCoordinator clients,
    SafeFileMover mover,
    TvShowNfoWriter nfoWriter,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<MediaOrganizationResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var claim = await store.TryClaimNextAsync(
            _timeProvider.GetUtcNow(), LeaseDuration, cancellationToken).ConfigureAwait(false);
        if (claim is null)
        {
            return MediaOrganizationResult.NoWork;
        }

        try
        {
            if (claim.Stage == MediaOrganizationStage.CleanupDownloader)
            {
                await clients.ExecuteAsync(
                    claim.DownloaderId,
                    async (client, token) =>
                    {
                        await client.ConnectAsync(token).ConfigureAwait(false);
                        await client.DeleteAsync([claim.InfoHash], deleteFiles: false, token).ConfigureAwait(false);
                        return true;
                    },
                    cancellationToken).ConfigureAwait(false);
                await store.CompleteCleanupAsync(claim, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                return MediaOrganizationResult.CleanupCompleted;
            }

            await clients.ExecuteAsync(
                claim.DownloaderId,
                async (client, token) =>
                {
                    await client.ConnectAsync(token).ConfigureAwait(false);
                    await client.PauseAsync([claim.InfoHash], token).ConfigureAwait(false);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);

            var plans = claim.Files.Select(file => Plan(claim, file)).ToArray();
            var operations = await store.EnsureOperationsAsync(
                claim, plans, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            foreach (var operation in operations.Where(operation => operation.State != "completed"))
            {
                var file = claim.Files.Single(file => file.TaskFileId == operation.TaskFileId);
                var result = await mover.MoveAsync(new SafeFileMoveRequest(
                    operation.OperationId,
                    claim.DownloadRootPath,
                    claim.SaveRootPath,
                    operation.SourcePath,
                    operation.TargetPath,
                    file.SizeBytes), cancellationToken).ConfigureAwait(false);
                await store.CompleteFileAsync(
                    claim, operation.OperationId, result.BytesVerified,
                    _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            }

            foreach (var series in claim.Files
                         .GroupBy(file => (file.TmdbSeriesId, file.CanonicalSeriesName))
                         .Select(group => group.Key))
            {
                await nfoWriter.WriteAsync(
                    claim.SaveRootPath, series.CanonicalSeriesName, series.TmdbSeriesId,
                    claim.BangumiSubjectId, cancellationToken).ConfigureAwait(false);
            }

            await store.CompleteMovesAsync(claim, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            return MediaOrganizationResult.FilesCompleted;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (claim.Stage == MediaOrganizationStage.MoveFiles)
            {
                await BestEffortPauseAsync(claim, cancellationToken).ConfigureAwait(false);
            }

            await store.ReleaseAsync(
                claim, Classify(exception), _timeProvider.GetUtcNow() + RetryDelay,
                _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            return MediaOrganizationResult.RetryScheduled;
        }
    }

    private static MediaOperationPlan Plan(MediaOrganizationClaim claim, MediaOrganizationFile file)
    {
        var sourceRelative = file.RelativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var targetRelative = MediaPathPlanner.PlanRelativePath(new MediaPathInput(
            file.CanonicalSeriesName, file.SeasonNumber, file.Disposition,
            file.EpisodeNumber, file.RelativePath, file.RenameSuffix));
        var source = PathBoundary.Combine(claim.DownloadRootPath, sourceRelative);
        var target = PathBoundary.Combine(claim.SaveRootPath, targetRelative);
        return new MediaOperationPlan(file.TaskFileId, source, target);
    }

    private async Task BestEffortPauseAsync(MediaOrganizationClaim claim, CancellationToken cancellationToken)
    {
        try
        {
            await clients.ExecuteAsync(
                claim.DownloaderId,
                async (client, token) =>
                {
                    await client.PauseAsync([claim.InfoHash], token).ConfigureAwait(false);
                    return true;
                }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // The durable lease still records a retry even while the downloader is unavailable.
        }
    }

    private static string Classify(Exception exception) => exception switch
    {
        SafeFileMoveException move => move.Code,
        KeyNotFoundException => "downloader_unavailable",
        HttpRequestException => "qbittorrent_http_error",
        TaskCanceledException => "organization_timeout",
        UnauthorizedAccessException => "file_access_denied",
        IOException => "file_move_io_error",
        _ => "media_organization_error",
    };
}
