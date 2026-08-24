using System.Buffers;
using System.Text;

namespace AnimeGoNet.Core.Library;

public static class PortablePathNormalizer
{
    private static readonly SearchValues<char> InvalidSegmentCharacters =
        SearchValues.Create("<>:\"/\\|?*");

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

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

    public static string NormalizeRelativePathForComparison(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value[0] is '/' or '\\'
            || (value.Length >= 3
                && char.IsAsciiLetter(value[0])
                && value[1] == ':'
                && value[2] is '/' or '\\'))
        {
            throw new ArgumentException("A portable comparison path must be relative.", nameof(value));
        }

        var segments = value.Replace('\\', '/').Split('/');
        if (segments.Any(static segment =>
                string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new ArgumentException(
                "A portable comparison path cannot contain empty or traversal segments.",
                nameof(value));
        }

        return string.Join('/', segments.Select(SanitizeSegment));
    }
}
