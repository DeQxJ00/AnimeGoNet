using AnimeGo.Plugin.Abstractions;
using AnimeGo.Plugin.Sdk;
using AnimeGoNet.ContainerPluginFixture;

var metadata = new AnimeGoPluginMetadata(
    "com.animegonet.container-source",
    "1.0.0",
    PluginCategory.Source,
    []);
return await AnimeGoPluginHost.RunSourceAsync(
    metadata,
    new ContainerSourcePlugin(),
    ContainerPluginJsonContext.Default.SourceIngestContext,
    ContainerPluginJsonContext.Default.SourceIngestResult);
