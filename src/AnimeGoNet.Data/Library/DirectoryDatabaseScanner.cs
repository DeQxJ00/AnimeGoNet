using System.Text.Json;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Diagnostics;

namespace AnimeGoNet.Data.Library;

public sealed class DirectoryDatabaseScanner
{
    internal const long MaximumSidecarBytes = 64 * 1024;
    internal const string AnimeFileName = "anime.a_json";
    internal const string SeasonFileName = "anime.s_json";
    internal const string EpisodeSuffix = ".e_json";

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The scanner is an injectable service boundary.")]
    public async Task<DirectoryDatabaseScanResult> ScanAsync(
        string saveRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveRoot);
        var root = Path.GetFullPath(saveRoot);
        var rootInfo = new DirectoryInfo(root);
        if (!rootInfo.Exists)
        {
            return new DirectoryDatabaseScanResult(0, [], []);
        }
        if (IsSymbolic(rootInfo))
        {
            throw new IOException("Media save root must not be a symbolic link or reparse point.");
        }

        var entries = new List<DirectoryDatabaseEntry>();
        var issues = new List<DirectoryDatabaseIssue>();
        var scanned = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false,
        };
        foreach (var path in Directory.EnumerateFiles(root, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(path);
            if (!IsSidecar(fileName))
            {
                continue;
            }

            scanned++;
            var relativePath = NormalizeRelative(root, path);
            try
            {
                var info = new FileInfo(path);
                if (IsSymbolic(info))
                {
                    throw new SidecarException("directory_database_symbolic_file");
                }
                if (info.Length is <= 0 or > MaximumSidecarBytes)
                {
                    throw new SidecarException("directory_database_size_invalid");
                }

                entries.Add(await ReadAsync(
                    path,
                    relativePath,
                    Classify(fileName),
                    cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (SidecarException exception)
            {
                issues.Add(new DirectoryDatabaseIssue(relativePath, exception.Code));
            }
            catch (JsonException)
            {
                issues.Add(new DirectoryDatabaseIssue(relativePath, "directory_database_json_invalid"));
            }
            catch (IOException)
            {
                issues.Add(new DirectoryDatabaseIssue(relativePath, "directory_database_read_failed"));
            }
            catch (UnauthorizedAccessException)
            {
                issues.Add(new DirectoryDatabaseIssue(relativePath, "directory_database_read_denied"));
            }
        }

        return new DirectoryDatabaseScanResult(scanned, entries, issues);
    }

    internal static async Task<DirectoryDatabaseEntry> ReadAsync(
        string path,
        string relativePath,
        DirectoryDatabaseEntryKind kind,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var document = await JsonDocument.ParseAsync(
            stream,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            },
            cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("info", out var info)
            || info.ValueKind != JsonValueKind.Object)
        {
            throw new SidecarException("directory_database_shape_invalid");
        }

        var hash = RequiredString(info, "hash", allowEmpty: true);
        var name = RequiredString(info, "name", allowEmpty: false);
        var createAt = RequiredNonNegativeInt64(info, "create_at");
        var updateAt = RequiredNonNegativeInt64(info, "update_at");
        if (kind == DirectoryDatabaseEntryKind.Anime)
        {
            return new DirectoryDatabaseEntry(
                relativePath, kind, hash, name, createAt, updateAt);
        }

        var season = RequiredPositiveInt32(root, "season");
        if (kind == DirectoryDatabaseEntryKind.Season)
        {
            return new DirectoryDatabaseEntry(
                relativePath, kind, hash, name, createAt, updateAt, season);
        }

        if (!root.TryGetProperty("state", out var state)
            || state.ValueKind != JsonValueKind.Object)
        {
            throw new SidecarException("directory_database_shape_invalid");
        }
        var type = RequiredInt32(root, "type");
        var episode = RequiredInt32(root, "ep");
        if (type is < 0 or > 2 || episode < 0 || (type == 1 && episode == 0))
        {
            throw new SidecarException("directory_database_episode_invalid");
        }

        return new DirectoryDatabaseEntry(
            relativePath,
            kind,
            hash,
            name,
            createAt,
            updateAt,
            season,
            type,
            episode,
            RequiredBoolean(state, "seeded"),
            RequiredBoolean(state, "downloaded"),
            RequiredBoolean(state, "renamed"),
            RequiredBoolean(state, "scraped"));
    }

    internal static string NormalizeRelative(string root, string path)
    {
        if (!PathBoundary.IsWithin(root, path))
        {
            throw new SidecarException("directory_database_path_outside_root");
        }
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    private static bool IsSidecar(string fileName) =>
        fileName.Equals(AnimeFileName, StringComparison.Ordinal)
        || fileName.Equals(SeasonFileName, StringComparison.Ordinal)
        || fileName.EndsWith(EpisodeSuffix, StringComparison.Ordinal);

    private static DirectoryDatabaseEntryKind Classify(string fileName) =>
        fileName.Equals(AnimeFileName, StringComparison.Ordinal)
            ? DirectoryDatabaseEntryKind.Anime
            : fileName.Equals(SeasonFileName, StringComparison.Ordinal)
                ? DirectoryDatabaseEntryKind.Season
                : DirectoryDatabaseEntryKind.Episode;

    private static bool IsSymbolic(FileSystemInfo info) =>
        info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget is not null;

    private static string RequiredString(JsonElement parent, string name, bool allowEmpty)
    {
        if (!parent.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw new SidecarException("directory_database_shape_invalid");
        }
        var text = value.GetString()!;
        if ((!allowEmpty && string.IsNullOrWhiteSpace(text)) || text.Length > 1024)
        {
            throw new SidecarException("directory_database_value_invalid");
        }
        return text;
    }

    private static long RequiredNonNegativeInt64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)
            || !value.TryGetInt64(out var result)
            || result < 0)
        {
            throw new SidecarException("directory_database_value_invalid");
        }
        return result;
    }

    private static int RequiredPositiveInt32(JsonElement parent, string name)
    {
        var value = RequiredInt32(parent, name);
        if (value <= 0)
        {
            throw new SidecarException("directory_database_value_invalid");
        }
        return value;
    }

    private static int RequiredInt32(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
        {
            throw new SidecarException("directory_database_value_invalid");
        }
        return result;
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)
            || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new SidecarException("directory_database_value_invalid");
        }
        return value.GetBoolean();
    }

    internal sealed class SidecarException(string code) : Exception(code)
    {
        public string Code { get; } = StableErrorCode.Require(code, nameof(code));
    }
}
