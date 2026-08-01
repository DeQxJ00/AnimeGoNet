using AnimeGo.Plugin.Abstractions;
using AnimeGo.Plugin.Sample;
using AnimeGo.Plugin.Sdk;

var metadata = new AnimeGoPluginMetadata(
    "__PLUGIN_ID__",
    "1.0.0",
    PluginCategory.Filter,
    []);
return await AnimeGoPluginHost.RunFilterAsync(
    metadata,
    new PluginHandler(),
    PluginJsonContext.Default.FilterContext,
    PluginJsonContext.Default.FilterResult);
