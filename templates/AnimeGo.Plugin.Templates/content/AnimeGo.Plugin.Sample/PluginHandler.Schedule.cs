using AnimeGo.Plugin.Abstractions;
using AnimeGo.Plugin.Sdk;

namespace AnimeGo.Plugin.Sample;

internal sealed class PluginHandler : ISchedulePluginHandler
{
    public ValueTask<ScheduledResult> ExecuteAsync(
        AnimeGoPluginExecutionContext<ScheduledContext> context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ScheduledResult(
            true,
            "Sample schedule completed.",
            [],
            null));
    }
}
