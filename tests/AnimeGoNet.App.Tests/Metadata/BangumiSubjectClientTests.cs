using System.Net;
using System.Text;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class BangumiSubjectClientTests
{
    [Fact]
    public async Task SubjectEndpointMapsUpstreamFieldsWithoutReflection()
    {
        using var handler = new RecordingHandler(_ => Json("""
            {
              "id": 371546,
              "name": "ようこそ実力至上主義の教室へ 2nd Season",
              "name_cn": "欢迎来到实力至上主义教室 第二季",
              "date": "2022-07-04",
              "eps": 13,
              "total_episodes": 13
            }
            """));
        using var client = new BangumiSubjectClient(new HttpClient(handler));

        var subject = Assert.IsType<BangumiSubject>(await client.GetSubjectAsync(371546));

        Assert.Equal(371546, subject.Id);
        Assert.Equal("欢迎来到实力至上主义教室 第二季", subject.ChineseName);
        Assert.Equal(new DateOnly(2022, 7, 4), subject.AirDate);
        Assert.Equal(13, subject.EpisodeCount);
        Assert.Equal("https://api.bgm.tv/v0/subjects/371546", handler.RequestUri?.AbsoluteUri);
        Assert.Contains("AnimeGoNet/0.1", handler.UserAgent ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotFoundReturnsNullAndMalformedDateUsesStableFailure()
    {
        using var notFound = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var first = new BangumiSubjectClient(new HttpClient(notFound));
        Assert.Null(await first.GetSubjectAsync(1));

        using var malformed = new RecordingHandler(_ => Json("""
            { "id": 1, "name": "Example", "name_cn": "", "date": "private-date", "eps": 1, "total_episodes": 1 }
            """));
        using var second = new BangumiSubjectClient(new HttpClient(malformed));
        var exception = await Assert.ThrowsAsync<BangumiClientException>(() => second.GetSubjectAsync(1));
        Assert.Equal(MetadataFailureKind.Protocol, exception.Kind);
        Assert.Equal("bangumi_date_invalid", exception.SafeCode);
        Assert.DoesNotContain("private-date", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RelatedSubjectsMapsOfficialRelationContract()
    {
        using var handler = new RecordingHandler(_ => Json("""
            [{
              "id": 253047,
              "type": 2,
              "name": "前作",
              "name_cn": "前作中文名",
              "relation": "前传"
            }]
            """));
        using var client = new BangumiSubjectClient(new HttpClient(handler));

        var relation = Assert.Single(await client.GetRelatedSubjectsAsync(371546));

        Assert.Equal(253047, relation.Id);
        Assert.Equal(2, relation.Type);
        Assert.Equal("前传", relation.Relation);
        Assert.Equal("https://api.bgm.tv/v0/subjects/371546/subjects", handler.RequestUri?.AbsoluteUri);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? UserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(response(request));
        }
    }
}
