namespace AnimeGoNet.Core.Configuration;

public static class SourceDownloadPolicy
{
    public const int MaximumTagCount = 16;
    public const int MaximumSeedingTimeMinutes = 5_256_000;
    public const string HardLinkType = "hard";
    public const string SymbolicLinkType = "symbolic";

    public static string NormalizeCategory(string? value)
    {
        var category = value?.Trim() ?? string.Empty;
        if (category.Length is < 1 or > 64
            || category.Any(character => char.IsControl(character) || character == ','))
        {
            throw new ArgumentException(
                "category must contain 1 to 64 characters without control characters or commas.");
        }

        return category;
    }

    public static IReadOnlyList<string> NormalizeTags(IEnumerable<string?>? values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var tags = values
            .Select(value => value?.Trim() ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (tags.Length > MaximumTagCount
            || tags.Any(tag => tag.Length is < 1 or > 64
                || tag.Any(character => char.IsControl(character) || character == ',')))
        {
            throw new ArgumentException(
                $"tags must contain at most {MaximumTagCount} unique values of 1 to 64 characters without control characters or commas.");
        }

        return tags;
    }

    public static int ValidateSeedingTimeMinutes(string fileStrategy, int value)
    {
        if (value is < -1 or > MaximumSeedingTimeMinutes)
        {
            throw new ArgumentException(
                $"seeding_time_minutes must be -1 or between 0 and {MaximumSeedingTimeMinutes}.");
        }

        if (string.Equals(fileStrategy, "move", StringComparison.Ordinal) && value != 0)
        {
            throw new ArgumentException("move requires seeding_time_minutes=0 because moving stops seeding.");
        }

        return value;
    }

    public static string NormalizeLinkType(string fileStrategy, string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? HardLinkType
            : value.Trim().ToLowerInvariant();
        if (normalized is not (HardLinkType or SymbolicLinkType))
        {
            throw new ArgumentException("link_type must be hard or symbolic.");
        }

        if (normalized == SymbolicLinkType
            && !string.Equals(fileStrategy, "link", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "symbolic link_type is only valid with file_strategy=link; "
                + "link_delete must use hard links so deleting the source cannot break the media target.");
        }

        return normalized;
    }
}
