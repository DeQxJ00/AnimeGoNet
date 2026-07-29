using AnimeGo.Plugin.Abstractions;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Feeds;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.App.Ingest;

public sealed record UnifiedIngestItemResult(
    string Status,
    string? IngestId,
    string? SourceProfileId,
    long? SourceProfileRevision,
    string? DownloaderId,
    string? TorrentUrlFingerprint,
    string? InfoHash,
    int? FileCount,
    IReadOnlyList<string> Errors)
{
    public bool Accepted => IngestId is not null;
}

public sealed class UnifiedIngestProcessor(
    SourceProfileStore profiles,
    IngestTaskStore tasks,
    ITorrentStagingService staging,
    AnimeGoOptions options,
    PluginCatalog plugins)
{
    public async Task<UnifiedIngestItemResult> ProcessAsync(
        string source,
        IngestItemCommand command,
        bool requireModernMetadata,
        CancellationToken cancellationToken = default) =>
        await ProcessCoreAsync(source, command, requireModernMetadata, null, cancellationToken).ConfigureAwait(false);

    public async Task<UnifiedIngestItemResult> ProcessRssWinnerAsync(
        string source,
        IngestItemCommand command,
        MikanRssWinnerLease winnerLease,
        CancellationToken cancellationToken = default) =>
        await ProcessCoreAsync(source, command, false, winnerLease, cancellationToken).ConfigureAwait(false);

    private async Task<UnifiedIngestItemResult> ProcessCoreAsync(
        string source,
        IngestItemCommand command,
        bool requireModernMetadata,
        MikanRssWinnerLease? winnerLease,
        CancellationToken cancellationToken)
    {
        var profileId = (source ?? string.Empty).Trim().ToLowerInvariant();
        var profile = await profiles.GetEnabledAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return Rejected(["no enabled source profile is configured"]);
        }

        var validation = await IngestCommandNormalizer.NormalizeAsync(
            plugins,
            profile.Adapter,
            command,
            requireModernMetadata,
            cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return Rejected(validation.Errors);
        }

        var normalized = validation.Item! with { Source = profile.Id };

        StagedTorrent? staged = null;
        var ownershipTransferred = false;
        try
        {
            staged = await staging.StageAsync(
                normalized.TorrentUrl,
                new TorrentSourcePolicy(profile.Id, profile.AllowedTorrentHosts),
                cancellationToken).ConfigureAwait(false);
            var expires = DateTimeOffset.UtcNow + options.TorrentFetch.StagingTtl;
            var task = winnerLease is null
                ? await tasks.AddStagedAsync(
                    normalized, profile, staged.Metadata, staged.StagingFileName,
                    expires, cancellationToken).ConfigureAwait(false)
                : await tasks.AddStagedForRssWinnerAsync(
                    normalized, profile, staged.Metadata, staged.StagingFileName,
                    expires, winnerLease, cancellationToken).ConfigureAwait(false);
            ownershipTransferred = true;
            return new UnifiedIngestItemResult(
                task.Status, task.Id, task.SourceProfileId, task.SourceProfileRevision,
                task.DownloaderId, normalized.TorrentUrlFingerprint, task.InfoHash, task.FileCount, []);
        }
        catch (TorrentStagingException exception)
        {
            return Rejected([$"torrent staging failed: {exception.Code}"]);
        }
        finally
        {
            if (!ownershipTransferred && staged is not null)
            {
                await staged.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static UnifiedIngestItemResult Rejected(IReadOnlyList<string> errors) =>
        new("rejected", null, null, null, null, null, null, null, errors);
}
