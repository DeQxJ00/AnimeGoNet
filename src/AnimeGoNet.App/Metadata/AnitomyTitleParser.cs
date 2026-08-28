using static AnitomySharp.AnitomySharp;

namespace AnimeGoNet.App.Metadata;

public sealed record AnitomyTitleParseResult(
    string SourceText,
    string? AnimeTitle,
    int MatchStart,
    int MatchLength)
{
    public bool Success => !string.IsNullOrWhiteSpace(AnimeTitle);
}

public static class AnitomyTitleParser
{
    public static AnitomyTitleParseResult ParseTitle(string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        var source = sourceText.Trim();
        if (source.Length == 0)
        {
            return new AnitomyTitleParseResult(source, null, -1, 0);
        }

        try
        {
            var separator = Math.Max(source.LastIndexOf('/'), source.LastIndexOf('\\'));
            var fileName = separator < 0 ? source : source[(separator + 1)..];
            var title = Parse(fileName)
                .FirstOrDefault(element => string.Equals(
                    element.Category.ToString(),
                    "ElementAnimeTitle",
                    StringComparison.Ordinal))
                ?.Value
                ?.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                return new AnitomyTitleParseResult(source, null, -1, 0);
            }

            var localStart = fileName.IndexOf(title, StringComparison.OrdinalIgnoreCase);
            var start = localStart < 0 ? -1 : separator + 1 + localStart;
            return new AnitomyTitleParseResult(
                source,
                title,
                start,
                start < 0 ? 0 : title.Length);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return new AnitomyTitleParseResult(source, null, -1, 0);
        }
    }
}
