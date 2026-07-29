using System.Globalization;
using AnimeGo.Plugin.Abstractions;
using AnimeGoNet.App.Configuration;
using AnimeGoNet.App.Downloads;
using AnimeGoNet.App.DataUpdate;
using AnimeGoNet.App.Feeds;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Data.Library;
using AnimeGoNet.Data.DataUpdate;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.App.Plugins;

internal sealed class MikanRssFeedPlugin(RssFeedReader reader) : IFeedPlugin
{
    public PluginDescriptor Descriptor { get; } =
        new("mikan-rss", "Mikan RSS feed", "1.0.0", PluginCategory.Feed, 100);

    public async ValueTask<FeedResult> FetchAsync(
        FeedContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var feed = await reader
                .ParseUrlAsync(
                    context.FeedUrl,
                    context.SourceProfileId,
                    cancellationToken)
                .ConfigureAwait(false);
            var sourceWorkId = feed.MikanId?.ToString(CultureInfo.InvariantCulture);
            return new FeedResult(
                feed.Items.Select(item => new FeedItem(
                    item.Title,
                    item.TorrentUrl,
                    item.MikanUrl,
                    null,
                    sourceWorkId,
                    item.ContentType,
                    item.Length,
                    item.PublishedDate,
                    MikanPublishedAtParser.Parse(item.PublishedDate))).ToArray(),
                [],
                feed.MikanId is null
                    ? EmptyMetadata
                    : new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["mikanid"] = sourceWorkId!,
                    });
        }
        catch (RssFeedException exception)
        {
            return new FeedResult(
                [],
                [new PluginOperationError(exception.Code, exception.Message)],
                EmptyMetadata);
        }
    }

    private static readonly Dictionary<string, string> EmptyMetadata =
        new(StringComparer.Ordinal);
}

internal sealed class MikanToolFilterPlugin(
    SourceProfileStore profiles,
    MikanLegacyFilterProcessor processor) : IFeedFilterPlugin
{
    public PluginDescriptor Descriptor { get; } =
        new("mikan-tool", "MikanTool compatibility filter", "1.0.0", PluginCategory.Filter, 100);

    public async ValueTask<FilterResult> FilterAsync(
        FilterContext context,
        CancellationToken cancellationToken)
    {
        var profileId = context.SourceProfileId.Trim().ToLowerInvariant();
        bool rssFilterEnabled;
        if (context.SourceProfileSnapshot is { } profileSnapshot)
        {
            if (profileSnapshot.Revision < 1)
            {
                return Failure(
                    "rss_source_profile_snapshot_invalid",
                    "RSS source profile snapshot revision is invalid.");
            }
            rssFilterEnabled = profileSnapshot.RssFilterEnabled;
        }
        else
        {
            var profile = await profiles.GetEnabledAsync(
                profileId,
                cancellationToken).ConfigureAwait(false);
            if (profile is null)
            {
                return Failure("rss_source_profile_missing", "Enabled RSS source profile was not found.");
            }
            rssFilterEnabled = profile.RssFilterEnabled;
        }

        var mikanIds = context.Items
            .Select(item => ParsePositiveInt(item.SourceWorkId))
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();
        if (mikanIds.Length > 1)
        {
            return Failure("filter_source_work_conflict", "Feed items identify different Mikan works.");
        }

        var feed = new RssFeedDocument(
            context.Items.Select(item => new RssFeedItem(
                item.Title,
                item.SourceUrl ?? string.Empty,
                item.TorrentUrl,
                item.ContentType ?? string.Empty,
                item.Length,
                item.PublishedAtRaw)).ToArray(),
            mikanIds.Length == 0 ? null : mikanIds[0]);
        var batch = await processor.EvaluateAsync(
            feed,
            profileId,
            rssFilterEnabled,
            cancellationToken).ConfigureAwait(false);
        var decisions = batch.Audits.Select((audit, index) =>
            new FilterDecision(
                context.Items[index].Index,
                audit.State.ToString(),
                audit.Eligible,
                audit.Reason,
                0,
                Metadata(audit))).ToArray();
        return new FilterResult(
            decisions,
            [],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["revision"] = batch.Revision.ToString(CultureInfo.InvariantCulture),
                ["enabled"] = batch.Enabled ? "true" : "false",
            });
    }

    private static FilterResult Failure(string code, string message) =>
        new(
            [],
            [new PluginOperationError(code, message)],
            new Dictionary<string, string>(StringComparer.Ordinal));

    private static Dictionary<string, string?> Metadata(
        AnimeGoNet.Core.Rules.MikanLegacyFilterAudit audit) =>
        new(StringComparer.Ordinal)
        {
            ["matched_scope"] = audit.MatchedScope,
            ["matched_key"] = audit.MatchedKey,
            ["identity_mikanid"] = audit.IdentityMikanId?.ToString(CultureInfo.InvariantCulture),
            ["identity_groupid"] = audit.IdentityGroupId?.ToString(CultureInfo.InvariantCulture),
        };

    private static int? ParsePositiveInt(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
        && parsed > 0
            ? parsed
            : null;
}

internal sealed class StagedTorrentDispatchSchedulePlugin(
    StagedTorrentDispatcher dispatcher) : IScheduledPlugin
{
    public PluginDescriptor Descriptor { get; } =
        new(
            "staged-torrent-dispatch",
            "Staged torrent dispatch",
            "1.0.0",
            PluginCategory.Schedule,
            100);

    public async ValueTask<ScheduledResult> ExecuteAsync(
        ScheduledContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await dispatcher.DispatchNextAsync(cancellationToken).ConfigureAwait(false);
            return new ScheduledResult(
                true,
                result.ToString(),
                [],
                result switch
                {
                    StagedDispatchResult.NoWork => TimeSpan.FromSeconds(2),
                    StagedDispatchResult.RetryScheduled => TimeSpan.FromSeconds(5),
                    _ => TimeSpan.Zero,
                });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ScheduledResult(
                false,
                null,
                [new PluginOperationError("staged_dispatch_failed", exception.Message)],
                TimeSpan.FromSeconds(5));
        }
    }
}

internal sealed class DirectoryDatabaseRefreshSchedulePlugin(
    DirectoryDatabaseIndexStore index,
    AnimeGoOptions options) : IScheduledPlugin
{
    public PluginDescriptor Descriptor { get; } =
        new(
            "refresh-directory-database",
            "Refresh directory database",
            "1.0.0",
            PluginCategory.Schedule,
            110);

    public async ValueTask<ScheduledResult> ExecuteAsync(
        ScheduledContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await index.RefreshAsync(
                options.Paths.SavePath,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            return new ScheduledResult(
                true,
                $"indexed={result.IndexedCount};rejected={result.RejectedCount}",
                [],
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new ScheduledResult(
                false,
                null,
                [new PluginOperationError(
                    "directory_database_refresh_failed",
                    "Directory database refresh failed.")],
                null);
        }
    }
}

internal sealed class DataUpdateSchedulePlugin(
    IDataUpdateService service,
    DataUpdateRuntimeState runtimeOptions) : IScheduledPlugin
{
    public PluginDescriptor Descriptor { get; } =
        new(
            "animegonet-data-update",
            "AnimeGoNetData update",
            "1.0.0",
            PluginCategory.Schedule,
            120);

    public async ValueTask<ScheduledResult> ExecuteAsync(
        ScheduledContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var options = runtimeOptions.Value;
            var action = !options.AutoDownload
                ? DataUpdateActions.Check
                : options.AutoImport
                    ? DataUpdateActions.DownloadImport
                    : DataUpdateActions.Download;
            var result = await service.ExecuteAsync(
                DataUpdateTriggerKinds.Scheduled,
                action,
                cancellationToken).ConfigureAwait(false);
            return new ScheduledResult(
                true,
                $"status={result.Status};version={result.DataVersion ?? "-"}",
                [],
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DataUpdateServiceException exception)
        {
            return new ScheduledResult(
                false,
                null,
                [new PluginOperationError(exception.Code, exception.Message)],
                null);
        }
        catch
        {
            return new ScheduledResult(
                false,
                null,
                [new PluginOperationError(
                    "data_update_failed",
                    "The scheduled data update failed.")],
                null);
        }
    }
}
