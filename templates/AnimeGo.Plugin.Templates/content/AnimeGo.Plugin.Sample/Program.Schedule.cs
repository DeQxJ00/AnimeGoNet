using AnimeGo.Plugin.Abstractions;
using AnimeGo.Plugin.Sample;
using AnimeGo.Plugin.Sdk;

var metadata = new AnimeGoPluginMetadata(
    "__PLUGIN_ID__",
    "1.0.0",
    PluginCategory.Schedule,
    []);
return await AnimeGoPluginHost.RunScheduleAsync(
    metadata,
    new PluginHandler(),
    PluginJsonContext.Default.ScheduledContext,
    PluginJsonContext.Default.ScheduledResult);
