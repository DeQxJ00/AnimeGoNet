using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Metadata;

public static class AiMetadataPromptRenderer
{
    public const string PromptVersion = "tmdb-ai-match-v13";
    public const int MaximumTemplateLength = AiMatchingOptions.MaximumPromptTemplateLength;

    private static readonly string[] RequiredPlaceholders =
    [
        "{{SOURCE_TITLE_JSON}}",
        "{{FILES_JSON}}",
        "{{OPTIONAL_BGM_ID_JSON}}",
        "{{OPTIONAL_ANIDB_ID_JSON}}",
        "{{OPTIONAL_IMDB_ID_JSON}}",
        "{{TORRENT_FILE_COUNT_JSON}}",
        "{{OPTIONAL_PUBLISHED_AT_JSON}}",
        "{{OPTIONAL_BGM_EPISODE_CANDIDATE_JSON}}",
        "{{USE_BANGUMI_PUBDATE_FIRST_JSON}}",
    ];

    private static readonly string[] RequiredConditionalSections =
    [
        "TMDB_MCP",
        "BGM_MCP",
        "ANIDB_LOOKUP",
        "IMDB_LOOKUP",
        "BANGUMI_PUBDATE_FIRST",
    ];

    public static string LoadAndRender(AiMetadataMatchInput input)
    {
        var template = input.PromptTemplateOverride ?? LoadTemplate();
        if (template.Length > MaximumTemplateLength)
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.InvalidInput,
                "ai_prompt_template_too_large");
        }

        ValidateTemplate(template);

        return Render(template, input);
    }

    public static void ValidateTemplate(string template)
    {
        if (string.IsNullOrWhiteSpace(template)
            || template.Length > MaximumTemplateLength
            || template.Contains('\0'))
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.InvalidInput,
                "ai_prompt_template_invalid");
        }

        if (RequiredPlaceholders.Any(placeholder =>
            !template.Contains(placeholder, StringComparison.Ordinal)))
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.InvalidInput,
                "ai_prompt_required_placeholder_missing");
        }

        if (RequiredConditionalSections.Any(section =>
            !template.Contains("{{#" + section + "}}", StringComparison.Ordinal)
            || !template.Contains("{{/" + section + "}}", StringComparison.Ordinal)))
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.InvalidInput,
                "ai_prompt_required_conditional_missing");
        }

        var fixture = new AiMetadataMatchInput(
            "Prompt validation",
            [new AiMetadataFileInput("01.mkv", 1)],
            1,
            1,
            "tt0000001",
            1,
            DateTimeOffset.UnixEpoch,
            1,
            true)
        {
            PromptFeaturesOverride = new AiMetadataPromptFeatures(true, true, true, true)
            {
                ImdbLookup = true,
            },
        };
        _ = Render(template, fixture);
        _ = Render(
            template,
            fixture with
            {
                BangumiSubjectId = null,
                AniDbAnimeId = null,
                ImdbTitleId = null,
                PublishedAt = null,
                BangumiEpisodeCandidate = null,
                UseBangumiPubDateFirst = false,
                PromptFeaturesOverride = new AiMetadataPromptFeatures(false, false, false, false),
            });
    }

    public static string LoadTemplate()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "docs",
            "TMDB_AI_MATCH_PROMPT.md");
        if (!File.Exists(path))
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.Configuration,
                "ai_prompt_missing");
        }

        return ExtractSingleTextCodeBlock(File.ReadAllText(path, Encoding.UTF8));
    }

    public static string ExtractSingleTextCodeBlock(string markdown)
    {
        const string opening = "```text";
        var start = markdown.IndexOf(opening, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.Configuration,
                "ai_prompt_text_block_missing");
        }

        var contentStart = markdown.IndexOf('\n', start);
        var end = contentStart < 0
            ? -1
            : markdown.IndexOf("```", contentStart + 1, StringComparison.Ordinal);
        if (contentStart < 0 || end < 0
            || markdown.IndexOf(opening, end + 3, StringComparison.Ordinal) >= 0)
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.Configuration,
                "ai_prompt_text_block_invalid");
        }

        return markdown[(contentStart + 1)..end].Trim('\r', '\n');
    }

    public static string Render(string template, AiMetadataMatchInput input)
    {
        var features = AiMetadataPromptFeatures.Resolve(input);
        var rendered = ApplyConditionalSections(template, features)
            .Replace("{{SOURCE_TITLE_JSON}}", JsonString(input.Title), StringComparison.Ordinal)
            .Replace("{{FILES_JSON}}", RenderFiles(input.Files), StringComparison.Ordinal)
            .Replace("{{OPTIONAL_BGM_ID_JSON}}", OptionalNumber(input.BangumiSubjectId), StringComparison.Ordinal)
            .Replace("{{OPTIONAL_ANIDB_ID_JSON}}", OptionalNumber(input.AniDbAnimeId), StringComparison.Ordinal)
            .Replace("{{OPTIONAL_IMDB_ID_JSON}}", OptionalString(input.ImdbTitleId), StringComparison.Ordinal)
            .Replace("{{TORRENT_FILE_COUNT_JSON}}", Number(input.TorrentFileCount), StringComparison.Ordinal)
            .Replace("{{OPTIONAL_PUBLISHED_AT_JSON}}", OptionalString(
                input.PublishedAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
                StringComparison.Ordinal)
            .Replace("{{OPTIONAL_BGM_EPISODE_CANDIDATE_JSON}}",
                OptionalNumber(input.UseBangumiPubDateFirst
                    ? input.BangumiEpisodeCandidate
                    : null),
                StringComparison.Ordinal)
            .Replace("{{USE_BANGUMI_PUBDATE_FIRST_JSON}}",
                input.UseBangumiPubDateFirst ? "true" : "false",
                StringComparison.Ordinal);
        if (rendered.Contains("{{", StringComparison.Ordinal))
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.Configuration,
                "ai_prompt_placeholder_unresolved");
        }

        return rendered;
    }

    private static string ApplyConditionalSections(
        string template,
        AiMetadataPromptFeatures features)
    {
        var rendered = ApplyConditionalSection(template, "TMDB_MCP", features.TmdbMcp);
        rendered = ApplyConditionalSection(rendered, "BGM_MCP", features.BangumiMcp);
        rendered = ApplyConditionalSection(rendered, "ANIDB_LOOKUP", features.AniDbLookup);
        rendered = ApplyConditionalSection(rendered, "IMDB_LOOKUP", features.ImdbLookup);
        return ApplyConditionalSection(
            rendered,
            "BANGUMI_PUBDATE_FIRST",
            features.BangumiPubDateFirst);
    }

    private static string ApplyConditionalSection(
        string template,
        string name,
        bool include)
    {
        var opening = "{{#" + name + "}}";
        var closing = "{{/" + name + "}}";
        var start = template.IndexOf(opening, StringComparison.Ordinal);
        while (start >= 0)
        {
            var contentStart = start + opening.Length;
            var end = template.IndexOf(closing, contentStart, StringComparison.Ordinal);
            if (end < 0)
            {
                throw new AiMetadataMatcherException(
                    MetadataFailureKind.Configuration,
                    "ai_prompt_conditional_invalid");
            }

            var content = include ? template[contentStart..end] : string.Empty;
            template = template[..start] + content + template[(end + closing.Length)..];
            start = template.IndexOf(opening, StringComparison.Ordinal);
        }

        if (template.Contains(closing, StringComparison.Ordinal))
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.Configuration,
                "ai_prompt_conditional_invalid");
        }

        return template;
    }

    private static string RenderFiles(IReadOnlyList<AiMetadataFileInput> files)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var file in files)
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

    private static string OptionalString(string? value) =>
        value is null ? "null" : JsonString(value);

    private static string OptionalNumber(int? value) =>
        value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null";

    private static string Number(int value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
