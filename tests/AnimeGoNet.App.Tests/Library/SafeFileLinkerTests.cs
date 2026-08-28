using AnimeGoNet.App.Library;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Tests.Library;

public sealed class SafeFileLinkerTests
{
    [Fact]
    public async Task CreatesHardLinkAndPreservesSource()
    {
        await using var fixture = new LinkFixture();
        var source = fixture.CreateSource("torrent/episode.mkv", [1, 2, 3, 4]);
        var target = fixture.Target("Series/S01/E001.mkv");
        var linker = new SafeFileLinker();

        var result = await linker.LinkAsync(new SafeFileLinkRequest(
            fixture.SourceRoot, fixture.TargetRoot, source, target, 4));

        Assert.False(result.RecoveredExistingTarget);
        Assert.True(File.Exists(source));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(target));
        await File.WriteAllBytesAsync(source, [9, 8, 7, 6]);
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task ExistingIdenticalTargetIsAnIdempotentRecovery()
    {
        await using var fixture = new LinkFixture();
        var bytes = new byte[] { 7, 8, 9 };
        var source = fixture.CreateSource("episode.mkv", bytes);
        var target = fixture.CreateTarget("Series/S01/E001.mkv", bytes);

        var result = await new SafeFileLinker().LinkAsync(new SafeFileLinkRequest(
            fixture.SourceRoot, fixture.TargetRoot, source, target, bytes.Length));

        Assert.True(result.RecoveredExistingTarget);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task CreatesSymbolicLinkAndPreservesSource()
    {
        await using var fixture = new LinkFixture();
        var bytes = new byte[] { 4, 3, 2, 1 };
        var source = fixture.CreateSource("torrent/episode.mkv", bytes);
        var target = fixture.Target("Series/S01/E001.mkv");
        var linker = new SafeFileLinker();

        SafeFileLinkResult created;
        try
        {
            created = await linker.LinkAsync(
                new SafeFileLinkRequest(
                    fixture.SourceRoot, fixture.TargetRoot, source, target, bytes.Length),
                SourceDownloadPolicy.SymbolicLinkType);
        }
        catch (SafeFileMoveException exception) when (
            OperatingSystem.IsWindows()
            && exception.Code == "symbolic_link_unavailable")
        {
            Assert.True(File.Exists(source));
            Assert.False(File.Exists(target));
            return;
        }
        var recovered = await linker.LinkAsync(
            new SafeFileLinkRequest(
                fixture.SourceRoot, fixture.TargetRoot, source, target, bytes.Length),
            SourceDownloadPolicy.SymbolicLinkType);

        Assert.False(created.RecoveredExistingTarget);
        Assert.True(recovered.RecoveredExistingTarget);
        Assert.NotNull(new FileInfo(target).LinkTarget);
        Assert.Equal(Path.GetFullPath(source), new FileInfo(target).ResolveLinkTarget(true)!.FullName);
        Assert.True(File.Exists(source));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task SymbolicModeRejectsLinkPointingToDifferentSource()
    {
        await using var fixture = new LinkFixture();
        var source = fixture.CreateSource("episode.mkv", [1, 2, 3]);
        var other = fixture.CreateSource("other.mkv", [1, 2, 3]);
        var target = fixture.Target("Series/S01/E001.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try
        {
            File.CreateSymbolicLink(target, other);
        }
        catch (IOException) when (OperatingSystem.IsWindows())
        {
            Assert.True(File.Exists(source));
            Assert.True(File.Exists(other));
            return;
        }

        var error = await Assert.ThrowsAsync<SafeFileMoveException>(() =>
            new SafeFileLinker().LinkAsync(
                new SafeFileLinkRequest(
                    fixture.SourceRoot, fixture.TargetRoot, source, target, 3),
                SourceDownloadPolicy.SymbolicLinkType));

        Assert.Equal("target_conflict", error.Code);
        Assert.True(File.Exists(source));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public async Task LinkDeleteVerifiesTargetBeforeDeletingSource()
    {
        await using var fixture = new LinkFixture();
        var source = fixture.CreateSource("episode.mkv", [1, 2, 3]);
        var target = fixture.Target("Series/S01/E001.mkv");
        var linker = new SafeFileLinker();
        var request = new SafeFileLinkRequest(
            fixture.SourceRoot, fixture.TargetRoot, source, target, 3);
        _ = await linker.LinkAsync(request);

        await linker.DeleteSourceAsync(request);
        await linker.DeleteSourceAsync(request);

        Assert.False(File.Exists(source));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task ConflictingTargetPreservesSource()
    {
        await using var fixture = new LinkFixture();
        var source = fixture.CreateSource("episode.mkv", [1, 2, 3]);
        var target = fixture.CreateTarget("Series/S01/E001.mkv", [3, 2, 1]);

        var error = await Assert.ThrowsAsync<SafeFileMoveException>(() =>
            new SafeFileLinker().LinkAsync(new SafeFileLinkRequest(
                fixture.SourceRoot, fixture.TargetRoot, source, target, 3)));

        Assert.Equal("target_conflict", error.Code);
        Assert.True(File.Exists(source));
    }

    private sealed class LinkFixture : IAsyncDisposable
    {
        public LinkFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "animegonet-file-link-tests", Guid.NewGuid().ToString("N"));
            SourceRoot = Path.Combine(Root, "download");
            TargetRoot = Path.Combine(Root, "library");
            Directory.CreateDirectory(SourceRoot);
            Directory.CreateDirectory(TargetRoot);
        }

        public string Root { get; }

        public string SourceRoot { get; }

        public string TargetRoot { get; }

        public string CreateSource(string relativePath, byte[] bytes) => Create(SourceRoot, relativePath, bytes);

        public string CreateTarget(string relativePath, byte[] bytes) => Create(TargetRoot, relativePath, bytes);

        public string Target(string relativePath) => Path.Combine(TargetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string Create(string root, string relativePath, byte[] bytes)
        {
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
