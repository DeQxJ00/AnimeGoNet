using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Diagnostics;

namespace AnimeGoNet.App.Deletion;

public sealed class SafeFileDeleteException(string code, string message, Exception? innerException = null)
    : IOException(message, innerException)
{
    public string Code { get; } = StableErrorCode.Require(code, nameof(code));
}

public sealed class SafeFileDeleter
{
    public Task<bool> DeleteAsync(
        string capturedRoot,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(capturedRoot) || !PathBoundary.IsWithin(capturedRoot, targetPath))
        {
            throw new SafeFileDeleteException("delete_path_outside_root", "Delete path is outside the captured root.");
        }

        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(capturedRoot));
        var fullTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetPath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(fullRoot, fullTarget, comparison))
        {
            throw new SafeFileDeleteException("delete_root_not_allowed", "Captured root cannot be deleted.");
        }

        RejectSymbolicTraversal(fullRoot, fullTarget);
        if (Directory.Exists(fullTarget))
        {
            throw new SafeFileDeleteException("delete_target_not_file", "Delete target is a directory.");
        }

        if (!File.Exists(fullTarget))
        {
            return Task.FromResult(false);
        }

        try
        {
            File.Delete(fullTarget);
            return Task.FromResult(true);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new SafeFileDeleteException("delete_access_denied", "File deletion access was denied.", exception);
        }
        catch (IOException exception)
        {
            throw new SafeFileDeleteException("delete_file_io_error", "File deletion failed.", exception);
        }
    }

    private static void RejectSymbolicTraversal(string root, string target)
    {
        var relative = Path.GetRelativePath(root, target);
        var current = root;
        Check(current);
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            Check(current);
        }

        static void Check(string path)
        {
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            if (info.Exists && (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget is not null))
            {
                throw new SafeFileDeleteException("delete_symbolic_path_not_allowed", "Symbolic links are not allowed in delete paths.");
            }
        }
    }
}
