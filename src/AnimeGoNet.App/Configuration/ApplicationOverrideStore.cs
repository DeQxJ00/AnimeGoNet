using System.Text.Json;
using System.Text.Json.Serialization;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Configuration;

public sealed record ApplicationOverrideEntry(
    string TmdbBaseUrl,
    string TmdbLanguage,
    double TmdbHttpTimeoutSeconds,
    bool TmdbApiKeyOverridden,
    string? TmdbApiKey,
    bool TmdbReadAccessTokenOverridden,
    string? TmdbReadAccessToken,
    bool SeasonFailureSkip,
    bool SeasonFailureBacktrace,
    bool SeasonFailureUseTitleSeason,
    bool SeasonFailureUseFirstSeason,
    bool AiUseSeasonMatch,
    bool AiUseEpisodeMatch,
    double AiHttpTimeoutSeconds,
    bool TmdbFailureUseBangumi,
    bool MikanTrustedOffsetCacheEnabled,
    double TorrentHttpTimeoutSeconds,
    long TorrentMaxResponseBytes,
    int TorrentMaxRedirects,
    double TorrentStagingTtlSeconds,
    DateTimeOffset UpdatedAtUtc,
    bool? TmdbProxyUrlOverridden = null,
    string? TmdbProxyUrl = null,
    string? BangumiBaseUrl = null,
    bool? BangumiProxyUrlOverridden = null,
    string? BangumiProxyUrl = null,
    double? BangumiHttpTimeoutSeconds = null,
    bool? AiUseMetadataMatch = null);

public sealed record ApplicationOverrideSnapshot(
    int FormatVersion,
    long Revision,
    ApplicationOverrideEntry? Settings);

public sealed record ApplicationConfigurationRuntimeState(long AppliedRevision);

public sealed class ApplicationOverrideRevisionException : InvalidOperationException;

public sealed class ApplicationOverrideStore : IDisposable
{
    private const int CurrentFormatVersion = 1;
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ApplicationOverrideStore(string configurationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        _path = Path.Combine(configurationPath, "application.private.json");
    }

    public void Dispose() => _gate.Dispose();

    public async Task<ApplicationOverrideSnapshot> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ApplicationOverrideSnapshot> SaveAsync(
        ApplicationOverrideEntry settings,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            if (current.Revision != expectedRevision)
            {
                throw new ApplicationOverrideRevisionException();
            }

            var saved = new ApplicationOverrideSnapshot(
                CurrentFormatVersion,
                current.Revision + 1,
                settings);
            await SaveCoreAsync(saved, cancellationToken).ConfigureAwait(false);
            return saved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ApplicationOverrideSnapshot> DeleteAsync(
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            if (current.Revision != expectedRevision)
            {
                throw new ApplicationOverrideRevisionException();
            }

            if (current.Settings is null)
            {
                return current;
            }

            var saved = new ApplicationOverrideSnapshot(
                CurrentFormatVersion,
                current.Revision + 1,
                null);
            await SaveCoreAsync(saved, cancellationToken).ConfigureAwait(false);
            return saved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public static AnimeGoOptions Apply(
        AnimeGoOptions options,
        ApplicationOverrideSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(snapshot);
        var settings = snapshot.Settings;
        if (settings is null)
        {
            return options;
        }

        if (!Uri.TryCreate(settings.TmdbBaseUrl, UriKind.Absolute, out var tmdbBaseUrl))
        {
            throw new InvalidOperationException("Application private configuration has an invalid TMDB base URL.");
        }
        var tmdbProxyUrl = settings.TmdbProxyUrlOverridden == true
            ? ParseOptionalUri(settings.TmdbProxyUrl, "TMDB proxy URL")
            : options.Metadata.Tmdb.ProxyUrl;
        var bangumiBaseUrl = settings.BangumiBaseUrl is null
            ? options.Metadata.Bangumi.BaseUrl
            : ParseRequiredUri(settings.BangumiBaseUrl, "Bangumi base URL");
        var bangumiProxyUrl = settings.BangumiProxyUrlOverridden == true
            ? ParseOptionalUri(settings.BangumiProxyUrl, "Bangumi proxy URL")
            : options.Metadata.Bangumi.ProxyUrl;

        return options with
        {
            Metadata = options.Metadata with
            {
                Tmdb = options.Metadata.Tmdb with
                {
                    BaseUrl = tmdbBaseUrl,
                    ProxyUrl = tmdbProxyUrl,
                    Language = settings.TmdbLanguage,
                    HttpTimeout = TimeSpan.FromSeconds(settings.TmdbHttpTimeoutSeconds),
                    ApiKey = settings.TmdbApiKeyOverridden
                        ? settings.TmdbApiKey
                        : options.Metadata.Tmdb.ApiKey,
                    ReadAccessToken = settings.TmdbReadAccessTokenOverridden
                        ? settings.TmdbReadAccessToken
                        : options.Metadata.Tmdb.ReadAccessToken,
                },
                Bangumi = options.Metadata.Bangumi with
                {
                    BaseUrl = bangumiBaseUrl,
                    ProxyUrl = bangumiProxyUrl,
                    HttpTimeout = settings.BangumiHttpTimeoutSeconds is > 0
                        ? TimeSpan.FromSeconds(settings.BangumiHttpTimeoutSeconds.Value)
                        : options.Metadata.Bangumi.HttpTimeout,
                },
                SeasonFailure = new SeasonFailureOptions
                {
                    Skip = settings.SeasonFailureSkip,
                    Backtrace = settings.SeasonFailureBacktrace,
                    UseTitleSeason = settings.SeasonFailureUseTitleSeason,
                    UseFirstSeason = settings.SeasonFailureUseFirstSeason,
                },
                Ai = options.Metadata.Ai with
                {
                    UseMetadataMatch = settings.AiUseMetadataMatch
                        ?? (settings.AiUseSeasonMatch || settings.AiUseEpisodeMatch),
                    HttpTimeout = TimeSpan.FromSeconds(settings.AiHttpTimeoutSeconds),
                },
                TmdbFailureUseBangumi = settings.TmdbFailureUseBangumi,
                MikanTrustedOffsetCacheEnabled = settings.MikanTrustedOffsetCacheEnabled,
            },
            TorrentFetch = options.TorrentFetch with
            {
                Timeout = TimeSpan.FromSeconds(settings.TorrentHttpTimeoutSeconds),
                MaxResponseBytes = settings.TorrentMaxResponseBytes,
                MaxRedirects = settings.TorrentMaxRedirects,
                StagingTtl = TimeSpan.FromSeconds(settings.TorrentStagingTtlSeconds),
            },
        };
    }

    private static Uri ParseRequiredUri(string value, string name) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException(
                $"Application private configuration has an invalid {name}.");

    private static Uri? ParseOptionalUri(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseRequiredUri(value, name);

    private async Task<ApplicationOverrideSnapshot> LoadCoreAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new ApplicationOverrideSnapshot(CurrentFormatVersion, 0, null);
        }

        await using var stream = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var snapshot = await JsonSerializer.DeserializeAsync(
            stream,
            ApplicationOverrideJsonContext.Default.ApplicationOverrideSnapshot,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Application private configuration is empty.");
        if (snapshot.FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported application private configuration format {snapshot.FormatVersion}.");
        }

        if (snapshot.Revision < 0)
        {
            throw new InvalidOperationException("Application private configuration revision is invalid.");
        }

        return snapshot;
    }

    private async Task SaveCoreAsync(
        ApplicationOverrideSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".application.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    snapshot,
                    ApplicationOverrideJsonContext.Default.ApplicationOverrideSnapshot,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(ApplicationOverrideSnapshot))]
internal sealed partial class ApplicationOverrideJsonContext : JsonSerializerContext;
