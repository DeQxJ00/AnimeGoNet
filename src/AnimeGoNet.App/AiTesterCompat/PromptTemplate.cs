using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AnimeGoNet.App.AiTesterCompat;

public static class PromptTemplate
{
    private const string TitlePlaceholder = "{{TITLE_JSON}}";
    private const string FilesPlaceholder = "{{FILES_JSON}}";
    private const string BgmidPlaceholder = "{{BGMID_JSON}}";
    private const string AnidbidPlaceholder = "{{ANIDBID_JSON}}";
    private const string MikanPubDatePlaceholder = "{{MIKAN_PUB_DATE_JSON}}";
    private const string TorrentFileCountPlaceholder = "{{TORRENT_FILE_COUNT_JSON}}";
    private const string BgmEpisodeCandidatePlaceholder = "{{BGM_EPISODE_CANDIDATE_JSON}}";
    private const string RequestIdentityPlaceholder = "{{REQUEST_IDENTITY_JSON}}";

    public static string LoadFromMarkdown(string markdownPath)
    {
        string markdown = File.ReadAllText(markdownPath);
        return ExtractSingleTextCodeBlock(markdown);
    }

    public static string ExtractSingleTextCodeBlock(string markdown)
    {
        const string fence = "```text";
        int start = markdown.IndexOf(fence, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException("Prompt markdown does not contain a ```text code block.");
        }

        int contentStart = markdown.IndexOf('\n', start);
        if (contentStart < 0)
        {
            throw new InvalidOperationException("Prompt markdown text code block is malformed.");
        }

        int end = markdown.IndexOf("```", contentStart + 1, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new InvalidOperationException("Prompt markdown text code block is not closed.");
        }

        int next = markdown.IndexOf(fence, end + 3, StringComparison.Ordinal);
        if (next >= 0)
        {
            throw new InvalidOperationException("Prompt markdown must contain exactly one ```text code block.");
        }

        return markdown[(contentStart + 1)..end].Trim('\r', '\n');
    }

    public static RenderedPrompt Render(string template, MatchRequestInput input) =>
        Render(template, input, new PromptFeatures(true, input.Bgmid is not null, input.Anidbid is not null, false));

    public static RenderedPrompt Render(string template, MatchRequestInput input, PromptFeatures features)
    {
        MatchRequestInput normalized = InputNormalizer.Normalize(input);
        string effectiveTemplate = ApplyConditionalSections(template, features);
        string requestIdentity = ComputeRequestIdentity(effectiveTemplate, normalized, features);
        PubDatePriority.TryNormalizePubDate(normalized.MikanPubDate, out string? normalizedPubDate, out _);

        string rendered = effectiveTemplate
            .Replace(TitlePlaceholder, JsonString(normalized.Title), StringComparison.Ordinal)
            .Replace(FilesPlaceholder, RenderFilesJson(normalized.Files), StringComparison.Ordinal)
            .Replace(BgmidPlaceholder, RenderOptionalId(normalized.Bgmid), StringComparison.Ordinal)
            .Replace(AnidbidPlaceholder, RenderOptionalId(normalized.Anidbid), StringComparison.Ordinal)
            .Replace(MikanPubDatePlaceholder, RenderOptionalString(normalizedPubDate), StringComparison.Ordinal)
            .Replace(TorrentFileCountPlaceholder, RenderOptionalId(normalized.TorrentFileCount), StringComparison.Ordinal)
            .Replace(BgmEpisodeCandidatePlaceholder, RenderOptionalId(normalized.BgmEpisodeCandidate), StringComparison.Ordinal)
            .Replace(RequestIdentityPlaceholder, JsonString(requestIdentity), StringComparison.Ordinal);

        if (rendered.Contains(TitlePlaceholder, StringComparison.Ordinal) ||
            rendered.Contains(FilesPlaceholder, StringComparison.Ordinal) ||
            rendered.Contains(BgmidPlaceholder, StringComparison.Ordinal) ||
            rendered.Contains(AnidbidPlaceholder, StringComparison.Ordinal) ||
            rendered.Contains(MikanPubDatePlaceholder, StringComparison.Ordinal) ||
            rendered.Contains(TorrentFileCountPlaceholder, StringComparison.Ordinal) ||
            rendered.Contains(BgmEpisodeCandidatePlaceholder, StringComparison.Ordinal) ||
            rendered.Contains(RequestIdentityPlaceholder, StringComparison.Ordinal) ||
            rendered.Contains("{{", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Prompt placeholders were not fully replaced.");
        }

        return new RenderedPrompt(rendered, requestIdentity, normalized);
    }

    public static string ComputeRequestIdentity(string template, MatchRequestInput input) =>
        ComputeRequestIdentity(template, input, new PromptFeatures(true, input.Bgmid is not null, input.Anidbid is not null, false));

    public static string ComputeRequestIdentity(string template, MatchRequestInput input, PromptFeatures features)
    {
        MatchRequestInput normalized = InputNormalizer.Normalize(input);
        var builder = new StringBuilder();
        builder.Append("prompt=").Append(Sha256Hex(template)).Append('\n');
        builder.Append("title=").Append(normalized.Title).Append('\n');
        builder.Append("tmdb_mcp=").Append(features.TmdbMcp).Append('\n');
        builder.Append("bgmid=").Append(features.BgmMcp ? normalized.Bgmid?.ToString(System.Globalization.CultureInfo.InvariantCulture) : "inactive").Append('\n');
        builder.Append("anidbid=").Append(features.AniDbLookup ? normalized.Anidbid?.ToString(System.Globalization.CultureInfo.InvariantCulture) : "inactive").Append('\n');
        builder.Append("bangumi_pubdate_first=").Append(features.BangumiPubDateFirst).Append('\n');
        if (features.BangumiPubDateFirst)
        {
            PubDatePriority.TryNormalizePubDate(normalized.MikanPubDate, out string? pubDate, out _);
            builder.Append("mikan_pub_date=").Append(pubDate).Append('\n');
            builder.Append("torrent_file_count=").Append(normalized.TorrentFileCount).Append('\n');
            builder.Append("bgm_episode_candidate=").Append(normalized.BgmEpisodeCandidate).Append('\n');
        }
        foreach (MatchFileInput file in normalized.Files.OrderBy(file => file.Name, StringComparer.Ordinal).ThenBy(file => file.SizeBytes))
        {
            builder.Append("file=").Append(file.Name).Append('|').Append(file.SizeBytes);
            builder.Append('\n');
        }

        return Sha256Hex(builder.ToString());
    }

    private static string ApplyConditionalSections(string template, PromptFeatures features)
    {
        EnsureConditionalSection(template, "TMDB_MCP");
        EnsureConditionalSection(template, "BGM_MCP");
        EnsureConditionalSection(template, "ANIDB_LOOKUP");
        EnsureConditionalSection(template, "BANGUMI_PUBDATE_FIRST");
        string rendered = ApplyConditionalSection(template, "TMDB_MCP", features.TmdbMcp);
        rendered = ApplyConditionalSection(rendered, "BGM_MCP", features.BgmMcp);
        rendered = ApplyConditionalSection(rendered, "ANIDB_LOOKUP", features.AniDbLookup);
        return ApplyConditionalSection(rendered, "BANGUMI_PUBDATE_FIRST", features.BangumiPubDateFirst);
    }

    private static void EnsureConditionalSection(string template, string name)
    {
        if (!template.Contains("{{#" + name + "}}", StringComparison.Ordinal) ||
            !template.Contains("{{/" + name + "}}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Prompt must retain the {name} conditional section markers.");
        }
    }

    private static string ApplyConditionalSection(string template, string name, bool include)
    {
        string opening = "{{#" + name + "}}";
        string closing = "{{/" + name + "}}";
        int start;
        while ((start = template.IndexOf(opening, StringComparison.Ordinal)) >= 0)
        {
            int contentStart = start + opening.Length;
            int end = template.IndexOf(closing, contentStart, StringComparison.Ordinal);
            if (end < 0)
            {
                throw new InvalidOperationException($"Prompt conditional section '{name}' is not closed.");
            }

            string content = include ? template[contentStart..end] : "";
            template = template[..start] + content + template[(end + closing.Length)..];
        }

        if (template.Contains(closing, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Prompt conditional section '{name}' has no opening marker.");
        }

        return template;
    }

    public static string FindDefaultMarkdownPath()
    {
        string outputPath = Path.Combine(AppContext.BaseDirectory, "docs", "TMDB_AI_MATCH_PROMPT_TESTER.md");
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        string current = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            string candidate = Path.GetFullPath(Path.Combine(current, "..", "..", "docs", "TMDB_AI_MATCH_PROMPT_TESTER.md"));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = Path.GetFullPath(Path.Combine(current, ".."));
        }

        throw new FileNotFoundException("Unable to find docs/TMDB_AI_MATCH_PROMPT_TESTER.md near the executable or repository root.");
    }

    private static string RenderFilesJson(IReadOnlyList<MatchFileInput> files)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (MatchFileInput file in files)
            {
                writer.WriteStartObject();
                writer.WriteString("name", file.Name);
                writer.WriteNumber("size_bytes", file.SizeBytes);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string JsonString(string value) =>
        "\"" + JsonEncodedText.Encode(value, JavaScriptEncoder.UnsafeRelaxedJsonEscaping) + "\"";

    private static string RenderOptionalId(long? value) =>
        value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null";

    private static string RenderOptionalString(string? value) =>
        value is null ? "null" : JsonString(value);

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed record RenderedPrompt(string Text, string RequestIdentity, MatchRequestInput NormalizedInput);

public static class InputNormalizer
{
    public static MatchRequestInput Normalize(MatchRequestInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
        {
            throw new ArgumentException("title is required.");
        }

        if (input.Files.Count == 0)
        {
            throw new ArgumentException("files must contain at least one item.");
        }

        var files = new List<MatchFileInput>(input.Files.Count);
        foreach (MatchFileInput file in input.Files)
        {
            if (file.SizeBytes < 0)
            {
                throw new ArgumentException($"size_bytes for '{file.Name}' must be non-negative.");
            }

            string name = FilenameTools.NormalizeTaskFileName(file.Name);
            int? candidate = input.IsMikanRssSource ? FileEpisodeCandidateResolver.Resolve(name) : null;
            files.Add(new MatchFileInput(name, file.SizeBytes, candidate));
        }

        if (input.TorrentFileCount is < 1)
        {
            throw new ArgumentException("torrent_file_count must be positive when present.");
        }

        if (input.BgmEpisodeCandidate is <= 0)
        {
            throw new ArgumentException("bgm_episode_candidate must be a positive integer when present.");
        }

        return new MatchRequestInput(
            input.Title.Trim(),
            files,
            input.Bgmid,
            input.Anidbid,
            string.IsNullOrWhiteSpace(input.MikanPubDate) ? null : input.MikanPubDate.Trim(),
            input.TorrentFileCount,
            input.EnableBangumiPubDateFirst,
            input.BgmEpisodeCandidate,
            input.IsMikanRssSource);
    }
}

public static class FilenameTools
{
    public static string NormalizeTaskFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("file name is required.", nameof(value));
        }

        string normalized = value.Trim().Replace('\\', '/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        if (Path.IsPathRooted(normalized) || normalized.StartsWith("../", StringComparison.Ordinal) || normalized.Contains("/../", StringComparison.Ordinal) || normalized is "." or "..")
        {
            normalized = GetBasename(normalized);
        }

        return normalized;
    }

    public static string GetBasename(string value)
    {
        string trimmed = value.Trim().Replace('\\', '/');
        int separator = trimmed.LastIndexOf('/');
        string basename = separator >= 0 ? trimmed[(separator + 1)..] : trimmed;
        if (string.IsNullOrWhiteSpace(basename) || basename is "." or "..")
        {
            throw new ArgumentException("file name must include a basename.", nameof(value));
        }

        return basename;
    }
}
