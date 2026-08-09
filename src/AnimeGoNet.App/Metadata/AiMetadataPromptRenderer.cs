using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Metadata;

public static class AiMetadataPromptRenderer
{
    public const string PromptVersion = "tmdb-ai-match-v10";

    public static string LoadAndRender(AiMetadataMatchInput input)
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

        return Render(ExtractSingleTextCodeBlock(File.ReadAllText(path, Encoding.UTF8)), input);
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
        var rendered = template
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
