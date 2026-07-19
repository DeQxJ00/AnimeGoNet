using System.Security.Cryptography;
using System.Text;
using AnimeGoNet.App.Api;
using AnimeGoNet.App.Serialization;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Data.Sqlite;
using Microsoft.AspNetCore.Http.Json;

namespace AnimeGoNet.App;

public static class AnimeGoApplication
{
    public static async Task<WebApplication> BuildAsync(
        string[] args,
        AnimeGoOptions? options = null,
        string? accessKey = null,
        CancellationToken cancellationToken = default)
    {
        var webRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = webRootPath,
        });

        options ??= LoadOptions(builder.Configuration);
        accessKey ??= builder.Configuration["access_key"];
        var errors = AnimeGoOptionsValidator.Validate(options);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Invalid AnimeGoNet configuration: " + string.Join("; ", errors));
        }

        var layout = DirectoryLayout.From(options.Paths);
        layout.CreateDataDirectories();
        var database = new AnimeGoSqliteDatabase(layout.DatabaseFile);
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(layout);
        builder.Services.AddSingleton(database);
        builder.Services.Configure<JsonOptions>(json =>
            json.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default));

        var app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api")
                && !string.IsNullOrWhiteSpace(accessKey)
                && !HasValidAccessKey(context.Request, accessKey))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context).ConfigureAwait(false);
        });

        ApiEndpoints.Map(app);
        app.MapFallbackToFile("index.html");
        return app;
    }

    private static AnimeGoOptions LoadOptions(ConfigurationManager configuration)
    {
        var inContainer = string.Equals(
            configuration["DOTNET_RUNNING_IN_CONTAINER"],
            "true",
            StringComparison.OrdinalIgnoreCase);
        var defaults = inContainer
            ? AnimeGoDefaults.CreateDocker()
            : AnimeGoDefaults.CreateNative(AppContext.BaseDirectory);

        var dataPath = configuration["data_path"] ?? defaults.Paths.DataPath;
        var downloadPath = configuration["download_path"] ?? defaults.Paths.DownloadPath;
        var savePath = configuration["save_path"] ?? defaults.Paths.SavePath;
        var paths = new PathOptions
        {
            DataPath = dataPath,
            DownloadPath = downloadPath,
            SavePath = savePath,
        };

        var defaultDownloader = defaults.Downloaders["bt"];
        return defaults with
        {
            Paths = paths,
            Downloaders = new Dictionary<string, QbittorrentInstanceOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["bt"] = defaultDownloader with
                {
                    DownloadPath = PathBoundary.Combine(downloadPath, "bt"),
                },
            },
        };
    }

    private static bool HasValidAccessKey(HttpRequest request, string configuredKey)
    {
        if (request.Headers.TryGetValue("X-AnimeGo-Access-Key", out var directKey)
            && FixedTimeEquals(directKey.ToString(), configuredKey))
        {
            return true;
        }

        var suppliedHash = request.Query["access_key"].ToString();
        if (string.IsNullOrWhiteSpace(suppliedHash))
        {
            suppliedHash = request.Headers["Access-Key"].ToString();
        }

        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey)));
        return FixedTimeEquals(suppliedHash.ToLowerInvariant(), expectedHash);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
