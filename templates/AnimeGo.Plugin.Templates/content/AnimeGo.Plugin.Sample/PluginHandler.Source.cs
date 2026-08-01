using System.Security.Cryptography;
using System.Text;
using AnimeGo.Plugin.Abstractions;
using AnimeGo.Plugin.Sdk;

namespace AnimeGo.Plugin.Sample;

internal sealed class PluginHandler : ISourcePluginHandler
{
    public ValueTask<SourceIngestResult> ExecuteAsync(
        AnimeGoPluginExecutionContext<SourceIngestContext> context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        if (string.IsNullOrWhiteSpace(request.TorrentUrl)
            || string.IsNullOrWhiteSpace(request.Title))
        {
            return ValueTask.FromResult(new SourceIngestResult(
                null,
                [new PluginOperationError("source_input_required", "Torrent URL and title are required.")]));
        }
        var normalizedUrl = request.TorrentUrl.Trim();
        var fingerprint = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalizedUrl)));
        return ValueTask.FromResult(new SourceIngestResult(
            new SourceNormalizedItem(
                request.Source,
                normalizedUrl,
                fingerprint,
                request.Title.Trim(),
                request.SourceItemId,
                request.SourceWorkId,
                request.MikanId,
                request.BangumiId,
                request.AniDbId,
                request.ImdbId,
                request.PublishedAtRaw,
                request.PublishedAt),
            []));
    }
}
