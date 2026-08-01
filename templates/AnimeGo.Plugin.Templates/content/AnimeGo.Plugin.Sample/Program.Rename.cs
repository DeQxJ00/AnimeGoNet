using AnimeGo.Plugin.Abstractions;
using AnimeGo.Plugin.Sample;
using AnimeGo.Plugin.Sdk;

var metadata = new AnimeGoPluginMetadata(
    "__PLUGIN_ID__",
    "1.0.0",
    PluginCategory.Rename,
    []);
return await AnimeGoPluginHost.RunRenameAsync(
    metadata,
    new PluginHandler(),
    PluginJsonContext.Default.RenameContext,
    PluginJsonContext.Default.RenameResult);
