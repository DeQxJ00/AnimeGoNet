using System.Reflection;
using System.Runtime.CompilerServices;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Data.Sqlite;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AnimeGoNet.App.Api;

public static class ApiEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/ping", Ping);
        app.MapGet("/sha256", Sha256);
        app.MapGet("/api/v1/status", Status);
    }

    private static Ok<LegacyApiResponse<PingData>> Ping()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        return TypedResults.Ok(new LegacyApiResponse<PingData>(
            200,
            "pong",
            new PingData(version, DateTimeOffset.UtcNow.ToUnixTimeSeconds())));
    }

    private static Ok<LegacyApiResponse<string>> Sha256(string accessKey)
    {
        var hash = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(accessKey)));
        return TypedResults.Ok(new LegacyApiResponse<string>(200, "Access-Key", hash));
    }

    private static Ok<RuntimeStatus> Status(AnimeGoOptions options)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        return TypedResults.Ok(new RuntimeStatus(
            version,
            DatabaseSchema.CurrentVersion,
            !RuntimeFeature.IsDynamicCodeSupported,
            System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
            new RuntimePaths(
                options.Paths.DataPath,
                options.Paths.DownloadPath,
                options.Paths.SavePath),
            new RuntimeCapabilities(
                Configuration: true,
                Sqlite: true,
                UnifiedIngest: false,
                RssRules: false,
                Qbittorrent: false,
                Tmdb: false,
                Organizer: false)));
    }
}
