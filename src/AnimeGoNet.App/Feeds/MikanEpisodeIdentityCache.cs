using System.Text.Json;
using System.Text.Json.Serialization;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Data.Cache;

namespace AnimeGoNet.App.Feeds;

public sealed class MikanEpisodeIdentityCache(
    SqliteJsonCacheStore store,
    TimeProvider? timeProvider = null)
{
    public const string DatabaseName = "bolt";
    public const string BucketName = "mikan_episode_identity";
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<MikanEpisodeIdentity?> GetAsync(
        Uri episodeUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(episodeUri);
        var key = TryNormalizeKey(episodeUri);
        if (key is null)
        {
            return null;
        }

        try
        {
            var entry = await store.GetJsonAsync(
                DatabaseName,
                BucketName,
                key,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            if (entry is null)
            {
                return null;
            }

            var value = JsonSerializer.Deserialize(
                entry.ValueJson,
                MikanIdentityCacheJsonContext.Default.MikanEpisodeIdentityCacheValue);
            if (value is { SchemaVersion: 1, MikanId: > 0, GroupId: > 0 })
            {
                return new MikanEpisodeIdentity(value.MikanId, value.GroupId);
            }

            await TryDeleteAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverableCacheFailure(exception))
        {
            await TryDeleteAsync(key, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    public async Task PutAsync(
        Uri episodeUri,
        MikanEpisodeIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(episodeUri);
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.MikanId <= 0 || identity.SubGroupId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(identity),
                "Mikan identity must contain a positive mikanid and groupid.");
        }

        var key = TryNormalizeKey(episodeUri);
        if (key is null)
        {
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(
                new MikanEpisodeIdentityCacheValue(
                    1,
                    identity.MikanId,
                    identity.SubGroupId),
                MikanIdentityCacheJsonContext.Default.MikanEpisodeIdentityCacheValue);
            await store.PutJsonAsync(
                DatabaseName,
                BucketName,
                key,
                json,
                ttl: null,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverableCacheFailure(exception))
        {
            // A successful authoritative page parse wins even if cache persistence is unavailable.
        }
    }

    private async Task TryDeleteAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await store.DeleteAsync(DatabaseName, BucketName, key, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverableCacheFailure(exception))
        {
        }
    }

    private static string? TryNormalizeKey(Uri episodeUri)
    {
        if (!episodeUri.IsAbsoluteUri
            || episodeUri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(episodeUri.UserInfo)
            || !string.IsNullOrEmpty(episodeUri.Query)
            || !string.IsNullOrEmpty(episodeUri.Fragment))
        {
            return null;
        }

        return episodeUri.GetComponents(
            UriComponents.SchemeAndServer | UriComponents.Path,
            UriFormat.UriEscaped);
    }

    private static bool IsRecoverableCacheFailure(Exception exception) =>
        exception is JsonException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException
            or Microsoft.Data.Sqlite.SqliteException;
}

internal sealed record MikanEpisodeIdentityCacheValue(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("mikanid")] int MikanId,
    [property: JsonPropertyName("groupid")] int GroupId);

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(MikanEpisodeIdentityCacheValue))]
internal sealed partial class MikanIdentityCacheJsonContext : JsonSerializerContext;
