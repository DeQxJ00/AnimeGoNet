using System.Security.Cryptography;
using AnimeGoNet.App.Downloads;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Library;

namespace AnimeGoNet.App.Library;

public sealed record SafeFileLinkRequest(
    string SourceRoot,
    string TargetRoot,
    string SourcePath,
    string TargetPath,
    long ExpectedBytes);

public sealed record SafeFileLinkResult(long BytesVerified, bool RecoveredExistingTarget);

public sealed class SafeFileLinker
{
    public async Task<SafeFileLinkResult> LinkAsync(
        SafeFileLinkRequest request,
        string linkType = SourceDownloadPolicy.HardLinkType,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var normalizedLinkType = SourceDownloadPolicy.NormalizeLinkType("link", linkType);
        cancellationToken.ThrowIfCancellationRequested();
        RejectSymbolicTraversal(request.SourceRoot, request.SourcePath);
        RejectSymbolicTraversal(request.TargetRoot, Path.GetDirectoryName(request.TargetPath)!);

        if (normalizedLinkType == SourceDownloadPolicy.SymbolicLinkType)
        {
            return await CreateSymbolicLinkAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var sourceExists = File.Exists(request.SourcePath);
        if (new FileInfo(request.TargetPath).LinkTarget is not null)
        {
            throw new SafeFileMoveException(
                "target_conflict", "A symbolic link already exists at the hard-link target path.");
        }
        if (File.Exists(request.TargetPath))
        {
            EnsureExpectedSize(request.TargetPath, request.ExpectedBytes, "target_conflict");
            if (sourceExists)
            {
                EnsureExpectedSize(request.SourcePath, request.ExpectedBytes, "source_size_mismatch");
                await EnsureSameContentAsync(request.SourcePath, request.TargetPath, cancellationToken)
                    .ConfigureAwait(false);
            }

            return new SafeFileLinkResult(request.ExpectedBytes, true);
        }

        if (!sourceExists)
        {
            throw new SafeFileMoveException("source_file_missing", "Source file does not exist.");
        }

        EnsureExpectedSize(request.SourcePath, request.ExpectedBytes, "source_size_mismatch");
        Directory.CreateDirectory(Path.GetDirectoryName(request.TargetPath)!);
        RejectSymbolicTraversal(request.TargetRoot, Path.GetDirectoryName(request.TargetPath)!);
        try
        {
            HardLinkCapability.Create(request.TargetPath, request.SourcePath);
            EnsureExpectedSize(request.TargetPath, request.ExpectedBytes, "target_size_mismatch");
            return new SafeFileLinkResult(request.ExpectedBytes, false);
        }
        catch (SafeFileMoveException)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new SafeFileMoveException("file_access_denied", "Hard link creation was denied.", exception);
        }
        catch (IOException exception)
        {
            throw new SafeFileMoveException(
                File.Exists(request.TargetPath) ? "target_conflict" : "hard_link_unavailable",
                "Hard link creation failed.", exception);
        }
    }

    private static Task<SafeFileLinkResult> CreateSymbolicLinkAsync(
        SafeFileLinkRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(request.SourcePath))
        {
            throw new SafeFileMoveException("source_file_missing", "Source file does not exist.");
        }

        EnsureExpectedSize(request.SourcePath, request.ExpectedBytes, "source_size_mismatch");
        var targetInfo = new FileInfo(request.TargetPath);
        if (targetInfo.Exists || targetInfo.LinkTarget is not null)
        {
            ValidateSymbolicTarget(request, targetInfo);
            return Task.FromResult(new SafeFileLinkResult(request.ExpectedBytes, true));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(request.TargetPath)!);
        RejectSymbolicTraversal(request.TargetRoot, Path.GetDirectoryName(request.TargetPath)!);
        var targetDirectory = Path.GetDirectoryName(request.TargetPath)!;
        var relativeSource = Path.GetRelativePath(targetDirectory, request.SourcePath);
        var storedTarget = Path.IsPathRooted(relativeSource)
            ? request.SourcePath
            : relativeSource;
        try
        {
            File.CreateSymbolicLink(request.TargetPath, storedTarget);
            ValidateSymbolicTarget(request, new FileInfo(request.TargetPath));
            return Task.FromResult(new SafeFileLinkResult(request.ExpectedBytes, false));
        }
        catch (SafeFileMoveException)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new SafeFileMoveException(
                "file_access_denied", "Symbolic link creation was denied.", exception);
        }
        catch (IOException exception)
        {
            throw new SafeFileMoveException(
                "symbolic_link_unavailable", "Symbolic link creation failed.", exception);
        }
    }

    private static void ValidateSymbolicTarget(
        SafeFileLinkRequest request,
        FileInfo targetInfo)
    {
        if (targetInfo.LinkTarget is null)
        {
            throw new SafeFileMoveException(
                "target_conflict", "The existing target is not a symbolic link.");
        }

        FileSystemInfo? resolved;
        try
        {
            resolved = targetInfo.ResolveLinkTarget(returnFinalTarget: true);
        }
        catch (IOException exception)
        {
            throw new SafeFileMoveException(
                "target_conflict", "The symbolic link target cannot be resolved.", exception);
        }
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (resolved is null
            || !string.Equals(
                Path.GetFullPath(resolved.FullName),
                Path.GetFullPath(request.SourcePath),
                comparison))
        {
            throw new SafeFileMoveException(
                "target_conflict", "The symbolic link points to a different source file.");
        }

        EnsureExpectedSize(request.TargetPath, request.ExpectedBytes, "target_size_mismatch");
    }

    public async Task DeleteSourceAsync(
        SafeFileLinkRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        cancellationToken.ThrowIfCancellationRequested();
        RejectSymbolicTraversal(request.SourceRoot, request.SourcePath);
        RejectSymbolicTraversal(request.TargetRoot, request.TargetPath);
        if (!File.Exists(request.TargetPath))
        {
            throw new SafeFileMoveException(
                "linked_target_missing",
                "Linked media target is missing; source deletion was refused.");
        }

        EnsureExpectedSize(request.TargetPath, request.ExpectedBytes, "target_conflict");
        if (!File.Exists(request.SourcePath))
        {
            return;
        }

        EnsureExpectedSize(request.SourcePath, request.ExpectedBytes, "source_size_mismatch");
        await EnsureSameContentAsync(request.SourcePath, request.TargetPath, cancellationToken).ConfigureAwait(false);
        try
        {
            File.Delete(request.SourcePath);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new SafeFileMoveException("source_cleanup_failed", "Linked source cleanup was denied.", exception);
        }
        catch (IOException exception)
        {
            throw new SafeFileMoveException("source_cleanup_failed", "Linked source cleanup failed.", exception);
        }
    }

    private static async Task EnsureSameContentAsync(
        string sourcePath,
        string targetPath,
        CancellationToken cancellationToken)
    {
        var sourceHash = await HashAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var targetHash = await HashAsync(targetPath, cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(sourceHash, targetHash))
        {
            throw new SafeFileMoveException("target_conflict", "Target exists with different content.");
        }
    }

    private static async Task<byte[]> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureExpectedSize(string path, long expectedBytes, string code)
    {
        if (FilePathInspector.GetResolvedFileLength(path) != expectedBytes)
        {
            throw new SafeFileMoveException(code, "File size does not match the Torrent manifest.");
        }
    }

    private static void Validate(SafeFileLinkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegative(request.ExpectedBytes);
        if (!PathBoundary.IsWithin(request.SourceRoot, request.SourcePath))
        {
            throw new SafeFileMoveException("source_path_outside_root", "Source path is outside the captured download root.");
        }

        if (!PathBoundary.IsWithin(request.TargetRoot, request.TargetPath))
        {
            throw new SafeFileMoveException("target_path_outside_root", "Target path is outside the captured save root.");
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(Path.GetFullPath(request.SourcePath), Path.GetFullPath(request.TargetPath), comparison))
        {
            throw new SafeFileMoveException("source_target_same_path", "Source and target paths must differ.");
        }
    }

    private static void RejectSymbolicTraversal(string root, string candidate)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullCandidate = Path.GetFullPath(candidate);
        var relative = Path.GetRelativePath(fullRoot, fullCandidate);
        var current = fullRoot;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            if (info.Exists && (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget is not null))
            {
                throw new SafeFileMoveException("symbolic_path_not_allowed", "Symbolic links are not allowed in file operation paths.");
            }
        }
    }
}
