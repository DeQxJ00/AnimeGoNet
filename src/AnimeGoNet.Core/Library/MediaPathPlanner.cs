using System.Globalization;

namespace AnimeGoNet.Core.Library;

public sealed record MediaPathInput(
    string CanonicalSeriesName,
    int SeasonNumber,
    string Disposition,
    int? EpisodeNumber,
    string OriginalRelativePath,
    string? RenameSuffix = null);

public static class MediaPathPlanner
{
    public static string PlanRelativePath(MediaPathInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.CanonicalSeriesName);
        ArgumentOutOfRangeException.ThrowIfLessThan(input.SeasonNumber, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.OriginalRelativePath);

        var series = SanitizeSegment(input.CanonicalSeriesName);
        var season = $"S{input.SeasonNumber.ToString("00", CultureInfo.InvariantCulture)}";
        var fileName = input.Disposition switch
        {
            "episode" => PlanEpisodeFileName(input),
            "other" or "extras" => SanitizeSegment(GetFileName(input.OriginalRelativePath)),
            _ => throw new ArgumentException(
                "Only episode, other, and extras files can be organized.",
                nameof(input)),
        };

        return input.Disposition is "other" or "extras"
            ? Path.Combine(series, season, "Extras", fileName)
            : Path.Combine(series, season, fileName);
    }

    public static string SanitizeSegment(string value) =>
        PortablePathNormalizer.SanitizeSegment(value);

    private static string PlanEpisodeFileName(MediaPathInput input)
    {
        if (input.EpisodeNumber is not > 0)
        {
            throw new ArgumentException("Episode files require a positive TMDB episode number.", nameof(input));
        }

        var originalName = GetFileName(input.OriginalRelativePath);
        var extension = input.RenameSuffix ?? Path.GetExtension(originalName);
        extension = extension.Length == 0 ? string.Empty : SanitizeSuffix(extension);
        return $"E{input.EpisodeNumber.Value.ToString("000", CultureInfo.InvariantCulture)}{extension}";
    }

    private static string SanitizeSuffix(string extension)
    {
        var segments = extension.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeSegment)
            .Where(segment => segment != "_")
            .ToArray();
        return segments.Length == 0 ? string.Empty : "." + string.Join('.', segments);
    }

    private static string GetFileName(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimEnd('/');
        var separator = normalized.LastIndexOf('/');
        var fileName = separator >= 0 ? normalized[(separator + 1)..] : normalized;
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or "..")
        {
            throw new ArgumentException("Original relative path must identify a file.", nameof(relativePath));
        }

        return fileName;
    }
}
