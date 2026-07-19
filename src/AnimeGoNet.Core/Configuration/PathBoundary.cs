using System.Text.RegularExpressions;

namespace AnimeGoNet.Core.Configuration;

public static partial class PathBoundary
{
    public static bool IsAbsolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path.StartsWith('/')
            || WindowsDrivePath().IsMatch(path)
            || path.StartsWith("\\\\", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(path);
    }

    public static bool IsWithin(string root, string candidate)
    {
        if (!IsAbsolute(root) || !IsAbsolute(candidate))
        {
            return false;
        }

        if (IsPosix(root) || IsPosix(candidate))
        {
            if (!IsPosix(root) || !IsPosix(candidate))
            {
                return false;
            }

            var normalizedRoot = NormalizePosix(root);
            var normalizedCandidate = NormalizePosix(candidate);
            return normalizedCandidate.Equals(normalizedRoot, StringComparison.Ordinal)
                || normalizedCandidate.StartsWith(normalizedRoot + '/', StringComparison.Ordinal);
        }

        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return fullCandidate.Equals(fullRoot, comparison)
            || fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison);
    }

    public static string Combine(string root, string child)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(child);
        if (IsPosix(root))
        {
            return NormalizePosix(root + '/' + child);
        }

        return Path.Combine(root, child);
    }

    private static bool IsPosix(string path) => path.StartsWith('/');

    private static string NormalizePosix(string path)
    {
        var segments = new List<string>();
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return "/__outside_root__";
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return '/' + string.Join('/', segments);
    }

    [GeneratedRegex("^[A-Za-z]:[\\\\/]")]
    private static partial Regex WindowsDrivePath();
}
