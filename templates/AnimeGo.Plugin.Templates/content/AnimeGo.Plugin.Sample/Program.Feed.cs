using AnimeGo.Plugin.Abstractions;
using AnimeGo.Plugin.Sample;
using AnimeGo.Plugin.Sdk;

var metadata = new AnimeGoPluginMetadata(
    "__PLUGIN_ID__",
    "1.0.0",
    PluginCategory.Feed,
    []);
return await AnimeGoPluginHost.RunFeedAsync(
    metadata,
    new PluginHandler(),
    PluginJsonContext.Default.FeedContext,
    PluginJsonContext.Default.FeedResult);
