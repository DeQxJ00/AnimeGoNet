using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnimeGoNet.Core.Metadata;

public sealed class AiMetadataEpisodeJsonConverter : JsonConverter<int?>
{
    public override bool HandleNull => true;

    public override int? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var episode))
        {
            return episode;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString()?.Trim();
            if (string.Equals(
                value,
                AiMetadataFileCandidate.ExtrasEpisodeValue,
                StringComparison.OrdinalIgnoreCase))
            {
                return AiMetadataFileCandidate.ExtrasEpisodeSentinel;
            }

            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out episode))
            {
                return episode;
            }
        }

        throw new JsonException("episode must be a number, numeric string, null, or Extras.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        int? value,
        JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else if (value == AiMetadataFileCandidate.ExtrasEpisodeSentinel)
        {
            writer.WriteStringValue(AiMetadataFileCandidate.ExtrasEpisodeValue);
        }
        else
        {
            writer.WriteStringValue(value.Value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
