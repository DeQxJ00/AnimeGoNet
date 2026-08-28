using AnimeGoNet.App.Deletion;

namespace AnimeGoNet.App.Tests.Deletion;

public sealed class SafeFileDeleterTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "animegonet-delete-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DeletesOnlyAnExactFileWithinCapturedRoot()
    {
        var path = Path.Combine(_root, "nested", "episode.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, [1, 2, 3]);

        Assert.True(await new SafeFileDeleter().DeleteAsync(_root, path));
        Assert.False(File.Exists(path));
        Assert.True(Directory.Exists(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public async Task MissingFileIsIdempotentlySkipped()
    {
        Directory.CreateDirectory(_root);
        Assert.False(await new SafeFileDeleter().DeleteAsync(_root, Path.Combine(_root, "missing.mkv")));
    }

    [Fact]
    public async Task DeletesSymbolicMediaLinkWithoutDeletingItsSource()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), "animegonet-delete-link-source", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(sourceRoot, "episode.mkv");
        var target = Path.Combine(_root, "Series", "S01", "E001.mkv");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        try
        {
            try
            {
                File.CreateSymbolicLink(target, source);
            }
            catch (IOException) when (OperatingSystem.IsWindows())
            {
                Assert.True(File.Exists(source));
                return;
            }

            Assert.True(await new SafeFileDeleter().DeleteAsync(_root, target));
            Assert.False(new FileInfo(target).Exists);
            Assert.Null(new FileInfo(target).LinkTarget);
            Assert.True(File.Exists(source));
        }
        finally
        {
            if (Directory.Exists(sourceRoot))
            {
                Directory.Delete(sourceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RejectsOutsidePathRootAndDirectoryTargets()
    {
        Directory.CreateDirectory(_root);
        var outside = Path.Combine(Path.GetDirectoryName(_root)!, "outside.mkv");
        var outsideError = await Assert.ThrowsAsync<SafeFileDeleteException>(() =>
            new SafeFileDeleter().DeleteAsync(_root, outside));
        Assert.Equal("delete_path_outside_root", outsideError.Code);

        var directoryError = await Assert.ThrowsAsync<SafeFileDeleteException>(() =>
            new SafeFileDeleter().DeleteAsync(_root, _root));
        Assert.Equal("delete_root_not_allowed", directoryError.Code);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
