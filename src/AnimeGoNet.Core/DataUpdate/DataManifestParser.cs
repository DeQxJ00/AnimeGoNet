using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AnimeGoNet.Core.DataUpdate;

public static partial class DataManifestParser
{
    public const int CurrentSchemaVersion = 2;
    public const int MinimumSupportedSchemaVersion = 1;
    public const int MaximumManifestBytes = 1024 * 1024;
    public const long MaximumAssetBytes = 8L * 1024 * 1024 * 1024;
    public const int MaximumAssets = 4096;

    public static DataManifest Parse(ReadOnlyMemory<byte> utf8Json)
    {
        if (utf8Json.IsEmpty || utf8Json.Length > MaximumManifestBytes)
        {
            throw Error("data_manifest_size_invalid", "Data manifest size is invalid.");
        }

        try
        {
            using var document = JsonDocument.Parse(
                utf8Json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 8,
                });
            return Parse(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new DataManifestException(
                "data_manifest_json_invalid",
                $"Data manifest JSON is invalid: {exception.Message}");
        }
        catch (OverflowException)
        {
            throw Error("data_manifest_totals_overflow", "Data manifest totals exceed supported bounds.");
        }
    }

    private static DataManifest Parse(JsonElement root)
    {
        RequireObject(root);
        var schemaVersion = RequiredInt32(root, "schema_version");
        if (schemaVersion is < MinimumSupportedSchemaVersion or > CurrentSchemaVersion)
        {
            throw Error("data_manifest_schema_unsupported", "Data manifest schema is not supported.");
        }

        var dataVersion = RequiredString(root, "data_version", 64);
        if (!StableVersion().IsMatch(dataVersion))
        {
            throw Error("data_manifest_version_invalid", "Data version is invalid.");
        }
        var generatedAt = RequiredUtcTimestamp(root, "generated_at_utc");
        var minimumClientVersion = RequiredString(root, "minimum_client_version", 64);
        if (!Version.TryParse(minimumClientVersion, out _))
        {
            throw Error("data_manifest_client_version_invalid", "Minimum client version is invalid.");
        }

        var upstreamElement = RequiredProperty(root, "upstream", JsonValueKind.Object);
        var upstream = new DataManifestUpstream(
            RequiredString(upstreamElement, "repository", 512),
            RequiredString(upstreamElement, "release", 256),
            RequiredString(upstreamElement, "asset", 256),
            RequiredSha256(upstreamElement, "sha256"));

        var assetsElement = RequiredProperty(root, "assets", JsonValueKind.Array);
        var assetCount = assetsElement.GetArrayLength();
        if (assetCount is < 2 or > MaximumAssets)
        {
            throw Error("data_manifest_assets_invalid", "Data manifest asset count is invalid.");
        }
        var assets = new List<DataManifestAsset>(assetCount);
        var fileNames = new HashSet<string>(StringComparer.Ordinal);
        long subjectCount = 0;
        long episodeCount = 0;
        long relationCount = 0;
        foreach (var element in assetsElement.EnumerateArray())
        {
            RequireObject(element);
            var kindText = RequiredString(element, "kind", 16);
            var kind = kindText switch
            {
                "subjects" => DataAssetKind.Subjects,
                "episodes" => DataAssetKind.Episodes,
                "relations" when schemaVersion >= 2 => DataAssetKind.Relations,
                _ => throw Error("data_manifest_asset_kind_invalid", "Data asset kind is invalid."),
            };
            var fileName = RequiredString(element, "file_name", 256);
            if (!IsSafeFileName(fileName) || !fileNames.Add(fileName))
            {
                throw Error("data_manifest_asset_name_invalid", "Data asset file name is invalid.");
            }
            var url = RequiredHttpUrl(element, "url");
            var sizeBytes = RequiredInt64(element, "size_bytes");
            var recordCount = RequiredInt64(element, "record_count");
            var idMin = RequiredInt32(element, "subject_id_min");
            var idMax = RequiredInt32(element, "subject_id_max");
            if (sizeBytes is <= 0 or > MaximumAssetBytes
                || recordCount <= 0
                || idMin <= 0
                || idMax < idMin)
            {
                throw Error("data_manifest_asset_range_invalid", "Data asset bounds are invalid.");
            }
            assets.Add(new DataManifestAsset(
                kind,
                fileName,
                url,
                sizeBytes,
                RequiredSha256(element, "sha256"),
                recordCount,
                idMin,
                idMax));
            if (kind == DataAssetKind.Subjects)
            {
                subjectCount = checked(subjectCount + recordCount);
            }
            else if (kind == DataAssetKind.Episodes)
            {
                episodeCount = checked(episodeCount + recordCount);
            }
            else
            {
                relationCount = checked(relationCount + recordCount);
            }
        }

        if (subjectCount == 0 || episodeCount == 0)
        {
            throw Error("data_manifest_asset_kind_missing", "Both subject and episode assets are required.");
        }
        if (schemaVersion >= 2 && relationCount == 0)
        {
            throw Error(
                "data_manifest_relation_asset_missing",
                "Schema v2 requires at least one relation asset.");
        }
        var totals = RequiredProperty(root, "totals", JsonValueKind.Object);
        if (RequiredInt64(totals, "subjects") != subjectCount
            || RequiredInt64(totals, "episodes") != episodeCount)
        {
            throw Error("data_manifest_totals_mismatch", "Data manifest totals do not match assets.");
        }
        if (schemaVersion >= 2
            && RequiredInt64(totals, "relations") != relationCount)
        {
            throw Error(
                "data_manifest_totals_mismatch",
                "Data manifest totals do not match assets.");
        }

        return new DataManifest(
            schemaVersion,
            dataVersion,
            generatedAt,
            minimumClientVersion,
            upstream,
            assets,
            subjectCount,
            episodeCount)
        {
            RelationCount = relationCount,
        };
    }

    private static JsonElement RequiredProperty(
        JsonElement parent,
        string name,
        JsonValueKind kind)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != kind)
        {
            throw Error("data_manifest_shape_invalid", "Data manifest shape is invalid.");
        }
        return value;
    }

    private static string RequiredString(JsonElement parent, string name, int maxLength)
    {
        var value = RequiredProperty(parent, name, JsonValueKind.String).GetString()!;
        if (string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Length > maxLength
            || value.Any(char.IsControl))
        {
            throw Error("data_manifest_value_invalid", "Data manifest value is invalid.");
        }
        return value;
    }

    private static int RequiredInt32(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var result))
        {
            throw Error("data_manifest_value_invalid", "Data manifest integer is invalid.");
        }
        return result;
    }

    private static long RequiredInt64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var result))
        {
            throw Error("data_manifest_value_invalid", "Data manifest integer is invalid.");
        }
        return result;
    }

    private static string RequiredSha256(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name, 64);
        if (!LowerSha256().IsMatch(value))
        {
            throw Error("data_manifest_sha256_invalid", "Data manifest SHA-256 is invalid.");
        }
        return value;
    }

    private static Uri RequiredHttpUrl(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name, 2048);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw Error("data_manifest_asset_url_invalid", "Data asset URL is invalid.");
        }
        return uri;
    }

    private static DateTimeOffset RequiredUtcTimestamp(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name, 64);
        if (!DateTimeOffset.TryParseExact(
                value,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var result)
            || result.Offset != TimeSpan.Zero)
        {
            throw Error("data_manifest_timestamp_invalid", "Data manifest timestamp must be UTC ISO-8601.");
        }
        return result;
    }

    private static void RequireObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Error("data_manifest_shape_invalid", "Data manifest shape is invalid.");
        }
    }

    private static bool IsSafeFileName(string value) =>
        value == Path.GetFileName(value)
        && !value.Contains('\\', StringComparison.Ordinal)
        && !value.Contains('/', StringComparison.Ordinal)
        && value.EndsWith(".jsonl.gz", StringComparison.Ordinal)
        && SafeFileName().IsMatch(value);

    private static DataManifestException Error(string code, string message) =>
        new(code, message);

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableVersion();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex LowerSha256();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*\\.jsonl\\.gz$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeFileName();
}
