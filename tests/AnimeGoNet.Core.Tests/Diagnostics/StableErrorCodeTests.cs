using AnimeGoNet.Core.DataUpdate;
using AnimeGoNet.Core.Diagnostics;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Core.Scheduling;
using AnimeGoNet.Core.Torrents;

namespace AnimeGoNet.Core.Tests.Diagnostics;

public sealed class StableErrorCodeTests
{
    [Theory]
    [InlineData("rss_empty")]
    [InlineData("TMDB-429")]
    [InlineData("a1")]
    public void AcceptsPortableAsciiIdentifiers(string value)
    {
        Assert.True(StableErrorCode.IsValid(value));
        Assert.Same(value, StableErrorCode.Require(value, "value"));
    }

    [Fact]
    public void AcceptsTheMaximumLength()
    {
        var value = new string('a', StableErrorCode.MaximumLength);

        Assert.Equal(value, StableErrorCode.Require(value, "value"));
    }

    [Fact]
    public void RejectsUnsafeOrUnboundedValues()
    {
        string?[] invalid =
        [
            null,
            "",
            " ",
            "bad code",
            "bad.code",
            "错误码",
            new string('a', StableErrorCode.MaximumLength + 1),
        ];

        foreach (var value in invalid)
        {
            Assert.False(StableErrorCode.IsValid(value));
            var exception = Assert.Throws<ArgumentException>(() =>
                StableErrorCode.Require(value, "code"));
            Assert.Equal("code", exception.ParamName);
        }
    }

    [Fact]
    public void CoreExceptionsEnforceTheSharedContract()
    {
        const string invalid = "not portable";

        Assert.Throws<ArgumentException>(() => new RssFeedException(invalid, "message"));
        Assert.Throws<ArgumentException>(() => new MikanBangumiSubjectException(invalid, "message"));
        Assert.Throws<ArgumentException>(() => new MikanEpisodeIdentityException(invalid, "message"));
        Assert.Throws<ArgumentException>(() => new DataManifestException(invalid, "message"));
        Assert.Throws<ArgumentException>(() => new CronExpressionException(invalid, "message"));
        Assert.Throws<ArgumentException>(() =>
            new AiMetadataMatcherException(MetadataFailureKind.Protocol, invalid));
        Assert.Throws<ArgumentException>(() =>
            new TmdbClientException(MetadataFailureKind.Protocol, invalid, false));

        Assert.True(StableErrorCode.IsValid(TorrentMagnetException.StableCode));
        Assert.True(StableErrorCode.IsValid(TorrentMetainfoException.StableCode));
    }
}
