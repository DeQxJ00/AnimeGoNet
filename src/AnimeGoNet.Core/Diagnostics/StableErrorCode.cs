namespace AnimeGoNet.Core.Diagnostics;

public static class StableErrorCode
{
    public const int MaximumLength = 128;

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    public static string Require(string? value, string parameterName)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                $"Error code must be a 1-{MaximumLength} character ASCII identifier.",
                parameterName);
        }

        return value!;
    }
}
