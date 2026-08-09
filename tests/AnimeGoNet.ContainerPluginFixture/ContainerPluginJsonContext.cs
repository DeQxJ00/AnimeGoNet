using System.Text.Json.Serialization;
using AnimeGo.Plugin.Abstractions;

namespace AnimeGoNet.ContainerPluginFixture;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(SourceIngestContext))]
[JsonSerializable(typeof(SourceIngestResult))]
internal sealed partial class ContainerPluginJsonContext : JsonSerializerContext;
