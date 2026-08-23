using System.Globalization;

namespace AnimeGoNet.Core.Library;

public sealed record MoviePathInput(
    string CanonicalTitle,
    DateOnly? ReleaseDate,
    string OriginalRelativePath,
    string? RenameSuffix = null);

public static class MoviePathPlanner
{
    public static string PlanRelativePath(MoviePathInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.CanonicalTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.OriginalRelativePath);

        var title = MediaPathPlanner.SanitizeSegment(input.CanonicalTitle);
        var directory = input.ReleaseDate is null
            ? title
            : $"{title} ({input.ReleaseDate.Value.Year.ToString(CultureInfo.InvariantCulture)})";
        var extension = input.RenameSuffix ?? Path.GetExtension(FileName(input.OriginalRelativePath));
        extension = SanitizeSuffix(extension);
        return Path.Combine(directory, directory + extension);
    }

    public static string DirectoryName(string canonicalTitle, DateOnly? releaseDate)
    {
        var title = MediaPathPlanner.SanitizeSegment(canonicalTitle);
        return releaseDate is null
            ? title
            : $"{title} ({releaseDate.Value.Year.ToString(CultureInfo.InvariantCulture)})";
    }

    private static string SanitizeSuffix(string extension)
    {
        var segments = extension.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(MediaPathPlanner.SanitizeSegment)
            .Where(segment => segment != "_")
            .ToArray();
        return segments.Length == 0 ? string.Empty : "." + string.Join('.', segments);
    }

    private static string FileName(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimEnd('/');
        var fileName = normalized[(normalized.LastIndexOf('/') + 1)..];
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or "..")
        {
            throw new ArgumentException("Original relative path must identify a file.", nameof(relativePath));
        }
        return fileName;
    }
}
