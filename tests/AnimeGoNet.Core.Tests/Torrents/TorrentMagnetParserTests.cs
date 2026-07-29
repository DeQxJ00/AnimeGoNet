using AnimeGoNet.Core.Torrents;

namespace AnimeGoNet.Core.Tests.Torrents;

public sealed class TorrentMagnetParserTests
{
    private const string ExpectedHash =
        "f6aa232b3024073c90d04614fcbf050d94fe8ad6";

    [Fact]
    public void ParsesUpstreamHexInfoHash()
    {
        var result = TorrentMagnetParser.Parse(
            $"magnet:?xt=urn:btih:{ExpectedHash}");

        Assert.Equal(ExpectedHash, result.InfoHash);
        Assert.Equal(string.Empty, result.DisplayName);
        Assert.Equal(0, result.TrackerCount);
    }

    [Fact]
    public void ParsesUpstreamBase32InfoHash()
    {
        var result = TorrentMagnetParser.Parse(
            "magnet:?xt=urn:btih:62VCGKZQEQDTZEGQIYKPZPYFBWKP5CWW");

        Assert.Equal(ExpectedHash, result.InfoHash);
    }

    [Fact]
    public void DecodesFirstDisplayNameAndCountsTrackersWithoutRetainingUrls()
    {
        const string magnet =
            "magnet:?xt=urn%3Abtih%3AF6AA232B3024073C90D04614FCBF050D94FE8AD6" +
            "&dn=Re%3AZero+S04" +
            "&tr=https%3A%2F%2Ftracker.example%2Fannounce%3Fpasskey%3Dsecret-one" +
            "&tr=udp%3A%2F%2Ftracker.example%3A6969%2Fannounce" +
            "&dn=ignored";

        var result = TorrentMagnetParser.Parse(magnet);

        Assert.Equal(ExpectedHash, result.InfoHash);
        Assert.Equal("Re:Zero S04", result.DisplayName);
        Assert.Equal(2, result.TrackerCount);
        Assert.DoesNotContain(
            "tracker.example",
            result.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-one", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UsesFirstExactTopicLikeUpstream()
    {
        var result = TorrentMagnetParser.Parse(
            $"magnet:?xt=urn:btih:{ExpectedHash}&xt=urn:btih:not-used");

        Assert.Equal(ExpectedHash, result.InfoHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("MAGNET:?xt=urn:btih:f6aa232b3024073c90d04614fcbf050d94fe8ad6")]
    [InlineData("magnet:")]
    [InlineData("magnet:?dn=missing")]
    [InlineData("magnet:?xt=urn:sha1:f6aa232b3024073c90d04614fcbf050d94fe8ad6")]
    [InlineData("magnet:?xt=urn:btih:too-short")]
    [InlineData("magnet:?xt=urn:btih:62vcgkzqeqdtzegqiykpzpyfbwkp5cww")]
    [InlineData("magnet:?xt=urn%ZZbtih%3Af6aa232b3024073c90d04614fcbf050d94fe8ad6")]
    [InlineData("magnet:?xt=urn:btih:f6aa232b3024073c90d04614fcbf050d94fe8ad6&dn=%00")]
    public void RejectsInvalidMagnets(string value)
    {
        Assert.Throws<TorrentMagnetException>(
            () => TorrentMagnetParser.Parse(value));
    }

    [Fact]
    public void InvalidDiagnosticNeverEchoesRawUriOrTrackerSecret()
    {
        const string secret = "private-passkey-value";
        var exception = Assert.Throws<TorrentMagnetException>(
            () => TorrentMagnetParser.Parse(
                "magnet:?xt=urn:btih:not-a-hash" +
                $"&tr=https%3A%2F%2Ftracker.example%2Fannounce%3Fpasskey%3D{secret}"));

        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("tracker.example", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("not-a-hash", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsOversizedInputBeforeParsingSecrets()
    {
        var value =
            $"magnet:?xt=urn:btih:{ExpectedHash}&tr=" +
            new string('s', 17 * 1024);

        Assert.Throws<TorrentMagnetException>(
            () => TorrentMagnetParser.Parse(value));
    }
}
