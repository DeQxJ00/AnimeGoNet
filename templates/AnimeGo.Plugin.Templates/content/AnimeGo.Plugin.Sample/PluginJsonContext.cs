using System.Text.Json.Serialization;
using AnimeGo.Plugin.Abstractions;

namespace AnimeGo.Plugin.Sample;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(SourceIngestContext))]
[JsonSerializable(typeof(SourceIngestResult))]
[JsonSerializable(typeof(FeedContext))]
[JsonSerializable(typeof(FeedResult))]
[JsonSerializable(typeof(TitleParseContext))]
[JsonSerializable(typeof(TitleParseResult))]
[JsonSerializable(typeof(FilterContext))]
[JsonSerializable(typeof(FilterResult))]
[JsonSerializable(typeof(RenameContext))]
[JsonSerializable(typeof(RenameResult))]
[JsonSerializable(typeof(ScheduledContext))]
[JsonSerializable(typeof(ScheduledResult))]
internal sealed partial class PluginJsonContext : JsonSerializerContext;
