using System.Globalization;
using AnimeGo.Plugin.Abstractions;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Library;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.Core.Plugins;

public static class BuiltInPluginCatalog
{
    public static PluginCatalog Create(IEnumerable<IAnimeGoPlugin>? extensions = null)
    {
        List<IAnimeGoPlugin> plugins =
        [
            new MikanSourceAdapter(),
            new U2SourceAdapter(),
            new TtgSourceAdapter(),
            new MikanTitleParserPlugin(),
            new AnimeLibraryRenamePlugin(),
        ];
        if (extensions is not null)
        {
            plugins.AddRange(extensions);
        }

        return new PluginCatalog(plugins);
    }
}

internal abstract class BuiltInSourceAdapter(
    string id,
    string displayName,
    int order) : IInputSourceAdapter
{
    public PluginDescriptor Descriptor { get; } =
        new(id, displayName, "1.0.0", PluginCategory.Source, order);

    public ValueTask<SourceIngestResult> NormalizeAsync(
        SourceIngestContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var command = new IngestItemCommand(
            context.TorrentUrl,
            new IngestItemInfo(
                context.Title,
                context.LegacyName,
                context.SourceItemId,
                context.SourceWorkId,
                context.SourceUrl,
                context.LegacyUrl,
                context.MikanId,
                context.BangumiId,
                context.AniDbId,
                context.ImdbId),
            context.PublishedAtRaw is null && context.PublishedAt is null
                ? null
                : new IngestSourceEvidence(context.PublishedAtRaw, context.PublishedAt));
        var normalized = IngestCommandNormalizer.NormalizeKnownSource(
            Descriptor.Id,
            command,
            context.RequireModernMetadata);
        var item = normalized.Item is null
            ? null
            : new SourceNormalizedItem(
                normalized.Item.Source,
                normalized.Item.TorrentUrl.AbsoluteUri,
                normalized.Item.TorrentUrlFingerprint,
                normalized.Item.Title,
                normalized.Item.SourceItemId,
                normalized.Item.SourceWorkId,
                normalized.Item.MikanId,
                normalized.Item.BangumiId,
                normalized.Item.AniDbId,
                normalized.Item.ImdbId,
                normalized.Item.PublishedAtRaw,
                normalized.Item.PublishedAt);
        return ValueTask.FromResult(new SourceIngestResult(
            item,
            normalized.Errors
                .Select(error => new PluginOperationError("invalid_input", error))
                .ToArray()));
    }
}

internal sealed class MikanSourceAdapter()
    : BuiltInSourceAdapter("mikan", "Mikan input source", 10);

internal sealed class U2SourceAdapter()
    : BuiltInSourceAdapter("u2", "U2 input source", 20);

internal sealed class TtgSourceAdapter()
    : BuiltInSourceAdapter("ttg", "TTG input source", 30);

internal sealed class MikanTitleParserPlugin : ITitleParserPlugin
{
    public PluginDescriptor Descriptor { get; } =
        new("mikan-title", "Mikan title parser", "1.0.0", PluginCategory.Parser, 100);

    public ValueTask<TitleParseResult> ParseAsync(
        TitleParseContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(context.Title))
        {
            return ValueTask.FromResult(new TitleParseResult(
                false, null, null, null, "unknown", null, null, null,
                [new PluginOperationError("title_empty", "Title is required.")]));
        }

        var candidate = MikanRssEpisodeParser.Parse(context.Title);
        var episode = candidate.SourceEpisode is not null
            && decimal.TryParse(
                candidate.SourceEpisode,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsedEpisode)
            ? parsedEpisode
            : (decimal?)null;
        return ValueTask.FromResult(new TitleParseResult(
            candidate.Kind != TorrentEpisodeCandidateKind.Unknown,
            null,
            null,
            episode,
            EpisodeKind(candidate.Kind),
            candidate.SourceEpisode,
            null,
            null,
            candidate.Reason is null
                ? []
                : [new PluginOperationError(candidate.Reason, candidate.Reason)]));
    }

    private static string EpisodeKind(TorrentEpisodeCandidateKind kind) => kind switch
    {
        TorrentEpisodeCandidateKind.Normal => "normal",
        TorrentEpisodeCandidateKind.Fractional => "fractional",
        TorrentEpisodeCandidateKind.Special => "special",
        _ => "unknown",
    };
}

internal sealed class AnimeLibraryRenamePlugin : IRenamePlugin
{
    public PluginDescriptor Descriptor { get; } =
        new("anime-library", "Anime library rename", "1.0.0", PluginCategory.Rename, 100);

    public ValueTask<RenameResult> RenameAsync(
        RenameContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var path = MediaPathPlanner.PlanRelativePath(new MediaPathInput(
                context.SeriesName,
                context.Season,
                context.Disposition,
                context.Episode,
                context.SourcePath,
                context.RenameSuffix));
            return ValueTask.FromResult(new RenameResult(true, path, []));
        }
        catch (ArgumentException exception)
        {
            return ValueTask.FromResult(new RenameResult(
                false,
                null,
                [new PluginOperationError("rename_invalid_input", exception.Message)]));
        }
    }
}
