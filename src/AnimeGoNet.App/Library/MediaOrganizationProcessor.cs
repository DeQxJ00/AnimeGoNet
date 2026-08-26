using AnimeGo.Plugin.Abstractions;
using AnimeGoNet.App.Downloads;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Diagnostics;
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
    MovieNfoWriter movieNfoWriter,
    DirectoryDatabaseWriter directoryDatabaseWriter,
    DirectoryDatabaseIndexStore directoryDatabaseIndex,
    PluginCatalog plugins,
    AnimeGoOptions options,
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
                var cleanupUnits = claim.FileStrategy == "link_delete" ? claim.Files.Count + 1 : 1;
                var completedCleanupUnits = 0;
                await store.UpdateProgressAsync(
                    claim,
                    MediaOrganizationPhases.CleanupDownloader,
                    completedCleanupUnits,
                    cleanupUnits,
                    _timeProvider.GetUtcNow(),
                    cancellationToken).ConfigureAwait(false);
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
                        completedCleanupUnits++;
                        await store.UpdateProgressAsync(
                            claim,
                            MediaOrganizationPhases.CleanupDownloader,
                            completedCleanupUnits,
                            cleanupUnits,
                            _timeProvider.GetUtcNow(),
                            cancellationToken).ConfigureAwait(false);
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
                await store.UpdateProgressAsync(
                    claim,
                    MediaOrganizationPhases.CleanupDownloader,
                    cleanupUnits,
                    cleanupUnits,
                    _timeProvider.GetUtcNow(),
                    cancellationToken).ConfigureAwait(false);
                await store.CompleteCleanupAsync(claim, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                return MediaOrganizationResult.CleanupCompleted;
            }

            if (!claim.IsOtherReadaptation
                && claim.FileStrategy is "move" or "wait_move")
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
            await store.UpdateProgressAsync(
                claim,
                MediaOrganizationPhases.RenamePlanning,
                0,
                claim.Files.Count,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            foreach (var file in claim.Files)
            {
                plans.Add(await PlanAsync(claim, file, cancellationToken).ConfigureAwait(false));
                await store.UpdateProgressAsync(
                    claim,
                    MediaOrganizationPhases.RenamePlanning,
                    plans.Count,
                    claim.Files.Count,
                    _timeProvider.GetUtcNow(),
                    cancellationToken).ConfigureAwait(false);
            }
            var operations = await store.EnsureOperationsAsync(
                claim, plans, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            var filesById = claim.Files.ToDictionary(file => file.TaskFileId, StringComparer.Ordinal);
            await ProcessOperationsAsync(
                claim,
                operations.Where(operation => filesById[operation.TaskFileId].AssociatedFileId is null).ToArray(),
                filesById,
                MediaOrganizationPhases.MediaTransfer,
                cancellationToken).ConfigureAwait(false);
            await ProcessOperationsAsync(
                claim,
                operations.Where(operation => filesById[operation.TaskFileId].AssociatedFileId is not null).ToArray(),
                filesById,
                MediaOrganizationPhases.SubtitleTransfer,
                cancellationToken).ConfigureAwait(false);

            var movieFiles = claim.Files.Where(file => file.MediaType == "movie").ToArray();
            var tvFiles = claim.Files.Where(file => file.MediaType != "movie").ToArray();
            if (movieFiles.Length > 0)
            {
                var movieGroups = movieFiles
                    .GroupBy(file => (
                        MovieId: file.TmdbMovieId!.Value,
                        Title: file.CanonicalSeriesName,
                        OriginalTitle: file.OriginalMovieTitle ?? file.CanonicalSeriesName,
                        file.MovieReleaseDate))
                    .Select(group => group.Key)
                    .ToArray();
                await store.UpdateProgressAsync(
                    claim,
                    MediaOrganizationPhases.NfoWrite,
                    0,
                    movieGroups.Length,
                    _timeProvider.GetUtcNow(),
                    cancellationToken).ConfigureAwait(false);
                for (var index = 0; index < movieGroups.Length; index++)
                {
                    var movie = movieGroups[index];
                    await movieNfoWriter.WriteAsync(
                        options.Paths.EffectiveMovieSavePath,
                        new AnimeGoNet.Core.Metadata.TmdbMovie(
                            movie.MovieId,
                            movie.Title,
                            movie.OriginalTitle,
                            movie.MovieReleaseDate),
                        claim.BangumiSubjectId,
                        cancellationToken).ConfigureAwait(false);
                    await store.UpdateProgressAsync(
                        claim,
                        MediaOrganizationPhases.NfoWrite,
                        index + 1,
                        movieGroups.Length,
                        _timeProvider.GetUtcNow(),
                        cancellationToken).ConfigureAwait(false);
                }
            }
            if (tvFiles.Length > 0)
            {
                var seriesGroups = tvFiles
                    .GroupBy(file => (file.TmdbSeriesId, file.CanonicalSeriesName))
                    .Select(group => group.Key)
                    .ToArray();
                await store.UpdateProgressAsync(
                    claim,
                    MediaOrganizationPhases.NfoWrite,
                    0,
                    seriesGroups.Length,
                    _timeProvider.GetUtcNow(),
                    cancellationToken).ConfigureAwait(false);
                for (var index = 0; index < seriesGroups.Length; index++)
                {
                    var series = seriesGroups[index];
                    await nfoWriter.WriteAsync(
                        claim.SaveRootPath, series.CanonicalSeriesName, series.TmdbSeriesId,
                        claim.BangumiSubjectId, cancellationToken).ConfigureAwait(false);
                    await store.UpdateProgressAsync(
                        claim,
                        MediaOrganizationPhases.NfoWrite,
                        index + 1,
                        seriesGroups.Length,
                        _timeProvider.GetUtcNow(),
                        cancellationToken).ConfigureAwait(false);
                }

                var seasonGroups = tvFiles.GroupBy(file =>
                        (file.TmdbSeriesId, file.CanonicalSeriesName, file.SeasonNumber))
                    .ToArray();
                await store.UpdateProgressAsync(
                    claim,
                    MediaOrganizationPhases.DirectoryIndex,
                    0,
                    seasonGroups.Length,
                    _timeProvider.GetUtcNow(),
                    cancellationToken).ConfigureAwait(false);
                for (var index = 0; index < seasonGroups.Length; index++)
                {
                    var seasonGroup = seasonGroups[index];
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
                    await store.UpdateProgressAsync(
                        claim,
                        MediaOrganizationPhases.DirectoryIndex,
                        index + 1,
                        seasonGroups.Length,
                        _timeProvider.GetUtcNow(),
                        cancellationToken).ConfigureAwait(false);
                }
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
                && !claim.IsOtherReadaptation
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

    private async Task ProcessOperationsAsync(
        MediaOrganizationClaim claim,
        MediaOperationRecord[] operations,
        Dictionary<string, MediaOrganizationFile> filesById,
        string phase,
        CancellationToken cancellationToken)
    {
        if (operations.Length == 0)
        {
            return;
        }

        var completedUnits = operations.Count(operation => operation.State == "completed");
        await store.UpdateProgressAsync(
            claim,
            phase,
            completedUnits,
            operations.Length,
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        foreach (var operation in operations.Where(operation => operation.State != "completed"))
        {
            var file = filesById[operation.TaskFileId];
            var sourceRoot = file.SourceOverridePath is null
                ? claim.DownloadRootPath
                : claim.SaveRootPath;
            var targetRoot = file.MediaType == "movie"
                ? options.Paths.EffectiveMovieSavePath
                : claim.SaveRootPath;
            var sourcePath = file.SourceOverridePath is null
                ? ResolvePortableDownloaderPath(sourceRoot, operation.SourcePath)
                : operation.SourcePath;
            var samePath = string.Equals(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(operation.TargetPath),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
            long bytesVerified;
            if (samePath)
            {
                if (!File.Exists(sourcePath)
                    || new FileInfo(sourcePath).Length != file.SizeBytes)
                {
                    throw new SafeFileMoveException(
                        "readaptation_source_invalid",
                        "Other readaptation source file is missing or has an unexpected size.");
                }

                bytesVerified = file.SizeBytes;
            }
            else
            {
                bytesVerified = file.SourceOverridePath is null
                    && claim.FileStrategy is ("link" or "link_delete")
                    ? (await linker.LinkAsync(new SafeFileLinkRequest(
                        sourceRoot,
                        targetRoot,
                        sourcePath,
                        operation.TargetPath,
                        file.SizeBytes), cancellationToken).ConfigureAwait(false)).BytesVerified
                    : (await mover.MoveAsync(new SafeFileMoveRequest(
                        operation.OperationId,
                        sourceRoot,
                        targetRoot,
                        sourcePath,
                        operation.TargetPath,
                        file.SizeBytes,
                        ForceCopyAndVerify: file.PreserveSource,
                        PreserveSource: file.PreserveSource), cancellationToken).ConfigureAwait(false)).BytesVerified;
            }
            await store.CompleteFileAsync(
                claim,
                operation.OperationId,
                bytesVerified,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            completedUnits++;
            await store.UpdateProgressAsync(
                claim,
                phase,
                completedUnits,
                operations.Length,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static string ResolvePortableDownloaderPath(string sourceRoot, string sourcePath)
    {
        if (File.Exists(sourcePath))
        {
            return sourcePath;
        }

        try
        {
            var relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
            var portablePath = PortablePathNormalizer.NormalizeRelativePathForComparison(relativePath)
                .Replace('/', Path.DirectorySeparatorChar);
            var candidate = PathBoundary.Combine(sourceRoot, portablePath);
            return File.Exists(candidate) ? candidate : sourcePath;
        }
        catch (ArgumentException)
        {
            return sourcePath;
        }
    }

    private async ValueTask<MediaOperationPlan> PlanAsync(
        MediaOrganizationClaim claim,
        MediaOrganizationFile file,
        CancellationToken cancellationToken)
    {
        var sourceRelative = file.RelativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (file.MediaType == "movie")
        {
            if (file.TmdbMovieId is null or <= 0)
            {
                throw new MediaRenamePluginException("movie_tmdb_identity_missing");
            }

            var movieSource = file.SourceOverridePath
                ?? PathBoundary.Combine(claim.DownloadRootPath, sourceRelative);
            var relativeTarget = MoviePathPlanner.PlanRelativePath(new MoviePathInput(
                file.CanonicalSeriesName,
                file.MovieReleaseDate,
                file.RelativePath,
                file.RenameSuffix));
            return new MediaOperationPlan(
                file.TaskFileId,
                movieSource,
                PathBoundary.Combine(options.Paths.EffectiveMovieSavePath, relativeTarget));
        }

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

        var source = file.SourceOverridePath
            ?? PathBoundary.Combine(claim.DownloadRootPath, sourceRelative);
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
    public string Code { get; } = StableErrorCode.Require(code, nameof(code));
}
