using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed partial class OpenApiDocumentTests
{
    private static readonly HashSet<string> HttpMethods =
        new(["get", "post", "put", "delete", "patch"], StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task DocumentCoversEveryHttpEndpointWithUniqueOperationsAndTags()
    {
        await using var app = await RunningApp.StartAsync();

        using var document = await GetDocumentAsync(app.Client);
        var root = document.RootElement;
        Assert.StartsWith("3.1", root.GetProperty("openapi").GetString(), StringComparison.Ordinal);
        Assert.Equal("AnimeGoNet API", root.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("v1", root.GetProperty("info").GetProperty("version").GetString());

        var documentOperations = ReadDocumentOperations(root).ToArray();
        var endpointOperations = ReadEndpointOperations(app).ToArray();
        Assert.Equal(endpointOperations, documentOperations.Select(operation => operation.Key));

        var operationIds = documentOperations
            .Select(operation => operation.Value.GetProperty("operationId").GetString())
            .ToArray();
        Assert.DoesNotContain(operationIds, string.IsNullOrWhiteSpace);
        Assert.Equal(operationIds.Length, operationIds.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(operationIds, operationId =>
            operationId!.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '_')));
        var documentTags = root.GetProperty("tags")
            .EnumerateArray()
            .Select(tag => tag.GetProperty("name").GetString())
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(documentOperations, operation =>
        {
            var tags = operation.Value.GetProperty("tags");
            Assert.Equal(JsonValueKind.Array, tags.ValueKind);
            Assert.Single(tags.EnumerateArray());
            Assert.Contains(tags[0].GetString(), documentTags);
        });
        Assert.DoesNotContain(
            root.GetProperty("paths").EnumerateObject(),
            path => path.Name.StartsWith("/openapi/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DocumentDescribesAuthenticationRequestsResponsesAndPathParameters()
    {
        await using var app = await RunningApp.StartAsync(accessKey: "synthetic-access-key");

        using var document = await GetDocumentAsync(app.Client);
        var root = document.RootElement;
        var schemes = root.GetProperty("components").GetProperty("securitySchemes");
        AssertScheme(schemes, "AnimeGoAccessKey", "X-AnimeGo-Access-Key", "header");
        AssertScheme(schemes, "LegacyAccessKey", "Access-Key", "header");
        AssertScheme(schemes, "LegacyAccessKeyQuery", "access_key", "query");
        AssertScheme(schemes, "AnimeGoWebUiAccessKey", "X-AnimeGo-WebUI-Access-Key", "header");
        AssertScheme(schemes, "WebUiAccessKey", "WebUI-Access-Key", "header");
        AssertScheme(schemes, "WebUiAccessKeyQuery", "webui_access_key", "query");

        var status = Operation(root, "/api/v1/status", "get");
        var security = status.GetProperty("security").EnumerateArray().ToArray();
        Assert.Equal(4, security.Length);
        Assert.Empty(security[0].EnumerateObject());
        Assert.Equal(
            ["AnimeGoWebUiAccessKey", "WebUiAccessKey", "WebUiAccessKeyQuery"],
            security.Skip(1).Select(item => Assert.Single(item.EnumerateObject()).Name));
        Assert.False(Operation(root, "/ping", "get").TryGetProperty("security", out _));

        var ingest = Operation(root, "/api/v1/ingest", "post");
        Assert.Equal(
            ["AnimeGoAccessKey", "LegacyAccessKey", "LegacyAccessKeyQuery"],
            ingest.GetProperty("security").EnumerateArray().Skip(1)
                .Select(item => Assert.Single(item.EnumerateObject()).Name));
        var requestBody = ingest.GetProperty("requestBody");
        Assert.True(requestBody.GetProperty("required").GetBoolean());
        Assert.Equal(
            JsonValueKind.Object,
            requestBody.GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .ValueKind);
        Assert.True(ingest.GetProperty("responses").TryGetProperty("200", out _));

        var u2Ingest = Operation(
            root,
            "/api/v1/plugins/inner_plugin_u2/ingest",
            "post");
        Assert.Equal(
            ["AnimeGoAccessKey", "LegacyAccessKey", "LegacyAccessKeyQuery"],
            u2Ingest.GetProperty("security").EnumerateArray().Skip(1)
                .Select(item => Assert.Single(item.EnumerateObject()).Name));
        Assert.True(u2Ingest.GetProperty("requestBody").GetProperty("required").GetBoolean());

        var manualIngest = Operation(root, "/api/v1/ingest/manual", "post");
        Assert.Equal(
            ["AnimeGoWebUiAccessKey", "WebUiAccessKey", "WebUiAccessKeyQuery"],
            manualIngest.GetProperty("security").EnumerateArray().Skip(1)
                .Select(item => Assert.Single(item.EnumerateObject()).Name));

        var workRule = Operation(root, "/api/v1/mikan/work-rules/{mikanId}", "get");
        var mikanId = Assert.Single(workRule.GetProperty("parameters").EnumerateArray(), parameter =>
            string.Equals(parameter.GetProperty("name").GetString(), "mikanId", StringComparison.Ordinal));
        Assert.Equal("path", mikanId.GetProperty("in").GetString());
        Assert.True(mikanId.GetProperty("required").GetBoolean());
        Assert.Equal("integer", mikanId.GetProperty("schema").GetProperty("type").GetString());

        var unauthorized = await app.Client.GetAsync("/api/v1/status");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/status");
        request.Headers.Add("X-AnimeGo-WebUI-Access-Key", "synthetic-access-key");
        using var authorized = await app.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
    }

    [Fact]
    public async Task DocumentRetainsEveryUpstreamCompatibilityOperation()
    {
        await using var app = await RunningApp.StartAsync();
        using var document = await GetDocumentAsync(app.Client);
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
        var actual = ReadDocumentOperations(document.RootElement)
            .Select(operation => operation.Key)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(12, expected.Length);
        Assert.DoesNotContain(expected, operation => !actual.Contains(operation));
    }

    [Fact]
    public async Task DocumentIsDeterministicAndDoesNotExposeRuntimeSecretsOrPaths()
    {
        const string firstAccessKey = "openapi-first-private-key";
        const string secondAccessKey = "openapi-second-private-key";
        await using var first = await RunningApp.StartAsync(accessKey: firstAccessKey);
        await using var second = await RunningApp.StartAsync(accessKey: secondAccessKey);

        var firstJson = await GetDocumentTextAsync(first.Client);
        var secondJson = await GetDocumentTextAsync(second.Client);

        Assert.Equal(firstJson, secondJson);
        Assert.DoesNotContain(firstAccessKey, firstJson, StringComparison.Ordinal);
        Assert.DoesNotContain(secondAccessKey, secondJson, StringComparison.Ordinal);
        Assert.DoesNotContain(first.RootPath, firstJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(second.RootPath, secondJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", firstJson, StringComparison.Ordinal);
    }

    private static async Task<JsonDocument> GetDocumentAsync(HttpClient client)
    {
        return JsonDocument.Parse(await GetDocumentTextAsync(client));
    }

    private static async Task<string> GetDocumentTextAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/openapi/v1.json");
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, content);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return content;
    }

    private static IEnumerable<KeyValuePair<string, JsonElement>> ReadDocumentOperations(
        JsonElement document) =>
        document.GetProperty("paths")
            .EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => HttpMethods.Contains(operation.Name))
                .Select(operation => new KeyValuePair<string, JsonElement>(
                    $"{operation.Name.ToUpperInvariant()} {path.Name}",
                    operation.Value)))
            .OrderBy(operation => operation.Key, StringComparer.Ordinal);

    private static IEnumerable<string> ReadEndpointOperations(RunningApp app) =>
        app.App.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>()
                is not { ExcludeFromDescription: true })
            .Where(endpoint => !(endpoint.RoutePattern.RawText ?? string.Empty)
                .Contains("*path", StringComparison.Ordinal))
            .SelectMany(endpoint =>
            {
                var path = "/" + ConstraintPattern().Replace(
                    (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/'),
                    "{$1}");
                var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                    ?? [];
                return methods.Select(method => $"{method.ToUpperInvariant()} {path}");
            })
            .Order(StringComparer.Ordinal);

    private static JsonElement Operation(JsonElement document, string path, string method) =>
        document.GetProperty("paths").GetProperty(path).GetProperty(method);

    private static void AssertScheme(
        JsonElement schemes,
        string id,
        string name,
        string location)
    {
        var scheme = schemes.GetProperty(id);
        Assert.Equal("apiKey", scheme.GetProperty("type").GetString());
        Assert.Equal(name, scheme.GetProperty("name").GetString());
        Assert.Equal(location, scheme.GetProperty("in").GetString());
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));

    [GeneratedRegex(@"\{([^}:]+)(?::[^}]+)?\}", RegexOptions.CultureInvariant)]
    private static partial Regex ConstraintPattern();
}
