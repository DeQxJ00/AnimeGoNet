using AnimeGo.Plugin.Abstractions;
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
    SafeFileLinker linker,
    TvShowNfoWriter nfoWriter,
    DirectoryDatabaseWriter directoryDatabaseWriter,
    DirectoryDatabaseIndexStore directoryDatabaseIndex,
    PluginCatalog plugins,
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
                if (claim.FileStrategy == "link_delete")
                {
                    var completed = await store.GetOperationsAsync(claim, cancellationToken).ConfigureAwait(false);
                    foreach (var operation in completed)
                    {
                        var file = claim.Files.Single(item => item.TaskFileId == operation.TaskFileId);
                        await linker.DeleteSourceAsync(new SafeFileLinkRequest(
                            claim.DownloadRootPath,
                            claim.SaveRootPath,
                            operation.SourcePath,
                            operation.TargetPath,
                            file.SizeBytes), cancellationToken).ConfigureAwait(false);
                    }
                }

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

            if (claim.FileStrategy is "move" or "wait_move")
            {
                await clients.ExecuteAsync(
                    claim.DownloaderId,
                    async (client, token) =>
                    {
                        await client.ConnectAsync(token).ConfigureAwait(false);
                        await client.PauseAsync([claim.InfoHash], token).ConfigureAwait(false);
                        return true;
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            var plans = new List<MediaOperationPlan>(claim.Files.Count);
            foreach (var file in claim.Files)
            {
                plans.Add(await PlanAsync(claim, file, cancellationToken).ConfigureAwait(false));
            }
            var operations = await store.EnsureOperationsAsync(
                claim, plans, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            foreach (var operation in operations.Where(operation => operation.State != "completed"))
            {
                var file = claim.Files.Single(file => file.TaskFileId == operation.TaskFileId);
                var bytesVerified = claim.FileStrategy is "link" or "link_delete"
                    ? (await linker.LinkAsync(new SafeFileLinkRequest(
                        claim.DownloadRootPath,
                        claim.SaveRootPath,
                        operation.SourcePath,
                        operation.TargetPath,
                        file.SizeBytes), cancellationToken).ConfigureAwait(false)).BytesVerified
                    : (await mover.MoveAsync(new SafeFileMoveRequest(
                        operation.OperationId,
                        claim.DownloadRootPath,
                        claim.SaveRootPath,
                        operation.SourcePath,
                        operation.TargetPath,
                        file.SizeBytes), cancellationToken).ConfigureAwait(false)).BytesVerified;
                await store.CompleteFileAsync(
                    claim, operation.OperationId, bytesVerified,
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

            foreach (var seasonGroup in claim.Files.GroupBy(file =>
                         (file.TmdbSeriesId, file.CanonicalSeriesName, file.SeasonNumber)))
            {
                var episodeSidecars = seasonGroup
                    .Where(file => file.Disposition == "episode" && file.AssociatedFileId is null)
                    .Select(file =>
                    {
                        var operation = operations.Single(item => item.TaskFileId == file.TaskFileId);
                        return new DirectoryDatabaseEpisodeWrite(
                            operation.TargetPath,
                            file.EpisodeNumber is > 0 ? 1 : 0,
                            file.EpisodeNumber ?? 0);
                    })
                    .ToArray();
                var directoryEntries = await directoryDatabaseWriter.WriteAsync(
                    new DirectoryDatabaseWriteRequest(
                        claim.SaveRootPath,
                        claim.InfoHash,
                        seasonGroup.Key.CanonicalSeriesName,
                        seasonGroup.Key.SeasonNumber,
                        episodeSidecars),
                    _timeProvider.GetUtcNow(),
                    cancellationToken).ConfigureAwait(false);
                await directoryDatabaseIndex.UpsertAsync(
                    directoryEntries,
                    _timeProvider.GetUtcNow(),
                    cancellationToken).ConfigureAwait(false);
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
            if (claim.Stage == MediaOrganizationStage.MoveFiles
                && claim.FileStrategy is "move" or "wait_move")
            {
                await BestEffortPauseAsync(claim, cancellationToken).ConfigureAwait(false);
            }

            await store.ReleaseAsync(
                claim, Classify(exception), _timeProvider.GetUtcNow() + RetryDelay,
                _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            return MediaOrganizationResult.RetryScheduled;
        }
    }

    private async ValueTask<MediaOperationPlan> PlanAsync(
        MediaOrganizationClaim claim,
        MediaOrganizationFile file,
        CancellationToken cancellationToken)
    {
        var sourceRelative = file.RelativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var rename = await plugins.Require<IRenamePlugin>("anime-library").RenameAsync(
            new RenameContext(
                file.RelativePath,
                file.CanonicalSeriesName,
                file.SeasonNumber,
                file.Disposition,
                file.EpisodeNumber,
                null,
                file.RenameSuffix,
                EmptyArguments),
            cancellationToken).ConfigureAwait(false);
        if (!rename.Matched || string.IsNullOrWhiteSpace(rename.RelativeTargetPath))
        {
            throw new MediaRenamePluginException(
                rename.Errors.Count > 0 ? rename.Errors[0].Code : "rename_no_match");
        }

        var source = PathBoundary.Combine(claim.DownloadRootPath, sourceRelative);
        var target = PathBoundary.Combine(claim.SaveRootPath, rename.RelativeTargetPath);
        return new MediaOperationPlan(file.TaskFileId, source, target);
    }

    private static readonly Dictionary<string, string> EmptyArguments =
        new(StringComparer.Ordinal);

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
        MediaRenamePluginException rename => rename.Code,
        _ => "media_organization_error",
    };
}

internal sealed class MediaRenamePluginException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
