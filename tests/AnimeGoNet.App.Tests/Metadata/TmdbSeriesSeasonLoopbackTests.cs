using System.Collections.Concurrent;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class TmdbSeriesSeasonLoopbackTests
{
    [Fact]
    public async Task ExhaustsJapaneseSearchRoundsThenEveryChineseSeriesUntilSeasonVerifies()
    {
        const string japanese = "Re:ゼロから始める異世界生活 4th season 喪失編";
        const string japaneseBase = "Re:ゼロから始める異世界生活";
        const string chinese = "Re：从零开始的异世界生活 第四季 丧失篇";
        var searchedTitles = new ConcurrentQueue<string>();
        var detailIds = new ConcurrentQueue<int>();
        var seasonRequests = new ConcurrentQueue<(int SeriesId, int SeasonNumber)>();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();

        app.MapGet("/3/discover/tv", (HttpRequest request) =>
        {
            Assert.Equal("first_air_date.desc", request.Query["sort_by"]);
            Assert.Equal("zh-CN", request.Query["language"]);
            Assert.Equal("Asia/Shanghai", request.Query["timezone"]);
            Assert.Equal("16", request.Query["with_genres"]);
            Assert.Equal("fixture-key", request.Query["api_key"]);
            var title = request.Query["with_text_query"].ToString();
            searchedTitles.Enqueue(title);
            return title switch
            {
                japanese => Json($$"""
                    {"total_results":1,"results":[{"id":10,"name":"{{japanese}}","original_name":"{{japanese}}","first_air_date":"2016-04-04"}]}
                    """),
                japaneseBase => Json("""
                    {"total_results":0,"results":[]}
                    """),
                chinese => Json($$"""
                    {"total_results":2,"results":[
                      {"id":20,"name":"{{chinese}}","original_name":"{{japaneseBase}}","first_air_date":"2016-04-04"},
                      {"id":30,"name":"{{chinese}}","original_name":"{{japaneseBase}}","first_air_date":"2026-04-01"}
                    ]}
                    """),
                _ => Json("""
                    {"total_results":0,"results":[]}
                    """),
            };
        });
        app.MapGet("/3/tv/{id:int}", (int id) =>
        {
            detailIds.Enqueue(id);
            return id switch
            {
                10 => Json($$"""
                    {"id":10,"name":"{{japanese}}","original_name":"{{japanese}}","first_air_date":"2016-04-04","seasons":[{"id":1001,"name":"Season 1","season_number":1,"air_date":"2016-04-04","episode_count":25}]}
                    """),
                20 => Json($$"""
                    {"id":20,"name":"{{chinese}}","original_name":"{{japaneseBase}}","first_air_date":"2016-04-04","seasons":[{"id":2001,"name":"Season 1","season_number":1,"air_date":"2016-04-04","episode_count":25}]}
                    """),
                30 => Json($$"""
                    {"id":30,"name":"Re：从零开始的异世界生活","original_name":"{{japaneseBase}}","first_air_date":"2016-04-04","seasons":[{"id":3004,"name":"Season 4","season_number":4,"air_date":"2026-04-01","episode_count":12}]}
                    """),
                _ => Results.NotFound(),
            };
        });
        app.MapGet("/3/tv/{id:int}/season/{season:int}", (int id, int season) =>
        {
            seasonRequests.Enqueue((id, season));
            return id == 30 && season == 4
                ? Json("""
                    {"id":3004,"name":"Season 4","season_number":4,"air_date":"2026-04-01","episode_count":12,"episodes":[{"id":300401,"name":"Episode 1","air_date":"2026-04-01","season_number":4,"episode_number":1}]}
                    """)
                : Results.NotFound();
        });

        try
        {
            await app.StartAsync();
            var address = Assert.Single(app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()!
                .Addresses);
            var origin = new Uri(address.EndsWith('/') ? address : address + "/");
            using var http = new HttpClient();
            using var tmdb = new TmdbClient(
                http,
                new TmdbClientOptions
                {
                    BaseUrl = origin,
                    ApiKey = "fixture-key",
                    HttpTimeout = TimeSpan.FromSeconds(5),
                    RetryCount = 0,
                    RetryDelay = TimeSpan.Zero,
                });
            var resolver = new TmdbSeriesSeasonResolver(
                new TmdbSeriesResolver(tmdb),
                tmdb);
            var subject = new BangumiSubject(
                547888,
                japanese,
                chinese,
                new DateOnly(2026, 4, 2),
                12);

            var result = await resolver.ResolveAsync(
                TmdbSeriesSeasonResolver.BangumiTitles(subject),
                subject.AirDate);

            Assert.True(result.IsSuccess);
            Assert.Equal(30, result.Details!.Series.Id);
            Assert.Equal("Re：从零开始的异世界生活", result.Details.Series.Name);
            Assert.Equal(4, result.Season!.SeasonNumber);
            Assert.Equal(
                [japanese, japaneseBase, chinese],
                searchedTitles.ToArray());
            Assert.Equal([10, 20, 30], detailIds.ToArray());
            Assert.Equal([(30, 4)], seasonRequests.ToArray());
            Assert.Equal(
                [japanese, japaneseBase, chinese],
                result.AttemptedTitles);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static IResult Json(string value) =>
        Results.Text(value, "application/json");
}
