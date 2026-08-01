using System.Text.Json.Serialization;
using AnimeGoNet.App.Plugins;

namespace AnimeGoNet.App.Serialization;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(ExternalPluginWireRequest))]
[JsonSerializable(typeof(ExternalPluginWireResponse))]
[JsonSerializable(typeof(ExternalPluginInitializePayload))]
[JsonSerializable(typeof(ExternalPluginInitializeResult))]
[JsonSerializable(typeof(ExternalPluginHealthResult))]
[JsonSerializable(typeof(ExternalPluginShutdownPayload))]
internal sealed partial class ExternalPluginJsonContext : JsonSerializerContext;
