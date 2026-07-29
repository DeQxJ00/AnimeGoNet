using System.Text;

namespace AnimeGoNet.App.Configuration;

public sealed record LegacyConfigurationDiagnostic(
    string Code,
    string Source,
    string LegacyDownloaderType,
    string Message,
    bool BlocksDownloads);

public sealed class LegacyDownloaderMigrationState(
    IReadOnlyList<LegacyConfigurationDiagnostic> diagnostics)
{
    public static LegacyDownloaderMigrationState None { get; } = new([]);

    public IReadOnlyList<LegacyConfigurationDiagnostic> Diagnostics { get; } =
        diagnostics.ToArray();

    public bool BlocksDownloads => Diagnostics.Any(item => item.BlocksDownloads);

    public LegacyConfigurationDiagnostic? BlockingDiagnostic =>
        Diagnostics.FirstOrDefault(item => item.BlocksDownloads);
}

public static class LegacyDownloaderMigrationDetector
{
    private const int MaximumLegacyYamlBytes = 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static LegacyDownloaderMigrationState Detect(
        string? legacyEnvironmentDownloader,
        string dataPath,
        string? legacyConfigurationPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataPath);
        var environmentValue = NormalizeScalar(legacyEnvironmentDownloader);
        if (environmentValue is not null)
        {
            return FromType(environmentValue, "environment:ANIMEGO_CLIENT");
        }

        var explicitPath = NormalizeScalar(legacyConfigurationPath);
        string path;
        try
        {
            path = explicitPath is null
                ? Path.Combine(dataPath, "animego.yaml")
                : Path.GetFullPath(explicitPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return Unreadable("legacy_yaml_path_invalid");
        }

        if (!File.Exists(path))
        {
            return explicitPath is null
                ? LegacyDownloaderMigrationState.None
                : Unreadable("legacy_yaml_not_found");
        }

        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaximumLegacyYamlBytes)
            {
                return Unreadable("legacy_yaml_too_large");
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var bytes = new byte[MaximumLegacyYamlBytes + 1];
            var read = 0;
            while (read < bytes.Length)
            {
                var count = stream.Read(bytes, read, bytes.Length - read);
                if (count == 0)
                {
                    break;
                }

                read += count;
            }

            if (read > MaximumLegacyYamlBytes)
            {
                return Unreadable("legacy_yaml_too_large");
            }

            var yaml = StrictUtf8.GetString(bytes, 0, read);
            var type = ReadDownloaderType(yaml);
            return type is null
                ? LegacyDownloaderMigrationState.None
                : FromType(type, "legacy_yaml");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or DecoderFallbackException)
        {
            return Unreadable("legacy_yaml_unreadable");
        }
    }

    internal static string? ReadDownloaderType(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        var stack = new List<(int Indent, string Key)>();
        using var reader = new StringReader(yaml);
        while (reader.ReadLine() is { } rawLine)
        {
            if (rawLine.Contains('\0'))
            {
                return null;
            }

            var line = StripComment(rawLine).TrimEnd();
            if (string.IsNullOrWhiteSpace(line)
                || line.TrimStart() is "---" or "...")
            {
                continue;
            }

            var indent = 0;
            while (indent < line.Length && line[indent] == ' ')
            {
                indent++;
            }

            if (indent < line.Length && line[indent] == '\t')
            {
                return null;
            }

            var content = line[indent..];
            var separator = FindMappingSeparator(content);
            if (separator <= 0)
            {
                continue;
            }

            var key = NormalizeScalar(content[..separator]);
            if (key is null)
            {
                continue;
            }

            while (stack.Count > 0 && stack[^1].Indent >= indent)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            var value = NormalizeScalar(content[(separator + 1)..]);
            if (value is null)
            {
                stack.Add((indent, key));
                continue;
            }

            if (stack.Count == 2
                && string.Equals(stack[0].Key, "setting", StringComparison.OrdinalIgnoreCase)
                && string.Equals(stack[1].Key, "client", StringComparison.OrdinalIgnoreCase)
                && string.Equals(key, "client", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    private static LegacyDownloaderMigrationState FromType(string type, string source)
    {
        var normalized = type.Trim();
        if (string.Equals(normalized, "qbittorrent", StringComparison.OrdinalIgnoreCase))
        {
            return LegacyDownloaderMigrationState.None;
        }

        var safeType = normalized.Length <= 64 ? normalized : normalized[..64];
        return new LegacyDownloaderMigrationState(
        [
            new LegacyConfigurationDiagnostic(
                "UnsupportedDownloaderType",
                source,
                safeType,
                $"Legacy downloader type '{safeType}' is not supported. Remove the legacy override and configure a qBittorrent instance explicitly.",
                BlocksDownloads: true),
        ]);
    }

    private static LegacyDownloaderMigrationState Unreadable(string reason) =>
        new(
        [
            new LegacyConfigurationDiagnostic(
                "LegacyConfigurationUnreadable",
                "legacy_yaml",
                "unknown",
                $"Legacy animego.yaml could not be inspected safely ({reason}). Move or repair it before enabling downloads.",
                BlocksDownloads: true),
        ]);

    private static int FindMappingSeparator(string value)
    {
        var singleQuoted = false;
        var doubleQuoted = false;
        var escaped = false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (doubleQuoted && escaped)
            {
                escaped = false;
                continue;
            }

            if (doubleQuoted && character == '\\')
            {
                escaped = true;
                continue;
            }

            if (!doubleQuoted && character == '\'')
            {
                singleQuoted = !singleQuoted;
                continue;
            }

            if (!singleQuoted && character == '"')
            {
                doubleQuoted = !doubleQuoted;
                continue;
            }

            if (!singleQuoted && !doubleQuoted && character == ':')
            {
                return index;
            }
        }

        return -1;
    }

    private static string StripComment(string value)
    {
        var singleQuoted = false;
        var doubleQuoted = false;
        var escaped = false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (doubleQuoted && escaped)
            {
                escaped = false;
                continue;
            }

            if (doubleQuoted && character == '\\')
            {
                escaped = true;
                continue;
            }

            if (!doubleQuoted && character == '\'')
            {
                singleQuoted = !singleQuoted;
                continue;
            }

            if (!singleQuoted && character == '"')
            {
                doubleQuoted = !doubleQuoted;
                continue;
            }

            if (!singleQuoted && !doubleQuoted && character == '#')
            {
                return value[..index];
            }
        }

        return value;
    }

    private static string? NormalizeScalar(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length >= 2
            && ((normalized[0] == '"' && normalized[^1] == '"')
                || (normalized[0] == '\'' && normalized[^1] == '\'')))
        {
            normalized = normalized[1..^1].Trim();
        }

        return normalized.Length == 0 ? null : normalized;
    }
}
