using AnimeGo.Plugin.Abstractions;
using AnimeGo.Plugin.Sample;
using AnimeGo.Plugin.Sdk;

var metadata = new AnimeGoPluginMetadata(
    "__PLUGIN_ID__",
    "1.0.0",
    PluginCategory.Source,
    []);
return await AnimeGoPluginHost.RunSourceAsync(
    metadata,
    new PluginHandler(),
    PluginJsonContext.Default.SourceIngestContext,
    PluginJsonContext.Default.SourceIngestResult);
