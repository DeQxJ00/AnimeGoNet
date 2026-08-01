using AnimeGo.Plugin.Abstractions;
using AnimeGo.Plugin.Sdk;

namespace AnimeGo.Plugin.Sample;

internal sealed class PluginHandler : IFeedPluginHandler
{
    public ValueTask<FeedResult> ExecuteAsync(
        AnimeGoPluginExecutionContext<FeedContext> context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new FeedResult(
            [],
            [],
            new Dictionary<string, string> { ["sample"] = "empty-feed" }));
    }
}
