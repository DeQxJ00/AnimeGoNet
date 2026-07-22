using System.Text.Json;

namespace AnimeGoNet.Core.Rules;

public static class LegacyMikanFilterCodec
{
    public static LegacyMikanFilterConfig Parse(ReadOnlyMemory<byte> utf8Json)
    {
        if (utf8Json.IsEmpty) throw new ArgumentException("Legacy filter JSON is empty.", nameof(utf8Json));
        using var document = JsonDocument.Parse(utf8Json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Legacy filter root must be an object.");
        }

        var tiers = new List<KeyValuePair<string, LegacyMikanFilterRule>>[5];
        for (var index = 0; index < tiers.Length; index++) tiers[index] = [];
        for (var tier = 0; tier <= 4; tier++)
        {
            if (!document.RootElement.TryGetProperty($"Filiter{tier}", out var element)) continue;
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException($"Filiter{tier} must be an object.");
            }
            foreach (var property in element.EnumerateObject())
            {
                tiers[tier].Add(new KeyValuePair<string, LegacyMikanFilterRule>(
                    property.Name, ParseRule(property.Value, tier, property.Name)));
            }
        }

        return new LegacyMikanFilterConfig(
            tiers[0], ToDictionary(tiers[1]), ToDictionary(tiers[2]),
            ToDictionary(tiers[3]), ToDictionary(tiers[4]));
    }

    public static byte[] Encode(LegacyMikanFilterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            WriteTier(writer, "Filiter0", config.Filiter0);
            WriteTier(writer, "Filiter1", config.Filiter1);
            WriteTier(writer, "Filiter2", config.Filiter2);
            WriteTier(writer, "Filiter3", config.Filiter3);
            WriteTier(writer, "Filiter4", config.Filiter4);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static LegacyMikanFilterConfig Empty { get; } = new(
        [], EmptyDictionary(), EmptyDictionary(), EmptyDictionary(), EmptyDictionary());

    private static LegacyMikanFilterRule ParseRule(JsonElement element, int tier, string key)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"Filiter{tier}.{key} must be an object.");
        }
        return new LegacyMikanFilterRule(
            ReadBoolean(element, "is_enable_whitelist"),
            ReadBoolean(element, "is_enable_blacklist"),
            ReadStrings(element, "whitelist"),
            ReadStrings(element, "blacklist"));
    }

    private static bool ReadBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return false;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new FormatException($"{propertyName} must be a boolean."),
        };
    }

    private static List<string> ReadStrings(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return [];
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"{propertyName} must be an array.");
        }
        var result = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new FormatException($"{propertyName} values must be strings.");
            }
            result.Add(item.GetString()!);
        }
        return result;
    }

    private static void WriteTier(
        Utf8JsonWriter writer,
        string name,
        IEnumerable<KeyValuePair<string, LegacyMikanFilterRule>> rules)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        foreach (var pair in rules)
        {
            writer.WritePropertyName(pair.Key);
            writer.WriteStartObject();
            writer.WriteBoolean("is_enable_whitelist", pair.Value.IsEnableWhitelist);
            writer.WritePropertyName("whitelist");
            writer.WriteStartArray();
            foreach (var value in pair.Value.Whitelist) writer.WriteStringValue(value);
            writer.WriteEndArray();
            writer.WriteBoolean("is_enable_blacklist", pair.Value.IsEnableBlacklist);
            writer.WritePropertyName("blacklist");
            writer.WriteStartArray();
            foreach (var value in pair.Value.Blacklist) writer.WriteStringValue(value);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }

    private static Dictionary<string, LegacyMikanFilterRule> ToDictionary(
        IEnumerable<KeyValuePair<string, LegacyMikanFilterRule>> values)
    {
        var result = new Dictionary<string, LegacyMikanFilterRule>(StringComparer.Ordinal);
        foreach (var pair in values) result[pair.Key] = pair.Value;
        return result;
    }

    private static Dictionary<string, LegacyMikanFilterRule> EmptyDictionary() =>
        new(StringComparer.Ordinal);
}
