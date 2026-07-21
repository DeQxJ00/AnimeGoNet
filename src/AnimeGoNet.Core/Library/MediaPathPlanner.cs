using System.Buffers;
using System.Globalization;
using System.Text;

namespace AnimeGoNet.Core.Library;

public sealed record MediaPathInput(
    string CanonicalSeriesName,
    int SeasonNumber,
    string Disposition,
    int? EpisodeNumber,
    string OriginalRelativePath);

public static class MediaPathPlanner
{
    private static readonly SearchValues<char> InvalidSegmentCharacters =
        SearchValues.Create("<>:\"/\\|?*");

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

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
            "other" => SanitizeSegment(GetFileName(input.OriginalRelativePath)),
            _ => throw new ArgumentException("Only episode and other files can be organized.", nameof(input)),
        };

        return input.Disposition == "other"
            ? Path.Combine(series, season, "Other", fileName)
            : Path.Combine(series, season, fileName);
    }

    public static string SanitizeSegment(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(normalized.Length);
        var replaced = false;
        foreach (var character in normalized)
        {
            var invalid = char.IsControl(character) || InvalidSegmentCharacters.Contains(character);
            if (invalid)
            {
                if (!replaced)
                {
                    builder.Append('_');
                    replaced = true;
                }
            }
            else
            {
                builder.Append(character);
                replaced = false;
            }
        }

        var result = builder.ToString().Trim().TrimEnd('.', ' ');
        if (result.Length == 0 || result is "." or "..")
        {
            return "_";
        }

        var stem = result.Split('.', 2)[0];
        return WindowsReservedNames.Contains(stem) ? "_" + result : result;
    }

    private static string PlanEpisodeFileName(MediaPathInput input)
    {
        if (input.EpisodeNumber is not > 0)
        {
            throw new ArgumentException("Episode files require a positive TMDB episode number.", nameof(input));
        }

        var originalName = GetFileName(input.OriginalRelativePath);
        var extension = Path.GetExtension(originalName);
        extension = extension.Length == 0 ? string.Empty : SanitizeExtension(extension);
        return $"E{input.EpisodeNumber.Value.ToString("000", CultureInfo.InvariantCulture)}{extension}";
    }

    private static string SanitizeExtension(string extension)
    {
        var value = SanitizeSegment(extension.TrimStart('.'));
        return value == "_" ? string.Empty : "." + value;
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
