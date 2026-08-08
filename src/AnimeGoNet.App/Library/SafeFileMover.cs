using System.Security.Cryptography;
using System.Text;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Diagnostics;

namespace AnimeGoNet.App.Library;

public sealed record SafeFileMoveRequest(
    string OperationId,
    string SourceRoot,
    string TargetRoot,
    string SourcePath,
    string TargetPath,
    long ExpectedBytes,
    bool ForceCopyAndVerify = false);

public sealed record SafeFileMoveResult(long BytesVerified, bool RecoveredExistingTarget);

public sealed class SafeFileMoveException(string code, string message, Exception? innerException = null)
    : IOException(message, innerException)
{
    public string Code { get; } = StableErrorCode.Require(code, nameof(code));
}

public sealed class SafeFileMover
{
    public async Task<SafeFileMoveResult> MoveAsync(
        SafeFileMoveRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        cancellationToken.ThrowIfCancellationRequested();
        RejectSymbolicTraversal(request.SourceRoot, request.SourcePath);
        RejectSymbolicTraversal(request.TargetRoot, Path.GetDirectoryName(request.TargetPath)!);

        var sourceExists = File.Exists(request.SourcePath);
        var targetExists = File.Exists(request.TargetPath);
        if (targetExists)
        {
            return await RecoverExistingTargetAsync(request, sourceExists, cancellationToken).ConfigureAwait(false);
        }

        if (!sourceExists)
        {
            throw new SafeFileMoveException("source_file_missing", "Source file does not exist.");
        }

        EnsureExpectedSize(request.SourcePath, request.ExpectedBytes, "source_size_mismatch");
        Directory.CreateDirectory(Path.GetDirectoryName(request.TargetPath)!);
        RejectSymbolicTraversal(request.TargetRoot, Path.GetDirectoryName(request.TargetPath)!);

        if (!request.ForceCopyAndVerify)
        {
            try
            {
                File.Move(request.SourcePath, request.TargetPath, overwrite: false);
                EnsureExpectedSize(request.TargetPath, request.ExpectedBytes, "target_size_mismatch");
                return new SafeFileMoveResult(request.ExpectedBytes, false);
            }
            catch (IOException) when (!File.Exists(request.TargetPath))
            {
                // Cross-device moves are completed by verified copy below.
            }
        }

        return await CopyVerifyAndDeleteAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<SafeFileMoveResult> RecoverExistingTargetAsync(
        SafeFileMoveRequest request,
        bool sourceExists,
        CancellationToken cancellationToken)
    {
        EnsureExpectedSize(request.TargetPath, request.ExpectedBytes, "target_conflict");
        if (!sourceExists)
        {
            return new SafeFileMoveResult(request.ExpectedBytes, true);
        }

        EnsureExpectedSize(request.SourcePath, request.ExpectedBytes, "source_size_mismatch");
        var sourceHash = await HashAsync(request.SourcePath, cancellationToken).ConfigureAwait(false);
        var targetHash = await HashAsync(request.TargetPath, cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(sourceHash, targetHash))
        {
            throw new SafeFileMoveException("target_conflict", "Target exists with different content.");
        }

        try
        {
            File.Delete(request.SourcePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SafeFileMoveException("source_cleanup_failed", "Verified target exists but source cleanup failed.", exception);
        }

        return new SafeFileMoveResult(request.ExpectedBytes, true);
    }

    private static async Task<SafeFileMoveResult> CopyVerifyAndDeleteAsync(
        SafeFileMoveRequest request,
        CancellationToken cancellationToken)
    {
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.OperationId)))[..16];
        var partialPath = request.TargetPath + $".animegonet-{token}.partial";
        try
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }

            await using (var source = new FileStream(
                request.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var target = new FileStream(
                partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
                target.Flush(flushToDisk: true);
            }

            EnsureExpectedSize(partialPath, request.ExpectedBytes, "copy_size_mismatch");
            var sourceHash = await HashAsync(request.SourcePath, cancellationToken).ConfigureAwait(false);
            var copyHash = await HashAsync(partialPath, cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(sourceHash, copyHash))
            {
                throw new SafeFileMoveException("copy_hash_mismatch", "Copied file checksum does not match source.");
            }

            File.Move(partialPath, request.TargetPath, overwrite: false);
            try
            {
                File.Delete(request.SourcePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new SafeFileMoveException("source_cleanup_failed", "Verified copy was committed but source cleanup failed.", exception);
            }

            return new SafeFileMoveResult(request.ExpectedBytes, false);
        }
        catch (SafeFileMoveException)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new SafeFileMoveException("file_access_denied", "File move access was denied.", exception);
        }
        catch (IOException exception)
        {
            throw new SafeFileMoveException(
                File.Exists(request.TargetPath) ? "target_conflict" : "file_move_io_error",
                "Verified file move failed.",
                exception);
        }
        finally
        {
            if (File.Exists(partialPath))
            {
                try
                {
                    File.Delete(partialPath);
                }
                catch
                {
                    // A task-owned partial is harmless and can be removed by the next retry.
                }
            }
        }
    }

    private static async Task<byte[]> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureExpectedSize(string path, long expectedBytes, string code)
    {
        if (new FileInfo(path).Length != expectedBytes)
        {
            throw new SafeFileMoveException(code, "File size does not match the Torrent manifest.");
        }
    }

    private static void Validate(SafeFileMoveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OperationId);
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
