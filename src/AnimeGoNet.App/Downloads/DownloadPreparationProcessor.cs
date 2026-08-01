using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Data.Downloads;

namespace AnimeGoNet.App.Downloads;

public enum DownloadPreparationResult
{
    NoWork,
    Completed,
    SkippedDuplicate,
    RetryScheduled,
}

public sealed class DownloadPreparationProcessor(
    DownloadPreparationStore preparations,
    DownloadClientOperationCoordinator clients,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<DownloadPreparationResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var claim = await preparations.TryClaimNextAsync(
            _timeProvider.GetUtcNow(),
            LeaseDuration,
            cancellationToken).ConfigureAwait(false);
        if (claim is null)
        {
            return DownloadPreparationResult.NoWork;
        }

        try
        {
            var prepared = await clients.ExecuteAsync(
                claim.DownloaderId,
                (client, token) => PrepareClientAsync(client, claim, token),
                cancellationToken).ConfigureAwait(false);
            await preparations.CompleteAsync(
                claim,
                prepared.Assignments,
                prepared.DynamicTags,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            if (prepared.AllSkipped)
            {
                try
                {
                    await clients.ExecuteAsync(
                        claim.DownloaderId,
                        async (client, token) =>
                        {
                            await client.DeleteAsync([claim.InfoHash], deleteFiles: false, token).ConfigureAwait(false);
                            return true;
                        },
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Priorities are zero and the durable task is skipped. Cleanup can be retried separately.
                }

                return DownloadPreparationResult.SkippedDuplicate;
            }

            return DownloadPreparationResult.Completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await BestEffortPauseAsync(claim, cancellationToken).ConfigureAwait(false);
            await preparations.ReleaseAsync(
                claim,
                Classify(exception),
                _timeProvider.GetUtcNow() + RetryDelay,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            return DownloadPreparationResult.RetryScheduled;
        }
    }

    private async Task BestEffortPauseAsync(
        DownloadPreparationClaim claim,
        CancellationToken cancellationToken)
    {
        try
        {
            await clients.ExecuteAsync(
                claim.DownloaderId,
                async (client, token) =>
                {
                    await client.PauseAsync([claim.InfoHash], token).ConfigureAwait(false);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // The durable preparation lease remains retryable even when qB is temporarily unavailable.
        }
    }

    private static async Task<PreparedDownload> PrepareClientAsync(
        IDownloadClient client,
        DownloadPreparationClaim claim,
        CancellationToken cancellationToken)
    {
        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await client.PauseAsync([claim.InfoHash], cancellationToken).ConfigureAwait(false);
        var clientFiles = await client.ListFilesAsync(claim.InfoHash, cancellationToken).ConfigureAwait(false);
        if (clientFiles.Count == 0)
        {
            throw new PreparationFailureException("qbittorrent_metadata_pending");
        }

        if (clientFiles.Count != claim.Files.Count
            || clientFiles.Any(file => file.Index < 0 || file.SizeBytes < 0)
            || clientFiles.Select(file => file.Index).Distinct().Count() != clientFiles.Count)
        {
            throw new PreparationFailureException("download_file_manifest_mismatch");
        }

        Dictionary<string, DownloadFileSnapshot> byPath;
        try
        {
            byPath = clientFiles.ToDictionary(
                file => NormalizePath(file.RelativePath),
                StringComparer.Ordinal);
        }
        catch (ArgumentException)
        {
            throw new PreparationFailureException("download_file_manifest_mismatch");
        }

        var assignments = new List<DownloadFileAssignment>(claim.Files.Count);
        foreach (var file in claim.Files)
        {
            if (file.Disposition == "pending")
            {
                throw new PreparationFailureException("metadata_files_pending");
            }

            if (!byPath.TryGetValue(NormalizePath(file.RelativePath), out var clientFile)
                || clientFile.SizeBytes != file.SizeBytes)
            {
                throw new PreparationFailureException("download_file_manifest_mismatch");
            }

            var wanted = file.Disposition is not ("duplicate" or "ignored");
            assignments.Add(new DownloadFileAssignment(
                file.FileId,
                clientFile.Index,
                wanted ? 1 : 0,
                wanted));
        }

        var unwantedIndexes = assignments.Where(item => !item.Wanted).Select(item => item.DownloadFileIndex).ToArray();
        var wantedIndexes = assignments.Where(item => item.Wanted).Select(item => item.DownloadFileIndex).ToArray();
        var dynamicTags = await ApplyDynamicTagsAsync(
            client,
            claim,
            wantedIndexes.Length == 0,
            cancellationToken).ConfigureAwait(false);

        if (unwantedIndexes.Length > 0)
        {
            await client.SetFilePriorityAsync(claim.InfoHash, unwantedIndexes, 0, cancellationToken).ConfigureAwait(false);
        }

        if (wantedIndexes.Length > 0)
        {
            await client.SetFilePriorityAsync(claim.InfoHash, wantedIndexes, 1, cancellationToken).ConfigureAwait(false);
            await client.ResumeAsync([claim.InfoHash], cancellationToken).ConfigureAwait(false);
        }

        return new PreparedDownload(assignments, wantedIndexes.Length == 0, dynamicTags);
    }

    private static async Task<DownloadDynamicTagAssignment> ApplyDynamicTagsAsync(
        IDownloadClient client,
        DownloadPreparationClaim claim,
        bool allSkipped,
        CancellationToken cancellationToken)
    {
        if (claim.DynamicTagTemplate is null)
        {
            return new DownloadDynamicTagAssignment([], "not_configured", null);
        }

        if (allSkipped)
        {
            return new DownloadDynamicTagAssignment(
                [],
                "skipped",
                "dynamic_tag_all_files_skipped");
        }

        var rendered = DownloadDynamicTagTemplate.Render(
            claim.DynamicTagTemplate,
            claim.DynamicTagAirDate,
            claim.DynamicTagEpisodeNumber);
        if (!rendered.IsSuccess)
        {
            return new DownloadDynamicTagAssignment([], "skipped", rendered.FailureCode);
        }

        await client.AddTagsAsync(
            [claim.InfoHash],
            rendered.Tags,
            cancellationToken).ConfigureAwait(false);
        return new DownloadDynamicTagAssignment(rendered.Tags, "applied", null);
    }

    private static string NormalizePath(string value) => value.Replace('\\', '/');

    private static string Classify(Exception exception) => exception switch
    {
        PreparationFailureException failure => failure.Code,
        KeyNotFoundException => "downloader_unavailable",
        HttpRequestException => "qbittorrent_http_error",
        TaskCanceledException => "qbittorrent_timeout",
        _ => "download_preparation_error",
    };

    private sealed record PreparedDownload(
        IReadOnlyList<DownloadFileAssignment> Assignments,
        bool AllSkipped,
        DownloadDynamicTagAssignment DynamicTags);

    private sealed class PreparationFailureException(string code) : Exception
    {
        public string Code { get; } = code;
    }
}
