using AnimeGo.Plugin.Abstractions;
using AnimeGo.Plugin.Sdk;

namespace AnimeGo.Plugin.Sample;

internal sealed class PluginHandler : IRenamePluginHandler
{
    public ValueTask<RenameResult> ExecuteAsync(
        AnimeGoPluginExecutionContext<RenameContext> context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new RenameResult(false, null, []));
    }
}
