using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AnimeGoNet.App.Tests.Api;

public sealed class NotificationApiTests
{
    private static readonly string[] BarkEvents = ["metadata_failed", "organization_completed"];
    private static readonly string[] MetadataFailedEvent = ["metadata_failed"];

    [Fact]
    public async Task CrudReturnsFullLocallyStoredCredentialAndOptions()
    {
        await using var app = await RunningApp.StartAsync();
        using var created = await app.Client.PostAsJsonAsync(
            "/api/v1/notifications/channels",
            new
            {
                name = "My Bark",
                provider = "bark",
                enabled = true,
                endpoint_url = "https://api.day.app",
                secret = "device-key",
                target = (string?)null,
                options = new { group = "AnimeGoNet", sound = "birdsong", level = "active" },
                events = BarkEvents,
            });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStreamAsync());
        var id = createdJson.RootElement.GetProperty("id").GetString()!;
        Assert.Equal("device-key", createdJson.RootElement.GetProperty("secret").GetString());

        using var listed = await app.Client.GetAsync("/api/v1/notifications/channels");
        using var listedJson = JsonDocument.Parse(await listed.Content.ReadAsStreamAsync());
        var item = Assert.Single(listedJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("AnimeGoNet", item.GetProperty("options").GetProperty("group").GetString());

        using var deleted = await app.Client.DeleteAsync($"/api/v1/notifications/channels/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task RejectsProviderWithoutRequiredCredential()
    {
        await using var app = await RunningApp.StartAsync();
        using var response = await app.Client.PostAsJsonAsync(
            "/api/v1/notifications/channels",
            new
            {
                name = "Broken Bark", provider = "bark", enabled = true,
                endpoint_url = "https://api.day.app", secret = (string?)null,
                target = (string?)null, options = new { },
                events = MetadataFailedEvent,
            });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("notification_channel_invalid", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task WebUiDocumentsBarkOptionsProvidersEventsAndDeliveryAudit()
    {
        await using var app = await RunningApp.StartAsync();
        var html = await app.Client.GetStringAsync("/");
        var script = await app.Client.GetStringAsync("/app.js");

        Assert.Contains(">通知</button>", html, StringComparison.Ordinal);
        Assert.Contains("Bark 详细选项", html, StringComparison.Ordinal);
        Assert.Contains("Discord Webhook", html, StringComparison.Ordinal);
        Assert.Contains("Telegram Bot", html, StringComparison.Ordinal);
        Assert.Contains("Server酱", html, StringComparison.Ordinal);
        Assert.Contains("PushPlus", html, StringComparison.Ordinal);
        Assert.Contains("id=\"notification-delivery-list\"", html, StringComparison.Ordinal);
        Assert.Contains("/api/v1/notifications/channels", script, StringComparison.Ordinal);
        Assert.Contains("loadNotificationDeliveries", script, StringComparison.Ordinal);
    }
}
