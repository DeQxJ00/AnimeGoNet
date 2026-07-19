using System.Security.Cryptography;
using System.Text;
using AnimeGoNet.Core.Torrents;

namespace AnimeGoNet.Core.Tests.Torrents;

public sealed class TorrentMetainfoParserTests
{
    [Fact]
    public void ParsesSingleFileAndHashesOriginalInfoBytesWithoutExposingAnnounce()
    {
        var info = "d6:lengthi5e4:name8:ep01.mkv12:piece lengthi16384e6:pieces20:aaaaaaaaaaaaaaaaaaaae";
        var bytes = Encoding.UTF8.GetBytes($"d8:announce20:https://secret/token4:info{info}e");

        var result = TorrentMetainfoParser.Parse(bytes);

        Assert.Equal("ep01.mkv", result.Name);
        Assert.Equal(5, result.TotalSize);
        var file = Assert.Single(result.Files);
        Assert.Equal("ep01.mkv", file.RelativePath);
        Assert.Equal(5, file.Size);
#pragma warning disable CA5350
        Assert.Equal(Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(info))), result.InfoHash);
#pragma warning restore CA5350
        Assert.DoesNotContain("secret", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ParsesMultiFilePathsAndMarksPadding()
    {
        const string files = "ld6:lengthi3e4:pathl8:ep01.mkveed6:lengthi2e4:pathl18:_____padding_file0eee";
        var bytes = Encoding.UTF8.GetBytes(
            $"d4:infod5:files{files}4:name4:Show12:piece lengthi16384e6:pieces20:aaaaaaaaaaaaaaaaaaaaee");

        var result = TorrentMetainfoParser.Parse(bytes);

        Assert.Equal(5, result.TotalSize);
        Assert.Collection(
            result.Files,
            file =>
            {
                Assert.Equal("Show/ep01.mkv", file.RelativePath);
                Assert.False(file.IsPadding);
            },
            file => Assert.True(file.IsPadding));
    }

    [Theory]
    [InlineData("d4:infod6:lengthi1e4:name5:../x1e")]
    [InlineData("d4:infod6:lengthi1e4:name1:x12:piece lengthi1e6:pieces20:aaaaaaaaaaaaaaaaaaaaeejunk")]
    [InlineData("d4:infod6:lengthi1e4:name1:x12:piece lengthi1e6:pieces0:ee")]
    public void RejectsUnsafeOrMalformedMetainfo(string encoded)
    {
        Assert.Throws<TorrentMetainfoException>(() => TorrentMetainfoParser.Parse(Encoding.UTF8.GetBytes(encoded)));
    }

    [Fact]
    public void RejectsTraversalInMultiFilePath()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "d4:infod5:filesld6:lengthi1e4:pathl2:..1:xeee4:name4:Show12:piece lengthi1e6:pieces20:aaaaaaaaaaaaaaaaaaaaee");

        Assert.Throws<TorrentMetainfoException>(() => TorrentMetainfoParser.Parse(bytes));
    }
}
