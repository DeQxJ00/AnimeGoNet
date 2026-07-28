using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class AiPublicationEvidenceResolverTests
{
    [Fact]
    public async Task ValidInternalMikanEvidenceEnablesFinalGate()
    {
        var bangumi = new FakeEpisodeClient(
        [
            new BangumiEpisode(1, 0, 6, new DateOnly(2026, 7, 15)),
            new BangumiEpisode(2, 0, 7, new DateOnly(2026, 7, 22)),
        ]);
        var resolver = new AiPublicationEvidenceResolver(
            bangumi,
            new AiMatchingOptions { UseBangumiPubDateFirst = true });

        var result = await resolver.ResolveAsync(Claim());

        Assert.True(result.UseBangumiPubDateFirst);
        Assert.Equal(7, result.BangumiEpisodeCandidate);
        Assert.Equal(PublishedAt, result.PublishedAt);
        Assert.Equal("matched", result.Result);
        Assert.Equal([547888], bangumi.SubjectIds);
    }

    [Theory]
    [InlineData("u2", 1, true, true, "ai_pubdate_source_not_mikan")]
    [InlineData("mikan", 2, true, true, "ai_pubdate_torrent_file_count_not_one")]
    [InlineData("mikan", 1, false, true, "ai_pubdate_bgmid_missing")]
    [InlineData("mikan", 1, true, false, "ai_pubdate_published_at_missing")]
    public async Task InvalidPrerequisiteDisablesGateWithoutBangumiQuery(
        string sourceAdapter,
        int torrentFileCount,
        bool hasBangumiId,
        bool hasPublication,
        string expectedCode)
    {
        var bangumi = new FakeEpisodeClient([]);
        var resolver = new AiPublicationEvidenceResolver(
            bangumi,
            new AiMatchingOptions { UseBangumiPubDateFirst = true });
        var claim = Claim() with
        {
            SourceAdapter = sourceAdapter,
            TorrentFileCount = torrentFileCount,
            BangumiSubjectId = hasBangumiId ? 547888 : null,
            SourcePublishedAtRaw = hasPublication ? "2026-07-22T12:34:56.123" : null,
            SourcePublishedAt = hasPublication ? PublishedAt : null,
        };

        var result = await resolver.ResolveAsync(claim);

        Assert.False(result.UseBangumiPubDateFirst);
        Assert.Null(result.PublishedAt);
        Assert.Null(result.BangumiEpisodeCandidate);
        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Empty(bangumi.SubjectIds);
    }

    [Fact]
    public async Task BangumiFailureDisablesOptionalGateAndIsSafelyClassified()
    {
        var resolver = new AiPublicationEvidenceResolver(
            new FakeEpisodeClient(new BangumiClientException(
                MetadataFailureKind.Network,
                "bangumi_network_error")),
            new AiMatchingOptions { UseBangumiPubDateFirst = true });

        var result = await resolver.ResolveAsync(Claim());

        Assert.False(result.UseBangumiPubDateFirst);
        Assert.Equal("error", result.Result);
        Assert.Equal("bangumi_network_error", result.ErrorCode);
        Assert.True(result.Retryable);
    }

    [Fact]
    public async Task DisabledConfigurationDoesNotQueryOrAudit()
    {
        var bangumi = new FakeEpisodeClient([]);
        var resolver = new AiPublicationEvidenceResolver(
            bangumi,
            new AiMatchingOptions { UseBangumiPubDateFirst = false });

        var result = await resolver.ResolveAsync(Claim());

        Assert.False(result.UseBangumiPubDateFirst);
        Assert.False(result.ShouldAudit);
        Assert.Empty(bangumi.SubjectIds);
    }

    private static readonly DateTimeOffset PublishedAt =
        new(2026, 7, 22, 12, 34, 56, TimeSpan.FromHours(8));

    private static MetadataTaskClaim Claim() =>
        new(
            "run",
            "task",
            "Show 07",
            3951,
            370,
            547888,
            1,
            "lease",
            Files: [new("file", "Show 07.mkv", 100, "7", "7")],
            SourceAdapter: "mikan",
            SourcePublishedAtRaw: "2026-07-22T12:34:56.123",
            SourcePublishedAt: PublishedAt,
            TorrentFileCount: 1);

    private sealed class FakeEpisodeClient : IBangumiEpisodeClient
    {
        private readonly IReadOnlyList<BangumiEpisode> _episodes = [];
        private readonly Exception? _failure;

        public FakeEpisodeClient(IReadOnlyList<BangumiEpisode> episodes) => _episodes = episodes;

        public FakeEpisodeClient(Exception failure) => _failure = failure;

        public List<int> SubjectIds { get; } = [];

        public Task<IReadOnlyList<BangumiEpisode>> GetEpisodesAsync(
            int subjectId,
            CancellationToken cancellationToken = default)
        {
            SubjectIds.Add(subjectId);
            return _failure is null
                ? Task.FromResult(_episodes)
                : Task.FromException<IReadOnlyList<BangumiEpisode>>(_failure);
        }
    }
}
