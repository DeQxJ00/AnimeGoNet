using AnimeGo.Plugin.Abstractions;
using AnimeGoNet.Core.Ingest;

namespace AnimeGoNet.Core.Plugins;

public static class BuiltInPluginCatalog
{
    public static PluginCatalog Create() =>
        new(
        [
            new MikanSourceAdapter(),
            new U2SourceAdapter(),
            new TtgSourceAdapter(),
        ]);
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
