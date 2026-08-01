using AnimeGo.Plugin.Abstractions;
using AnimeGo.Plugin.Sample;
using AnimeGo.Plugin.Sdk;

var metadata = new AnimeGoPluginMetadata(
    "__PLUGIN_ID__",
    "1.0.0",
    PluginCategory.Parser,
    []);
return await AnimeGoPluginHost.RunParserAsync(
    metadata,
    new PluginHandler(),
    PluginJsonContext.Default.TitleParseContext,
    PluginJsonContext.Default.TitleParseResult);
