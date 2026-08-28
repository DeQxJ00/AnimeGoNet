namespace AnimeGoNet.Core.Library;

public static class FilePathInspector
{
    public static bool HasExpectedFileLength(string path, long expectedBytes)
    {
        try
        {
            return File.Exists(path) && GetResolvedFileLength(path) == expectedBytes;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static long GetResolvedFileLength(string path)
    {
        var file = new FileInfo(path);
        if (file.LinkTarget is null)
        {
            return file.Length;
        }

        return file.ResolveLinkTarget(returnFinalTarget: true) is FileInfo resolved
            ? resolved.Length
            : file.Length;
    }

    public static bool TryResolveSymbolicFileTarget(string path, out string targetPath)
    {
        targetPath = string.Empty;
        try
        {
            var file = new FileInfo(path);
            if (file.LinkTarget is null
                || file.ResolveLinkTarget(returnFinalTarget: true) is not FileInfo resolved)
            {
                return false;
            }

            targetPath = Path.GetFullPath(resolved.FullName);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
