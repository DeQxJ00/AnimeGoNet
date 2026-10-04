using AnimeGoNet.Core.Library;

namespace AnimeGoNet.Core.Tests.Library;

public sealed class PortableFilePathsTests
{
    [Theory]
    [InlineData(@"D:\TV", @"d:\tv\抚子号\S01\E024.mkv", "抚子号/S01/E024.mkv")]
    [InlineData("/media/tv", "/media/tv/抚子号/S01/E024.mkv", "抚子号/S01/E024.mkv")]
    [InlineData(@"\\nas\tv", @"\\nas\tv\Show\S01\E001.mkv", "Show/S01/E001.mkv")]
    [InlineData("/", "/Show/E001.mkv", "Show/E001.mkv")]
    [InlineData(@"D:\", @"D:\Show\E001.mkv", "Show/E001.mkv")]
    public void LegacyPathsAreParsedIndependentlyOfHost(string root, string full, string relative)
    {
        Assert.True(PortableFilePaths.TryMakeRelative(root, full, out var actual));
        Assert.Equal(relative, actual);
    }

    [Theory]
    [InlineData(@"D:\TV", @"D:\TV2\E001.mkv")]
    [InlineData("/media/tv", "/media/TV/E001.mkv")]
    [InlineData("/media/tv", "/media/tv/../secret")]
    [InlineData(@"D:\TV", "/media/tv/E001.mkv")]
    public void RejectsOutsideAndForeignRoot(string root, string full) =>
        Assert.False(PortableFilePaths.TryMakeRelative(root, full, out _));

    [Theory]
    [InlineData("../secret")]
    [InlineData("Show/../../secret")]
    [InlineData(@"..\secret")]
    [InlineData("/absolute")]
    [InlineData(@"C:\absolute")]
    [InlineData(@"\\server\share")]
    [InlineData("C:drive-relative")]
    public void RejectsNonRelativePaths(string relative) =>
        Assert.Throws<PathMappingException>(() => PortableFilePaths.NormalizeRelative(relative));

    [Fact]
    public void SameRecordResolvesForBothPlatforms()
    {
        const string relative = "抚子号/S01/Extras/SP/预告.mkv";
        Assert.Equal(@"D:\TV\抚子号\S01\Extras\SP\预告.mkv", PortableFilePaths.Resolve(@"D:\TV", relative, windows: true));
        Assert.Equal("/media/tv/抚子号/S01/Extras/SP/预告.mkv", PortableFilePaths.Resolve("/media/tv", relative, windows: false));
        Assert.Equal(@"\\nas\tv\抚子号\S01\Extras\SP\预告.mkv", PortableFilePaths.Resolve(@"\\nas\tv", relative, windows: true));
    }

    [Theory]
    [InlineData("CON.mkv")]
    [InlineData("show/name?.mkv")]
    [InlineData("show /E001.mkv")]
    [InlineData("LPT1.txt")]
    [InlineData("file:stream")]
    public void WindowsIncompatibleNamesAreNotSilentlyRenamed(string relative) =>
        Assert.Throws<PathMappingException>(() => PortableFilePaths.Resolve(@"D:\TV", relative, windows: true));

    [Fact]
    public void PosixNamesArePreservedAndOnlyRejectedWhenIncompatibleWithTarget()
    {
        Assert.True(PortableFilePaths.TryMakeRelative("/tv", "/tv/Show: Part 2.mkv", out var relative));
        Assert.Equal("/tv/Show: Part 2.mkv", PortableFilePaths.Resolve("/tv", relative, windows: false));
        Assert.Throws<PathMappingException>(() => PortableFilePaths.Resolve(@"D:\TV", relative, windows: true));
        Assert.False(PortableFilePaths.TryMakeRelative("/tv", @"/tv/literal\slash.mkv", out _));
    }
}
