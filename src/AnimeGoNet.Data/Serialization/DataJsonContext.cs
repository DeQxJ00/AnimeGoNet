using System.Text.Json.Serialization;

namespace AnimeGoNet.Data.Serialization;

[JsonSerializable(typeof(string[]))]
internal sealed partial class DataJsonContext : JsonSerializerContext;
