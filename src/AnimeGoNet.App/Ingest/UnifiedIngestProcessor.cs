using AnimeGo.Plugin.Abstractions;
using AnimeGoNet.App.Configuration;
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
    PluginCatalog plugins,
    LegacyDownloaderMigrationState legacyMigration)
{
    public async Task<UnifiedIngestItemResult> ProcessAsync(
        string source,
        IngestItemCommand command,
        bool requireModernMetadata,
        CancellationToken cancellationToken = default) =>
        await ProcessCoreAsync(
            source,
            command,
            requireModernMetadata,
            null,
            null,
            cancellationToken).ConfigureAwait(false);

    public async Task<UnifiedIngestItemResult> ProcessRssWinnerAsync(
        SourceProfileRecord sourceProfileSnapshot,
        IngestItemCommand command,
        MikanRssWinnerLease winnerLease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceProfileSnapshot);
        return await ProcessCoreAsync(
            sourceProfileSnapshot.Id,
            command,
            false,
            winnerLease,
            sourceProfileSnapshot,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<UnifiedIngestItemResult> ProcessCoreAsync(
        string source,
        IngestItemCommand command,
        bool requireModernMetadata,
        MikanRssWinnerLease? winnerLease,
        SourceProfileRecord? sourceProfileSnapshot,
        CancellationToken cancellationToken)
    {
        if (legacyMigration.BlockingDiagnostic is { } diagnostic)
        {
            return Rejected([$"{diagnostic.Code}: {diagnostic.Message}"]);
        }

        var profileId = (source ?? string.Empty).Trim().ToLowerInvariant();
        if (sourceProfileSnapshot is not null
            && !string.Equals(sourceProfileSnapshot.Id, profileId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Source profile snapshot does not match the requested source.",
                nameof(sourceProfileSnapshot));
        }
        var profile = sourceProfileSnapshot
            ?? await profiles.GetEnabledAsync(profileId, cancellationToken).ConfigureAwait(false);
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
        var torrentRequestUrl = string.Equals(
                profile.Adapter,
                "mikan",
                StringComparison.OrdinalIgnoreCase)
            ? MikanEndpointRewriter.Rewrite(normalized.TorrentUrl, options.Metadata.Mikan)
            : normalized.TorrentUrl;
        var allowedHosts = profile.AllowedTorrentHosts
            .Append(torrentRequestUrl.IdnHost)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var trustedPrivateHosts = string.Equals(
                profile.Adapter,
                "mikan",
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                torrentRequestUrl.IdnHost,
                options.Metadata.Mikan.BaseUrl.IdnHost,
                StringComparison.OrdinalIgnoreCase)
                ? new[] { torrentRequestUrl.IdnHost }
                : [];

        StagedTorrent? staged = null;
        var ownershipTransferred = false;
        try
        {
            staged = await staging.StageAsync(
                torrentRequestUrl,
                new TorrentSourcePolicy(
                    profile.Id,
                    allowedHosts,
                    profile.MikanIdentityCookie,
                    trustedPrivateHosts),
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
