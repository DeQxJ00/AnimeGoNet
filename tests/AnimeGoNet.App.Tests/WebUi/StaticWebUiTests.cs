using System.Net;

namespace AnimeGoNet.App.Tests.WebUi;

public sealed class StaticWebUiTests
{
    [Theory]
    [InlineData("/", "text/html", "AnimeGoNet")]
    [InlineData("/styles.css", "text/css", ".hero")]
    [InlineData("/styles.css", "text/css", ".metadata-card")]
    [InlineData("/app.js", "text/javascript", "/api/v1/downloads")]
    [InlineData("/app.js", "text/javascript", "/api/v1/metadata/tasks")]
    [InlineData("/app.js", "text/javascript", "download_preparing")]
    [InlineData("/app.js", "text/javascript", "download_skipped_duplicate")]
    [InlineData("/", "text/html", "metadata-tasks")]
    public async Task ServesStaticAssets(string path, string mediaType, string marker)
    {
        await using var app = await RunningApp.StartAsync();

        using var response = await app.Client.GetAsync(path);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(marker, content, StringComparison.Ordinal);
    }
}
