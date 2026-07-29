namespace AnimeGo.Plugin.Abstractions;

public enum PluginCategory
{
    Source,
    Feed,
    Parser,
    Filter,
    Rename,
    Schedule,
}

public sealed record PluginDescriptor(
    string Id,
    string DisplayName,
    string Version,
    PluginCategory Category,
    int Order = 0,
    bool IsBuiltIn = true);

public sealed record PluginOperationError(string Code, string Message);

public interface IAnimeGoPlugin
{
    PluginDescriptor Descriptor { get; }
}

public sealed record SourceIngestContext(
    string Source,
    string? TorrentUrl,
    string? Title,
    string? LegacyName,
    string? SourceItemId,
    string? SourceWorkId,
    string? SourceUrl,
    string? LegacyUrl,
    int? MikanId,
    int? BangumiId,
    int? AniDbId,
    string? ImdbId,
    string? PublishedAtRaw,
    DateTimeOffset? PublishedAt,
    bool RequireModernMetadata);

public sealed record SourceNormalizedItem(
    string Source,
    string TorrentUrl,
    string TorrentUrlFingerprint,
    string Title,
    string? SourceItemId,
    string? SourceWorkId,
    int? MikanId,
    int? BangumiId,
    int? AniDbId,
    string? ImdbId,
    string? PublishedAtRaw,
    DateTimeOffset? PublishedAt);

public sealed record SourceIngestResult(
    SourceNormalizedItem? Item,
    IReadOnlyList<PluginOperationError> Errors)
{
    public bool Succeeded => Item is not null && Errors.Count == 0;
}

public interface IInputSourceAdapter : IAnimeGoPlugin
{
    ValueTask<SourceIngestResult> NormalizeAsync(
        SourceIngestContext context,
        CancellationToken cancellationToken);
}

public sealed record FeedContext(
    string SourceProfileId,
    string FeedUrl,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record FeedItem(
    string Title,
    string TorrentUrl,
    string? SourceUrl,
    string? SourceItemId,
    string? SourceWorkId,
    string? ContentType,
    long Length,
    string? PublishedAtRaw,
    DateTimeOffset? PublishedAt);

public sealed record FeedResult(
    IReadOnlyList<FeedItem> Items,
    IReadOnlyList<PluginOperationError> Errors,
    IReadOnlyDictionary<string, string> Metadata);

public interface IFeedPlugin : IAnimeGoPlugin
{
    ValueTask<FeedResult> FetchAsync(FeedContext context, CancellationToken cancellationToken);
}

public sealed record TitleParseContext(
    string Title,
    string? FileName,
    string? SourceProfileId,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record TitleParseResult(
    bool Matched,
    string? AnimeTitle,
    int? Season,
    decimal? Episode,
    string? EpisodeKind,
    string? EpisodeText,
    string? ReleaseGroup,
    string? Resolution,
    IReadOnlyList<PluginOperationError> Errors);

public interface ITitleParserPlugin : IAnimeGoPlugin
{
    ValueTask<TitleParseResult> ParseAsync(
        TitleParseContext context,
        CancellationToken cancellationToken);
}

public sealed record FilterItem(
    int Index,
    string Title,
    string TorrentUrl,
    string? SourceUrl,
    string? SourceItemId,
    string? SourceWorkId,
    string? ContentType,
    long Length,
    string? PublishedAtRaw);

public sealed record FilterSourceProfileSnapshot(
    long Revision,
    bool RssFilterEnabled,
    bool RssPriorityEnabled);

public sealed record FilterContext(
    string SourceProfileId,
    IReadOnlyList<FilterItem> Items,
    IReadOnlyDictionary<string, string> Arguments,
    FilterSourceProfileSnapshot? SourceProfileSnapshot = null);

public sealed record FilterDecision(
    int Index,
    string Outcome,
    bool Accepted,
    string Reason,
    int Priority,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed record FilterResult(
    IReadOnlyList<FilterDecision> Decisions,
    IReadOnlyList<PluginOperationError> Errors,
    IReadOnlyDictionary<string, string> Metadata);

public interface IFeedFilterPlugin : IAnimeGoPlugin
{
    ValueTask<FilterResult> FilterAsync(FilterContext context, CancellationToken cancellationToken);
}

public sealed record RenameContext(
    string SourcePath,
    string SeriesName,
    int Season,
    string Disposition,
    int? Episode,
    string? EpisodeName,
    string? RenameSuffix,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record RenameResult(
    bool Matched,
    string? RelativeTargetPath,
    IReadOnlyList<PluginOperationError> Errors);

public interface IRenamePlugin : IAnimeGoPlugin
{
    ValueTask<RenameResult> RenameAsync(RenameContext context, CancellationToken cancellationToken);
}

public sealed record ScheduledContext(
    string TaskId,
    DateTimeOffset TriggeredAt,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record ScheduledResult(
    bool Succeeded,
    string? Message,
    IReadOnlyList<PluginOperationError> Errors,
    TimeSpan? NextDelay);

public interface IScheduledPlugin : IAnimeGoPlugin
{
    ValueTask<ScheduledResult> ExecuteAsync(
        ScheduledContext context,
        CancellationToken cancellationToken);
}
