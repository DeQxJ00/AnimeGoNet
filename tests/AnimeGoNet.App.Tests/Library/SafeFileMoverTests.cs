using AnimeGoNet.App.Library;

namespace AnimeGoNet.App.Tests.Library;

public sealed class SafeFileMoverTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MovesWithinRootsAndVerifiesExpectedBytes(bool forceCopy)
    {
        await using var fixture = new MoveFixture();
        var source = fixture.CreateSource("torrent/episode.mkv", [1, 2, 3, 4]);
        var target = fixture.Target("Series/S01/E001.mkv");

        var result = await new SafeFileMover().MoveAsync(new SafeFileMoveRequest(
            "operation-1", fixture.SourceRoot, fixture.TargetRoot, source, target, 4, forceCopy));

        Assert.Equal(4, result.BytesVerified);
        Assert.False(result.RecoveredExistingTarget);
        Assert.False(File.Exists(source));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task RetryWithIdenticalCommittedTargetDeletesOnlySource()
    {
        await using var fixture = new MoveFixture();
        var bytes = new byte[] { 7, 8, 9 };
        var source = fixture.CreateSource("episode.mkv", bytes);
        var target = fixture.CreateTarget("Series/S01/E001.mkv", bytes);

        var result = await new SafeFileMover().MoveAsync(new SafeFileMoveRequest(
            "operation-2", fixture.SourceRoot, fixture.TargetRoot, source, target, bytes.Length));

        Assert.True(result.RecoveredExistingTarget);
        Assert.False(File.Exists(source));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task ConflictingTargetPreservesSourceAndTarget()
    {
        await using var fixture = new MoveFixture();
        var source = fixture.CreateSource("episode.mkv", [1, 2, 3]);
        var target = fixture.CreateTarget("Series/S01/E001.mkv", [3, 2, 1]);

        var error = await Assert.ThrowsAsync<SafeFileMoveException>(() => new SafeFileMover().MoveAsync(
            new SafeFileMoveRequest("operation-3", fixture.SourceRoot, fixture.TargetRoot, source, target, 3)));

        Assert.Equal("target_conflict", error.Code);
        Assert.True(File.Exists(source));
        Assert.Equal(new byte[] { 3, 2, 1 }, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task PathEscapeIsRejectedBeforeFilesystemMutation()
    {
        await using var fixture = new MoveFixture();
        var source = fixture.CreateSource("episode.mkv", [1]);
        var outside = Path.Combine(fixture.Root, "outside.mkv");

        var error = await Assert.ThrowsAsync<SafeFileMoveException>(() => new SafeFileMover().MoveAsync(
            new SafeFileMoveRequest("operation-4", fixture.SourceRoot, fixture.TargetRoot, source, outside, 1)));

        Assert.Equal("target_path_outside_root", error.Code);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(outside));
    }

    private sealed class MoveFixture : IAsyncDisposable
    {
        public MoveFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "animegonet-file-move-tests", Guid.NewGuid().ToString("N"));
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
