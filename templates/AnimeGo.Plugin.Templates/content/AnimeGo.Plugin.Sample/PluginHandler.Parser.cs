using AnimeGo.Plugin.Abstractions;
using AnimeGo.Plugin.Sdk;

namespace AnimeGo.Plugin.Sample;

internal sealed class PluginHandler : IParserPluginHandler
{
    public ValueTask<TitleParseResult> ExecuteAsync(
        AnimeGoPluginExecutionContext<TitleParseContext> context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new TitleParseResult(
            false,
            null,
            null,
            null,
            "unknown",
            null,
            null,
            null,
            []));
    }
}
