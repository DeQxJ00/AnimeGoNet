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

    [Fact]
    public void FindsStableSemanticsThroughWrappedErrors()
    {
        var parseFailure = new RssFeedException(
            "rss_parse_failed",
            "RSS could not be parsed.");
        var wrapped = new InvalidOperationException("wrapper", parseFailure);

        Assert.True(StableErrorCode.HasSemantic(
            wrapped,
            StableErrorSemantic.ParseFailed));
        Assert.False(StableErrorCode.HasSemantic(
            wrapped,
            StableErrorSemantic.NotFound));
        Assert.False(StableErrorCode.HasSemantic(
            wrapped,
            StableErrorSemantic.None));
        Assert.True(StableErrorCode.TryGet(wrapped, out var code, out var semantics));
        Assert.Equal("rss_parse_failed", code);
        Assert.Equal(StableErrorSemantic.ParseFailed, semantics);
    }

    [Fact]
    public void TorrentAndStructuredParserErrorsExposeParseFailedSemantic()
    {
        IStableError[] errors =
        [
            new TorrentMagnetException("invalid"),
            new TorrentMetainfoException("invalid"),
            new RssFeedException("rss_empty", "invalid"),
            new MikanBangumiSubjectException("mikan_bgmid_html_empty", "invalid"),
            new MikanEpisodeIdentityException("mikan_identity_html_empty", "invalid"),
            new DataManifestException("data_manifest_invalid", "invalid"),
            new CronExpressionException("cron_invalid", "invalid"),
        ];

        Assert.All(errors, error =>
        {
            Assert.Equal(StableErrorSemantic.ParseFailed, error.Semantics);
            Assert.True(StableErrorCode.IsValid(error.Code));
        });
    }

    [Fact]
    public void MissingStableErrorReturnsNoCodeOrSemantic()
    {
        Assert.False(StableErrorCode.TryGet(
            new InvalidOperationException("plain"),
            out var code,
            out var semantics));
        Assert.Null(code);
        Assert.Equal(StableErrorSemantic.None, semantics);
    }

    [Fact]
    public void InvalidThirdPartyStableErrorFailsClosed()
    {
        Assert.False(StableErrorCode.HasSemantic(
            new InvalidStableError(),
            StableErrorSemantic.ParseFailed));
        Assert.False(StableErrorCode.TryGet(
            new InvalidStableError(),
            out var code,
            out var semantics));
        Assert.Null(code);
        Assert.Equal(StableErrorSemantic.None, semantics);
    }

    private sealed class InvalidStableError : Exception, IStableError
    {
        public string Code => "unsafe code";

        public StableErrorSemantic Semantics => StableErrorSemantic.ParseFailed;
    }
}
