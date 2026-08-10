using System.Text.Json;

namespace AnimeGoNet.App.AiTesterCompat;

public static class Configuration
{
    public const string DefaultBgmMcpUrl = "http://bgm.mcp.local/mcp";
    public const string DefaultTmdbMcpUrl = "http://tmdb.mcp.local/mcp";
    public const string DefaultAniDbMappingUrlTemplate = "https://raw.githubusercontent.com/DeQxJ00/Anime-Lists-Json/refs/heads/main/api/anidb/{anidbid}.json";

    public static (TesterConfig Config, CliOptions Cli) Load(string[] args)
    {
        ParsedArgs cli = ParseArgs(args);
        TesterConfig config = LoadConfig(cli);

        var cliOptions = new CliOptions(
            Input: BuildInput(cli, requireInput: true, config),
            Integration: ParseBool(cli.GetOptional("integration") ?? "false", "integration"),
            Ui: ParseBool(cli.GetOptional("ui") ?? "false", "ui"),
            UiPort: ParseInt(cli.GetOptional("uiPort") ?? "15057", "uiPort"));

        return (config, cliOptions);
    }

    public static (TesterConfig Config, CliOptions Cli) LoadForUi(string[] args)
    {
        ParsedArgs cli = ParseArgs(args);
        TesterConfig config = LoadConfig(cli);
        var cliOptions = new CliOptions(
            Input: new MatchRequestInput("", []),
            Integration: ParseBool(cli.GetOptional("integration") ?? "false", "integration"),
            Ui: true,
            UiPort: ParseInt(cli.GetOptional("uiPort") ?? "15057", "uiPort"));

        return (config, cliOptions);
    }

    public static bool IsUiRequested(string[] args)
    {
        ParsedArgs cli = ParseArgs(args);
        return ParseBool(cli.GetOptional("ui") ?? "false", "ui");
    }

    public static TesterConfig LoadConfig(ParsedArgs cli)
    {
        Dictionary<string, string> file = LoadJsonFile("appsettings.json");
        Dictionary<string, string> sample = LoadJsonFile("appsettings.sample.json");

        string Get(string key, string? fallback = null)
        {
            string envName = "AIGOTESTER__" + NormalizeKey(key).ToUpperInvariant();
            return First(cli.GetOptional(key), Environment.GetEnvironmentVariable(envName), file.GetValueOrDefault(NormalizeKey(key)), sample.GetValueOrDefault(NormalizeKey(key)), fallback)
                ?? throw new InvalidOperationException($"Missing configuration value '{key}'.");
        }

        string? GetOptional(string key)
        {
            string envName = "AIGOTESTER__" + NormalizeKey(key).ToUpperInvariant();
            return First(cli.GetOptional(key), Environment.GetEnvironmentVariable(envName), file.GetValueOrDefault(NormalizeKey(key)), sample.GetValueOrDefault(NormalizeKey(key)));
        }

        var mode = ParseMode(Get("mode", "responses"));
        string reasoningEffort = Get("reasoningEffort", "medium");
        string? normalizedReasoning = string.Equals(reasoningEffort, "none", StringComparison.OrdinalIgnoreCase)
            ? null
            : reasoningEffort.ToLowerInvariant();
        bool enableBgmMcp = ParseBool(Get("enableBgmMcp", "true"), "enableBgmMcp");
        bool enableTmdbMcp = ParseBool(Get("enableTmdbMcp", "true"), "enableTmdbMcp");
        bool enableAniDbLookup = ParseBool(Get("enableAniDbLookup", "true"), "enableAniDbLookup");
        bool isMikanRssSource = ParseBool(Get("isMikanRssSource", "false"), "isMikanRssSource");
        string bgmMcpUrl = Get("bgmMcpUrl", DefaultBgmMcpUrl);
        string tmdbMcpUrl = Get("tmdbMcpUrl", DefaultTmdbMcpUrl);
        string aniDbTemplate = Get("aniDbMappingUrlTemplate", DefaultAniDbMappingUrlTemplate);

        return new TesterConfig(
            BaseUrl: Get("baseUrl"),
            ApiKey: GetOptional("apiKey") ?? "",
            Model: Get("model"),
            Mode: mode,
            ReasoningEffort: normalizedReasoning,
            WebSearchEnabled: ParseBool(Get("webSearchEnabled", "false"), "webSearchEnabled"),
            TimeoutSeconds: ParseInt(Get("timeoutSeconds", "600"), "timeoutSeconds"),
            ProxyUrl: NormalizeOptionalProxy(GetOptional("proxyUrl")),
            BgmMcpUrl: enableBgmMcp ? ValidateHttpUrl(bgmMcpUrl, "bgmMcpUrl") : bgmMcpUrl,
            TmdbMcpUrl: enableTmdbMcp ? ValidateHttpUrl(tmdbMcpUrl, "tmdbMcpUrl") : tmdbMcpUrl,
            EnableBgmMcp: enableBgmMcp,
            EnableTmdbMcp: enableTmdbMcp,
            EnableAniDbLookup: enableAniDbLookup,
            AniDbMappingUrlTemplate: enableAniDbLookup ? ValidateAniDbTemplate(aniDbTemplate) : aniDbTemplate,
            IsMikanRssSource: isMikanRssSource);
    }

    public static MatchRequestInput BuildInputFromManifestJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string title = GetRequiredString(root, "title");
        long? bgmid = GetOptionalPositiveLong(root, "bgmid");
        long? anidbid = GetOptionalPositiveLong(root, "anidbid");
        string? mikanPubDate = GetOptionalString(root, "mikan_pub_date");
        int? bgmEpisodeCandidate = GetOptionalPositiveInt(root, "bgm_episode_candidate");
        bool enableBangumiPubDateFirst = GetOptionalBool(root, "use_bangumi_pubdate_first", true);
        bool isMikanRssSource = GetOptionalBool(root, "is_mikan_rss_source", false);

        if (!root.TryGetProperty("files", out JsonElement filesElement) || filesElement.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("manifest requires files array.");
        }

        var files = new List<MatchFileInput>();
        foreach (JsonElement file in filesElement.EnumerateArray())
        {
            string name = GetRequiredString(file, "name");
            long sizeBytes = GetRequiredLong(file, "size_bytes");
            files.Add(new MatchFileInput(name, sizeBytes));
        }

        return InputNormalizer.Normalize(new MatchRequestInput(title, files, bgmid, anidbid, mikanPubDate, null, enableBangumiPubDateFirst, bgmEpisodeCandidate, isMikanRssSource));
    }

    public static IReadOnlyList<MatchFileInput> ParseFilesJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("files JSON must be an array.");
        }

        var files = new List<MatchFileInput>();
        foreach (JsonElement file in document.RootElement.EnumerateArray())
        {
            files.Add(new MatchFileInput(GetRequiredString(file, "name"), GetRequiredLong(file, "size_bytes")));
        }

        return files;
    }

    private static MatchRequestInput BuildInput(ParsedArgs cli, bool requireInput, TesterConfig config)
    {
        string? manifestPath = cli.GetOptional("manifest");
        string? torrentPath = cli.GetOptional("torrent");
        IReadOnlyList<string> fileArgs = cli.GetAll("file");
        int sourceCount = (string.IsNullOrWhiteSpace(manifestPath) ? 0 : 1) +
            (string.IsNullOrWhiteSpace(torrentPath) ? 0 : 1) +
            (fileArgs.Count == 0 ? 0 : 1);
        if (sourceCount > 1)
        {
            throw new InvalidOperationException("--manifest, --torrent and --file are mutually exclusive.");
        }

        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            return BuildInputFromManifestJson(File.ReadAllText(manifestPath));
        }

        string? title = cli.GetOptional("title");
        if (!requireInput && string.IsNullOrWhiteSpace(title) && fileArgs.Count == 0 && string.IsNullOrWhiteSpace(torrentPath))
        {
            return new MatchRequestInput("", [], null, null);
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("Missing command line value 'title'.");
        }

        if (!string.IsNullOrWhiteSpace(torrentPath))
        {
            TorrentImportResult imported = TorrentFileImporter.ImportDetailedFromFile(torrentPath);
            return InputNormalizer.Normalize(new MatchRequestInput(
                title,
                imported.VideoFiles,
                ParseOptionalPositiveLong(cli.GetOptional("bgmid"), "bgmid"),
                ParseOptionalPositiveLong(cli.GetOptional("anidbid"), "anidbid"),
                cli.GetOptional("mikanPubDate"),
                imported.TorrentFileCount,
                ParseBool(cli.GetOptional("useBangumiPubDateFirst") ?? "true", "useBangumiPubDateFirst"),
                ParseOptionalPositiveInt(cli.GetOptional("bgmEpisodeCandidate"), "bgmEpisodeCandidate"),
                ParseBool(cli.GetOptional("isMikanRssSource") ?? config.IsMikanRssSource.ToString(), "isMikanRssSource")));
        }

        if (fileArgs.Count == 0)
        {
            throw new InvalidOperationException("At least one --file value is required.");
        }

        var files = new List<MatchFileInput>(fileArgs.Count);
        foreach (string fileArg in fileArgs)
        {
            files.Add(ParseFileArg(fileArg));
        }

        return InputNormalizer.Normalize(new MatchRequestInput(
            title,
            files,
            ParseOptionalPositiveLong(cli.GetOptional("bgmid"), "bgmid"),
            ParseOptionalPositiveLong(cli.GetOptional("anidbid"), "anidbid"),
            cli.GetOptional("mikanPubDate"),
            null,
            ParseBool(cli.GetOptional("useBangumiPubDateFirst") ?? "true", "useBangumiPubDateFirst"),
            ParseOptionalPositiveInt(cli.GetOptional("bgmEpisodeCandidate"), "bgmEpisodeCandidate"),
            ParseBool(cli.GetOptional("isMikanRssSource") ?? config.IsMikanRssSource.ToString(), "isMikanRssSource")));
    }

    private static MatchFileInput ParseFileArg(string value)
    {
        int separator = value.LastIndexOf('|');
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw new ArgumentException("--file must use format name|size_bytes.");
        }

        string name = value[..separator];
        if (!long.TryParse(value[(separator + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long sizeBytes))
        {
            throw new ArgumentException("--file size_bytes must be an integer.");
        }

        return new MatchFileInput(name, sizeBytes);
    }

    private static ParsedArgs ParseArgs(string[] args)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{arg}'. Use --name value.");
            }

            string key = NormalizeKey(arg[2..]);
            string value = "true";
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }

            if (!values.TryGetValue(key, out List<string>? list))
            {
                list = [];
                values[key] = list;
            }

            list.Add(value);
        }

        return new ParsedArgs(values);
    }

    private static Dictionary<string, string> LoadJsonFile(string path)
    {
        string? resolvedPath = ResolveConfigPath(path);
        if (resolvedPath is null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        using FileStream stream = File.OpenRead(resolvedPath);
        using JsonDocument document = JsonDocument.Parse(stream);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            values[NormalizeKey(property.Name)] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? "",
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => property.Value.GetRawText(),
                _ => property.Value.GetRawText()
            };
        }

        return values;
    }

    private static string NormalizeKey(string key) =>
        key.Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();

    private static string? ResolveConfigPath(string path)
    {
        string cwdPath = Path.GetFullPath(path);
        if (File.Exists(cwdPath))
        {
            return cwdPath;
        }

        string outputPath = Path.Combine(AppContext.BaseDirectory, path);
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        string projectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", path));
        return File.Exists(projectPath) ? projectPath : null;
    }

    private static string? First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static ApiMode ParseMode(string value) => NormalizeKey(value) switch
    {
        "responses" or "response" => ApiMode.Responses,
        "chatcompletions" or "chatcompletion" or "chat" => ApiMode.ChatCompletions,
        _ => throw new ArgumentException("mode must be 'responses' or 'chat-completions'.")
    };

    private static bool ParseBool(string value, string key) =>
        bool.TryParse(value, out bool result) ? result : throw new ArgumentException($"{key} must be true or false.");

    private static int ParseInt(string value, string key) =>
        int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int result)
            ? result
            : throw new ArgumentException($"{key} must be an integer.");

    public static long? ParseOptionalPositiveLong(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long result) || result <= 0)
        {
            throw new ArgumentException($"{key} must be a positive integer.");
        }

        return result;
    }

    public static int? ParseOptionalPositiveInt(string? value, string key)
    {
        long? result = ParseOptionalPositiveLong(value, key);
        if (result > int.MaxValue) throw new ArgumentException($"{key} must not exceed {int.MaxValue}.");
        return result is null ? null : (int)result.Value;
    }

    private static string? NormalizeOptionalProxy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value;
    }

    public static string ValidateHttpUrl(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{key} is required when configured.");
        }

        string trimmed = value.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"{key} must be an absolute http or https URL.");
        }

        return trimmed;
    }

    public static string ValidateAniDbTemplate(string? value)
    {
        string template = ValidateHttpUrl(value, "aniDbMappingUrlTemplate");
        const string placeholder = "{anidbid}";
        int first = template.IndexOf(placeholder, StringComparison.Ordinal);
        if (first < 0 || template.IndexOf(placeholder, first + placeholder.Length, StringComparison.Ordinal) >= 0)
        {
            throw new ArgumentException("aniDbMappingUrlTemplate must contain exactly one {anidbid} placeholder.");
        }

        return template;
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new ArgumentException($"manifest requires string {propertyName}.");
        }

        return property.GetString()!;
    }

    private static long GetRequiredLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.Number || !property.TryGetInt64(out long value))
        {
            throw new ArgumentException($"manifest requires integer {propertyName}.");
        }

        return value;
    }

    private static long? GetOptionalPositiveLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        string? value = property.ValueKind == JsonValueKind.String ? property.GetString() : property.GetRawText();
        return ParseOptionalPositiveLong(value, propertyName);
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException($"manifest {propertyName} must be a string or null.");
        }

        return string.IsNullOrWhiteSpace(property.GetString()) ? null : property.GetString()!.Trim();
    }

    private static int? GetOptionalPositiveInt(JsonElement element, string propertyName)
    {
        long? value = GetOptionalPositiveLong(element, propertyName);
        if (value > int.MaxValue) throw new ArgumentException($"manifest {propertyName} must not exceed {int.MaxValue}.");
        return value is null ? null : (int)value.Value;
    }

    private static bool GetOptionalBool(JsonElement element, string propertyName, bool fallback)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind == JsonValueKind.Null) return fallback;
        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException($"manifest {propertyName} must be true or false.");
        return property.GetBoolean();
    }

}

public sealed class ParsedArgs(Dictionary<string, List<string>> values)
{
    public string? GetOptional(string key)
    {
        string normalized = key.Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
        return values.TryGetValue(normalized, out List<string>? list) && list.Count > 0 ? list[^1] : null;
    }

    public IReadOnlyList<string> GetAll(string key)
    {
        string normalized = key.Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
        return values.TryGetValue(normalized, out List<string>? list) ? list : [];
    }
}
