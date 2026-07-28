using System.Net;
using System.Text.Json;
using AnimeGoNet.Data.Mikan;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class MikanTrustedOffsetApiTests
{
    [Fact]
    public async Task ListsLearningAndTrustedProgressThenClearsOnlyRequestedAutomaticState()
    {
        await using var app = await RunningApp.StartAsync();
        var offsets = app.App.Services.GetRequiredService<MikanTrustedOffsetStore>();
        var rules = app.App.Services.GetRequiredService<MikanWorkMetadataRuleStore>();
        var now = DateTimeOffset.UtcNow;
        await rules.SaveAsync(
            new MikanWorkMetadataRuleUpdate(3951, 547888, 72517, 2, 13),
            expectedRevision: 0,
            now);
        await offsets.ObserveAsync(Observation(1), now);
        await offsets.ObserveAsync(Observation(2), now.AddMinutes(1));

        using var learning = await app.Client.GetAsync(
            "/api/v1/mikan/trusted-offsets?mikanid=3951&groupid=7");
        using var learningJson = JsonDocument.Parse(await learning.Content.ReadAsStreamAsync());
        var item = Assert.Single(learningJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, learning.StatusCode);
        Assert.Equal("learning", item.GetProperty("state").GetString());
        Assert.Equal(2, item.GetProperty("distinct_episode_count").GetInt32());
        Assert.Equal(3, item.GetProperty("required_episode_count").GetInt32());

        await offsets.ObserveAsync(Observation(3), now.AddMinutes(2));
        using var trusted = await app.Client.GetAsync("/api/v1/mikan/trusted-offsets");
        using var trustedJson = JsonDocument.Parse(await trusted.Content.ReadAsStreamAsync());
        item = Assert.Single(trustedJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("trusted", item.GetProperty("state").GetString());
        Assert.Equal(13, item.GetProperty("episode_offset").GetInt32());

        using var deleted = await app.Client.DeleteAsync(
            "/api/v1/mikan/trusted-offsets/3951/7");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Empty(await offsets.ListAsync());
        Assert.NotNull(await rules.GetAsync(3951));

        using var missing = await app.Client.DeleteAsync(
            "/api/v1/mikan/trusted-offsets/3951/7");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task RejectsInvalidFilterKeys()
    {
        await using var app = await RunningApp.StartAsync();

        using var response = await app.Client.GetAsync(
            "/api/v1/mikan/trusted-offsets?mikanid=0");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "mikan_offset_key_invalid",
            json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task StaticWebUiShowsProgressAndExplicitAutomaticOnlyCleanup()
    {
        await using var app = await RunningApp.StartAsync();
        var html = await app.Client.GetStringAsync("/");
        var script = await app.Client.GetStringAsync("/app.js");

        Assert.Contains("id=\"trusted-offsets\"", html, StringComparison.Ordinal);
        Assert.Contains("loadTrustedOffsets", script, StringComparison.Ordinal);
        Assert.Contains("/api/v1/mikan/trusted-offsets", script, StringComparison.Ordinal);
        Assert.Contains("人工规则、完成记录和媒体文件不会删除", script, StringComparison.Ordinal);
    }

    private static MikanOffsetEvidenceObservation Observation(int sourceEpisode) =>
        new(3951, 7, sourceEpisode, 72517, 2, 13);
}
