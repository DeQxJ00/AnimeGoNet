using AnimeGoNet.Core.Diagnostics;

namespace AnimeGoNet.Core.Library;

/// <summary>A file identity independent of the machine's mount points.</summary>
public sealed record PortableFileLocation(string RootKind, string RootId, string RelativePath);

public sealed record PortablePathRoot(string Kind, string Id, string Path);

public sealed class PathMappingException(string code) : IOException(code), IStableError
{
    public string Code { get; } = code;

    public StableErrorSemantic Semantics => StableErrorSemantic.None;
}

public static class PortableFilePaths
{
    public static string NormalizeRelative(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        var normalized = relativePath.Replace('\\', '/');
        if (normalized.StartsWith('/') || (normalized.Length >= 2 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':')
            || normalized.Contains('\0', StringComparison.Ordinal))
        {
            throw new PathMappingException("path_relative_invalid");
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".."))
        {
            throw new PathMappingException("path_relative_invalid");
        }

        return string.Join('/', segments);
    }

    // Pure lexical conversion: old Windows paths must also migrate on Linux, and vice versa.
    public static bool TryMakeRelative(string root, string path, out string relative)
    {
        relative = string.Empty;
        // A literal backslash in a POSIX filename cannot be represented by our separator
        // convention without changing its identity. Do not reinterpret it as a directory.
        if (!IsWindowsPath(path) && path.Contains('\\', StringComparison.Ordinal)) return false;
        var normalizedRoot = NormalizeAbsolute(root);
        var normalizedPath = NormalizeAbsolute(path);
        if (normalizedRoot is null || normalizedPath is null) return false;
        var comparison = IsWindowsPath(root) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (IsWindowsPath(root) != IsWindowsPath(path)) return false;
        if (normalizedPath.Equals(normalizedRoot, comparison)) return true;
        var prefix = normalizedRoot.TrimEnd('/') + '/';
        if (!normalizedPath.StartsWith(prefix, comparison)) return false;
        try
        {
            relative = NormalizeRelative(normalizedPath[prefix.Length..]);
            return true;
        }
        catch (PathMappingException)
        {
            return false;
        }
    }

    public static string Resolve(string root, string relative, bool windows)
    {
        var normalizedRoot = NormalizeAbsolute(root);
        if (normalizedRoot is null || IsWindowsPath(root) != windows)
        {
            throw new PathMappingException("path_root_platform_mismatch");
        }

        var normalizedRelative = NormalizeRelative(relative);
        if (windows && normalizedRelative.Split('/').Any(IsInvalidWindowsSegment))
        {
            throw new PathMappingException("path_filename_platform_incompatible");
        }

        var combined = normalizedRelative.Length == 0
            ? normalizedRoot
            : normalizedRoot.TrimEnd('/') + '/' + normalizedRelative;
        return windows ? combined.Replace('/', '\\') : combined;
    }

    public static bool IsWindowsPath(string path) =>
        path.StartsWith("\\\\", StringComparison.Ordinal)
        || path.StartsWith("//", StringComparison.Ordinal)
        || (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':'
            && path[2] is '\\' or '/');

    private static string? NormalizeAbsolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var normalized = path.Replace('\\', '/');
        if (!normalized.StartsWith('/') && !IsWindowsPath(normalized)) return null;
        if (normalized.Contains('\0', StringComparison.Ordinal)
            || normalized.Split('/').Any(segment => segment is "." or "..")) return null;
        if (normalized.StartsWith("//", StringComparison.Ordinal)
            && normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Length < 2) return null;
        return normalized.Length == 1 ? normalized : normalized.TrimEnd('/') +
            (normalized.Length == 3 && normalized[1] == ':' ? "/" : string.Empty);
    }

    private static bool IsInvalidWindowsSegment(string segment)
    {
        if (segment.Length == 0) return false;
        if (segment.EndsWith(' ') || segment.EndsWith('.')
            || segment.Any(character => character < 32 || "<>:\"|?*".Contains(character, StringComparison.Ordinal))) return true;
        var name = segment.Split('.')[0];
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || name.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || name.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || name.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (name.Length == 4 && name[3] is >= '1' and <= '9'
                && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)));
    }
}
