using System.Text.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class LegacyApiSurfaceTests
{
    [Fact]
    public async Task EveryUpstreamOpenApiOperationHasAnAnimeGoNetRoute()
    {
        await using var app = await RunningApp.StartAsync();
        using var baseline = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot(),
            "docs",
            "baseline",
            "openapi-upstream.json")));
        var expected = baseline.RootElement.GetProperty("paths")
            .EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => HttpMethods.Contains(operation.Name))
                .Select(operation => $"{operation.Name.ToUpperInvariant()} {path.Name}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var endpoints = app.App.Services.GetRequiredService<EndpointDataSource>().Endpoints;
        var actual = endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
            {
                var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                    ?? [];
                var route = "/" + (endpoint.RoutePattern.RawText ?? string.Empty)
                    .TrimStart('/');
                return methods.Select(method => $"{method.ToUpperInvariant()} {route}");
            })
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(12, expected.Length);
        Assert.DoesNotContain(expected, operation => !actual.Contains(operation));
    }

    private static readonly HashSet<string> HttpMethods =
        new(["get", "post", "put", "delete", "patch"], StringComparer.OrdinalIgnoreCase);

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
}
