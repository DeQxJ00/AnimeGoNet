using AnimeGo.Plugin.Abstractions;
using AnimeGo.Plugin.Sdk;

namespace AnimeGo.Plugin.Sample;

internal sealed class PluginHandler : IFilterPluginHandler
{
    public ValueTask<FilterResult> ExecuteAsync(
        AnimeGoPluginExecutionContext<FilterContext> context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var decisions = context.Request.Items
            .Select(item => new FilterDecision(
                item.Index,
                "accepted",
                true,
                "sample_accept",
                0,
                new Dictionary<string, string?>()))
            .ToArray();
        return ValueTask.FromResult(new FilterResult(
            decisions,
            [],
            new Dictionary<string, string> { ["sample"] = "accept-all" }));
    }
}
