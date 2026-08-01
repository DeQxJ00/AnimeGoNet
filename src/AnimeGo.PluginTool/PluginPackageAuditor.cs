using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using AnimeGoNet.App.Plugins;

namespace AnimeGo.PluginTool;

internal sealed record AuditedPluginFile(
    string FullPath,
    string EntryName,
    long Length,
    byte[] Sha256);

internal sealed record AuditedPluginPackage(
    ExternalPluginPackage Package,
    IReadOnlyList<AuditedPluginFile> Files,
    long TotalBytes,
    string ContentSha256)
{
    public PluginToolPackageOutput ToOutput() => new(
        Package.Manifest.Id,
        Package.Manifest.Name,
        Package.Manifest.Version,
        Package.Manifest.ApiVersion,
        Package.Manifest.Type,
        Package.Manifest.Rid,
        Package.Manifest.EntryPoint,
        Package.Manifest.ConfigSchema,
        Package.Manifest.Capabilities,
        Files.Count,
        TotalBytes,
        ContentSha256);
}

internal sealed class PluginPackageAuditor
{
    private readonly int _maximumEntries = 8192;
    private readonly int _maximumFiles = 4096;
    private readonly long _maximumFileBytes = 256L * 1024 * 1024;
    private readonly long _maximumPackageBytes = 512L * 1024 * 1024;

    public async Task<AuditedPluginPackage> AuditAsync(
        ExternalPluginPackage package,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        var root = Path.GetFullPath(package.DirectoryPath);
        EnsureSafe(root, isDirectory: true);
        var files = new List<AuditedPluginFile>();
        var entries = 0;
        long totalBytes = 0;
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0,
        };
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(root);
        while (pendingDirectories.TryPop(out var directory))
        {
            EnsureSafe(directory, isDirectory: true);
            foreach (var path in Directory.EnumerateFileSystemEntries(
                         directory,
                         "*",
                         enumeration))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++entries > _maximumEntries)
                {
                    throw Invalid("plugin_package_entry_limit");
                }
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw Invalid("plugin_package_link_disallowed");
                }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    EnsureSafe(path, isDirectory: true);
                    pendingDirectories.Push(path);
                    continue;
                }
                EnsureSafe(path, isDirectory: false);
                var info = new FileInfo(path);
                if (!info.Exists || info.Length < 0 || info.Length > _maximumFileBytes)
                {
                    throw Invalid("plugin_package_file_size_invalid");
                }
                if (files.Count >= _maximumFiles
                    || info.Length > _maximumPackageBytes - totalBytes)
                {
                    throw Invalid("plugin_package_size_invalid");
                }
                var relative = Path.GetRelativePath(root, info.FullName);
                if (!ValidRelativePath(relative))
                {
                    throw Invalid("plugin_package_path_invalid");
                }
                await using var stream = new FileStream(
                    info.FullName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var hash = await SHA256.HashDataAsync(stream, cancellationToken)
                    .ConfigureAwait(false);
                if (stream.Length != info.Length || new FileInfo(info.FullName).Length != info.Length)
                {
                    throw Invalid("plugin_package_changed");
                }
                totalBytes += info.Length;
                files.Add(new AuditedPluginFile(
                    info.FullName,
                    relative.Replace(Path.DirectorySeparatorChar, '/'),
                    info.Length,
                    hash));
            }
        }
        files.Sort(static (left, right) =>
            string.Compare(left.EntryName, right.EntryName, StringComparison.Ordinal));
        using var contentHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var length = new byte[sizeof(long)];
        foreach (var file in files)
        {
            contentHash.AppendData(Encoding.UTF8.GetBytes(file.EntryName));
            contentHash.AppendData([0]);
            BinaryPrimitives.WriteInt64LittleEndian(length, file.Length);
            contentHash.AppendData(length);
            contentHash.AppendData(file.Sha256);
        }
        return new AuditedPluginPackage(
            package,
            files,
            totalBytes,
            Convert.ToHexStringLower(contentHash.GetHashAndReset()));
    }

    private static bool ValidRelativePath(string path) =>
        path.Length is > 0 and <= 4096
        && !Path.IsPathRooted(path)
        && path is not "." and not ".."
        && !path.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        && path.Split(Path.DirectorySeparatorChar).All(segment =>
            segment.Length > 0 && segment is not "." and not "..");

    private static void EnsureSafe(string path, bool isDirectory)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0
            || isDirectory != ((attributes & FileAttributes.Directory) != 0))
        {
            throw Invalid("plugin_package_path_invalid");
        }
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(path);
            if ((mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
            {
                throw Invalid("plugin_permissions_unsafe");
            }
        }
    }

    private static PluginToolException Invalid(string code) =>
        new(code, "The plugin package tree is outside the supported safety limits.", 3);
}
