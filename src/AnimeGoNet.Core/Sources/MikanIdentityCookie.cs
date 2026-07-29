namespace AnimeGoNet.Core.Sources;

public static class MikanIdentityCookie
{
    public const string Name = ".AspNetCore.Identity.Application";
    public const int MaximumLength = 8 * 1024;

    public static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        var prefix = Name + "=";
        if (normalized.StartsWith(prefix, StringComparison.Ordinal))
        {
            normalized = normalized[prefix.Length..];
        }

        if (normalized.Length is < 1 or > MaximumLength
            || normalized.Any(character => !IsCookieOctet(character)))
        {
            throw new ArgumentException(
                $"Mikan identity Cookie must contain 1 to {MaximumLength} valid Cookie value characters.");
        }

        return normalized;
    }

    private static bool IsCookieOctet(char character) =>
        character == '\u0021'
        || character is >= '\u0023' and <= '\u002B'
        || character is >= '\u002D' and <= '\u003A'
        || character is >= '\u003C' and <= '\u005B'
        || character is >= '\u005D' and <= '\u007E';
}
