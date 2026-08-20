using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AnimeGoNet.Data.Mikan;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class MikanTrustedOffsetApiTests
{
    [Fact]
    public async Task ConfiguredThresholdControlsEffectiveStateAndProgress()
    {
        await using var app = await RunningApp.StartAsync(configure: options => options with
        {
            Metadata = options.Metadata with
            {
                MikanTrustedOffsetRequiredEpisodes = 2,
            },
        });
        var offsets = app.App.Services.GetRequiredService<MikanTrustedOffsetStore>();
        var now = DateTimeOffset.UtcNow;
        await offsets.ObserveAsync(Observation(1), now, 2);
        await offsets.ObserveAsync(Observation(2), now.AddMinutes(1), 2);

        using var response = await app.Client.GetAsync("/api/v1/mikan/trusted-offsets");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("trusted", item.GetProperty("state").GetString());
        Assert.Equal(2, item.GetProperty("required_episode_count").GetInt32());
    }

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
    public async Task ManagesBlacklistAndPurgesMatchingAutomaticOffsets()
    {
        await using var app = await RunningApp.StartAsync();
        var offsets = app.App.Services.GetRequiredService<MikanTrustedOffsetStore>();
        await offsets.ObserveAsync(Observation(1), DateTimeOffset.UtcNow, 1);

        using var added = await app.Client.PostAsJsonAsync(
            "/api/v1/mikan/trusted-offset-blacklist",
            new { scope = "pair", mikanid = 3951, groupid = 7 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.Null(await offsets.GetTrustedAsync(3951, 7, 1));
        Assert.Empty(await offsets.ListAsync());

        using var listed = await app.Client.GetAsync("/api/v1/mikan/trusted-offset-blacklist");
        using var json = JsonDocument.Parse(await listed.Content.ReadAsStreamAsync());
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("pair", item.GetProperty("scope").GetString());
        Assert.Equal(3951, item.GetProperty("mikanid").GetInt32());
        Assert.Equal(7, item.GetProperty("groupid").GetInt32());

        using var removed = await app.Client.DeleteAsync(
            "/api/v1/mikan/trusted-offset-blacklist?scope=pair&mikanid=3951&groupid=7");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.False(await offsets.IsBlacklistedAsync(3951, 7));
    }

    [Fact]
    public async Task RejectsBlacklistScopeWithWrongKeyShape()
    {
        await using var app = await RunningApp.StartAsync();
        using var response = await app.Client.PostAsJsonAsync(
            "/api/v1/mikan/trusted-offset-blacklist",
            new { scope = "mikanid", mikanid = 3951, groupid = 7 });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "mikan_offset_blacklist_key_invalid",
            json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task StaticWebUiShowsProgressAndExplicitAutomaticOnlyCleanup()
    {
        await using var app = await RunningApp.StartAsync();
        var html = await app.Client.GetStringAsync("/");
        var script = await app.Client.GetStringAsync("/app.js");

        Assert.Contains("id=\"trusted-offsets\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-offset-required-episodes\"", html, StringComparison.Ordinal);
        Assert.Contains("来源 EP 专指从 Torrent 视频文件名解析出的 EP", html, StringComparison.Ordinal);
        Assert.Contains("默认 3", html, StringComparison.Ordinal);
        Assert.Contains("loadTrustedOffsets", script, StringComparison.Ordinal);
        Assert.Contains("id=\"trusted-offset-blacklist-form\"", html, StringComparison.Ordinal);
        Assert.Contains("loadTrustedOffsetBlacklist", script, StringComparison.Ordinal);
        Assert.Contains("/api/v1/mikan/trusted-offset-blacklist", script, StringComparison.Ordinal);
        Assert.Contains("mikan_trusted_offset_required_episodes", script, StringComparison.Ordinal);
        Assert.Contains("/api/v1/mikan/trusted-offsets", script, StringComparison.Ordinal);
        Assert.Contains("人工规则、完成记录和媒体文件不会删除", script, StringComparison.Ordinal);
    }

    private static MikanOffsetEvidenceObservation Observation(int sourceEpisode) =>
        new(3951, 7, sourceEpisode, 72517, 2, 13);
}
