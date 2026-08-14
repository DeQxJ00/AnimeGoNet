using System.Text;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AnimeGoNet.App.Api;

internal static class ApiOpenApi
{
    private const string ModernHeaderScheme = "AnimeGoAccessKey";
    private const string LegacyHeaderScheme = "LegacyAccessKey";
    private const string LegacyQueryScheme = "LegacyAccessKeyQuery";
    private const string WebUiModernHeaderScheme = "AnimeGoWebUiAccessKey";
    private const string WebUiHashedHeaderScheme = "WebUiAccessKey";
    private const string WebUiQueryScheme = "WebUiAccessKeyQuery";
    private static readonly string[] DocumentTags =
    [
        "compatibility",
        "ai-test",
        "config",
        "configuration-archive",
        "cache",
        "data-update",
        "delete",
        "downloaders",
        "downloads",
        "ingest",
        "legacy",
        "library",
        "logs",
        "metadata",
        "mikan",
        "plugins",
        "rss",
        "rss-rules",
        "sources",
        "status",
    ];

    public static IServiceCollection AddAnimeGoOpenApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer(static (document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "AnimeGoNet API",
                    Summary = "NativeAOT Anime download, metadata and library management API.",
                    Description =
                        "Modern /api/v1 endpoints plus the preserved AnimeGo compatibility surface.",
                    Version = "v1",
                    TermsOfService = new Uri("https://github.com/wetor/AnimeGo"),
                    License = new OpenApiLicense
                    {
                        Name = "MIT",
                        Url = new Uri("https://www.mit-license.org/"),
                    },
                };
                document.Servers = [];
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes =
                    new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal)
                    {
                        [ModernHeaderScheme] = new OpenApiSecurityScheme
                        {
                            Type = SecuritySchemeType.ApiKey,
                            Name = "X-AnimeGo-Access-Key",
                            In = ParameterLocation.Header,
                            Description = "Configured external plugin/API access key in plaintext.",
                        },
                        [LegacyHeaderScheme] = new OpenApiSecurityScheme
                        {
                            Type = SecuritySchemeType.ApiKey,
                            Name = "Access-Key",
                            In = ParameterLocation.Header,
                            Description = "Lowercase SHA-256 of the configured access key.",
                        },
                        [LegacyQueryScheme] = new OpenApiSecurityScheme
                        {
                            Type = SecuritySchemeType.ApiKey,
                            Name = "access_key",
                            In = ParameterLocation.Query,
                            Description = "Lowercase SHA-256 of the configured external plugin/API access key.",
                        },
                        [WebUiModernHeaderScheme] = new OpenApiSecurityScheme
                        {
                            Type = SecuritySchemeType.ApiKey,
                            Name = "X-AnimeGo-WebUI-Access-Key",
                            In = ParameterLocation.Header,
                            Description = "Configured WebUI access key in plaintext.",
                        },
                        [WebUiHashedHeaderScheme] = new OpenApiSecurityScheme
                        {
                            Type = SecuritySchemeType.ApiKey,
                            Name = "WebUI-Access-Key",
                            In = ParameterLocation.Header,
                            Description = "Lowercase SHA-256 of the configured WebUI access key.",
                        },
                        [WebUiQueryScheme] = new OpenApiSecurityScheme
                        {
                            Type = SecuritySchemeType.ApiKey,
                            Name = "webui_access_key",
                            In = ParameterLocation.Query,
                            Description = "Lowercase SHA-256 of the configured WebUI access key.",
                        },
                    };
                document.Tags = DocumentTags
                    .Select(static tag => new OpenApiTag { Name = tag })
                    .ToHashSet();
                return Task.CompletedTask;
            });
            options.AddOperationTransformer(static (operation, context, _) =>
            {
                operation.OperationId = CreateOperationId(
                    context.Description.HttpMethod,
                    context.Description.RelativePath);
                var document = context.Document
                    ?? throw new InvalidOperationException(
                        "OpenAPI operation context has no host document.");
                operation.Tags = new HashSet<OpenApiTagReference>
                {
                    new OpenApiTagReference(
                        TagForPath(context.Description.RelativePath),
                        document,
                        null!),
                };
                if (RequiresAccessKey(context.Description.RelativePath))
                {
                    operation.Security = IsPluginApiPath(context.Description.RelativePath)
                        ? [
                            new OpenApiSecurityRequirement(),
                            Requirement(ModernHeaderScheme, document),
                            Requirement(LegacyHeaderScheme, document),
                            Requirement(LegacyQueryScheme, document),
                        ]
                        : [
                            new OpenApiSecurityRequirement(),
                            Requirement(WebUiModernHeaderScheme, document),
                            Requirement(WebUiHashedHeaderScheme, document),
                            Requirement(WebUiQueryScheme, document),
                        ];
                }
                return Task.CompletedTask;
            });
        });
        return services;
    }

    private static OpenApiSecurityRequirement Requirement(
        string scheme,
        OpenApiDocument document) =>
        new()
        {
            [new OpenApiSecuritySchemeReference(scheme, document, null!)] = [],
        };

    private static bool RequiresAccessKey(string? relativePath) =>
        relativePath is not null
        && (relativePath.StartsWith("api/", StringComparison.Ordinal)
            || relativePath.StartsWith("websocket/", StringComparison.Ordinal));

    private static bool IsPluginApiPath(string? relativePath)
    {
        var path = relativePath?.Split('?', 2)[0];
        return path is not null
            && (path.StartsWith("api/plugin/", StringComparison.Ordinal)
                || path.Equals("api/plugin", StringComparison.Ordinal)
                || path.StartsWith("api/rss/", StringComparison.Ordinal)
                || path.Equals("api/rss", StringComparison.Ordinal)
                || path.StartsWith("api/download/manager/", StringComparison.Ordinal)
                || path.Equals("api/download/manager", StringComparison.Ordinal)
                || path.Equals("api/v1/ingest", StringComparison.Ordinal));
    }

    private static string TagForPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return "compatibility";
        }
        var path = relativePath.Split('?', 2)[0];
        if (path.StartsWith("api/v1/", StringComparison.Ordinal))
        {
            var remainder = path["api/v1/".Length..];
            var separator = remainder.IndexOf('/');
            return separator < 0 ? remainder : remainder[..separator];
        }
        if (path.StartsWith("api/", StringComparison.Ordinal))
        {
            return "legacy";
        }
        if (path.StartsWith("websocket/", StringComparison.Ordinal))
        {
            return "logs";
        }
        return "compatibility";
    }

    private static string CreateOperationId(string? method, string? relativePath)
    {
        var builder = new StringBuilder(96);
        AppendToken(builder, string.IsNullOrWhiteSpace(method) ? "unknown" : method);
        AppendToken(builder, string.IsNullOrWhiteSpace(relativePath) ? "root" : relativePath);
        return builder.ToString();
    }

    private static void AppendToken(StringBuilder builder, string value)
    {
        var pendingSeparator = builder.Length > 0;
        foreach (var character in value)
        {
            if (character is >= 'A' and <= 'Z')
            {
                if (pendingSeparator)
                {
                    builder.Append('_');
                }
                builder.Append((char)(character + ('a' - 'A')));
                pendingSeparator = false;
            }
            else if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingSeparator)
                {
                    builder.Append('_');
                }
                builder.Append(character);
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = builder.Length > 0;
            }
        }
    }
}
