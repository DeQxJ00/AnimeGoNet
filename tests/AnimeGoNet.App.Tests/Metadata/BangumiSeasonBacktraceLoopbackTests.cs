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

public sealed class BangumiSeasonBacktraceLoopbackTests
{
    [Fact]
    public async Task RetriesBothServicesWhileValidatingASecondLevelPrequel()
    {
        var relationAttempts = 0;
        var originSearchAttempts = 0;
        var requests = new ConcurrentQueue<string>();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            requests.Enqueue(context.Request.Path + context.Request.QueryString);
            await next(context);
        });
        app.MapGet("/v0/subjects/{id:int}/subjects", (int id) => id switch
        {
            10 when Interlocked.Increment(ref relationAttempts) == 1 =>
                Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
            10 => Json("""
                [{"id":20,"type":2,"name":"Middle","name_cn":"","relation":"前传"}]
                """),
            20 => Json("""
                [{"id":30,"type":2,"name":"Origin","name_cn":"","relation":"前传"}]
                """),
            _ => Json("[]"),
        });
        app.MapGet("/v0/subjects/{id:int}", (int id) => id switch
        {
            20 => Json("""
                {"id":20,"name":"Middle","name_cn":"","date":"2024-01-01","eps":12,"total_episodes":12}
                """),
            30 => Json("""
                {"id":30,"name":"Origin","name_cn":"","date":"2018-01-01","eps":12,"total_episodes":12}
                """),
            _ => Results.NotFound(),
        });
        app.MapGet("/3/discover/tv", (HttpRequest request) =>
        {
            var title = request.Query["with_text_query"].ToString();
            if (title == "Origin" && Interlocked.Increment(ref originSearchAttempts) == 1)
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            return title switch
            {
                "Middle" => Json("""
                    {"total_results":1,"results":[{"id":200,"name":"Middle","original_name":"Middle","first_air_date":"2010-01-01"}]}
                    """),
                "Origin" => Json("""
                    {"total_results":1,"results":[{"id":300,"name":"Origin","original_name":"Origin","first_air_date":"2018-01-01"}]}
                    """),
                _ => Json("{\"total_results\":0,\"results\":[]}"),
            };
        });
        app.MapGet("/3/tv/{id:int}", (int id) => id switch
        {
            200 => Json("""
                {"id":200,"name":"Middle","original_name":"Middle","first_air_date":"2010-01-01","seasons":[{"id":20001,"name":"Season 1","season_number":1,"air_date":"2010-01-01","episode_count":12}]}
                """),
            300 => Json("""
                {"id":300,"name":"Origin","original_name":"Origin","first_air_date":"2018-01-01","seasons":[{"id":30001,"name":"Season 1","season_number":1,"air_date":"2018-01-01","episode_count":12}]}
                """),
            _ => Results.NotFound(),
        });
        app.MapGet("/3/tv/{id:int}/season/{season:int}", (int id, int season) =>
            id == 300 && season == 1
                ? Json("""
                    {"id":30001,"name":"Season 1","season_number":1,"air_date":"2018-01-01","episode_count":12,"episodes":[]}
                    """)
                : Results.NotFound());

        try
        {
            await app.StartAsync();
            var address = Assert.Single(app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()!
                .Addresses);
            var origin = new Uri(address.EndsWith('/') ? address : address + "/");
            using var bangumiHttp = new HttpClient();
            using var tmdbHttp = new HttpClient();
            using var bangumi = new BangumiSubjectClient(
                bangumiHttp,
                new BangumiClientOptions
                {
                    BaseUrl = origin,
                    HttpTimeout = TimeSpan.FromSeconds(5),
                    RetryCount = 1,
                    RetryDelay = TimeSpan.Zero,
                });
            using var tmdb = new TmdbClient(
                tmdbHttp,
                new TmdbClientOptions
                {
                    BaseUrl = origin,
                    ApiKey = "fixture-key",
                    HttpTimeout = TimeSpan.FromSeconds(5),
                    RetryCount = 1,
                    RetryDelay = TimeSpan.Zero,
                });
            var resolver = new BangumiSeasonBacktraceResolver(
                bangumi,
                new TmdbSeriesSeasonResolver(new TmdbSeriesResolver(tmdb), tmdb));

            var result = await resolver.ResolveAsync(10);

            Assert.True(result.IsSuccess);
            Assert.Equal(300, result.Details!.Series.Id);
            Assert.Equal(1, result.Season!.SeasonNumber);
            Assert.Equal(3, result.VisitedSubjectCount);
            Assert.Equal(2, relationAttempts);
            Assert.Equal(2, originSearchAttempts);
            Assert.Equal(11, requests.Count);
            Assert.Equal(
                2,
                requests.Count(value => value.StartsWith(
                    "/v0/subjects/10/subjects",
                    StringComparison.Ordinal)));
            Assert.Equal(
                2,
                requests.Count(value => value.StartsWith(
                    "/3/discover/tv?",
                    StringComparison.Ordinal)
                    && value.Contains("with_text_query=Origin", StringComparison.Ordinal)));
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
