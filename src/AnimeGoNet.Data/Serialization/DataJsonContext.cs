using System.Text.Json.Serialization;
using AnimeGoNet.Data.Cache;

namespace AnimeGoNet.Data.Serialization;

[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(LegacyCacheExportPackage))]
internal sealed partial class DataJsonContext : JsonSerializerContext;
