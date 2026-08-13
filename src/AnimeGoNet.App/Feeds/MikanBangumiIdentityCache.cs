using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Data.Cache;

namespace AnimeGoNet.App.Feeds;

public sealed class MikanBangumiIdentityCache
{
    public const string DatabaseName = "bolt";
    public const string BucketName = "mikan_bangumi_identity";
    private readonly SqliteJsonCacheStore _store;
    private readonly TimeSpan _cacheTtl;
    private readonly TimeProvider _timeProvider;

    public MikanBangumiIdentityCache(SqliteJsonCacheStore store)
        : this(store, new MikanClientOptions().BangumiIdentityCacheTtl, TimeProvider.System)
    {
    }

    public MikanBangumiIdentityCache(
        SqliteJsonCacheStore store,
        TimeSpan cacheTtl,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfLessThan(cacheTtl, TimeSpan.Zero);
        _store = store;
        _cacheTtl = cacheTtl;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<int?> GetAsync(
        int mikanId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(mikanId, 1);
        var key = Key(mikanId);
        try
        {
            var entry = await _store.GetJsonAsync(
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
                MikanBangumiIdentityCacheJsonContext.Default.MikanBangumiIdentityCacheValue);
            if (value is { SchemaVersion: 1, MikanId: > 0, BangumiId: > 0 }
                && value.MikanId == mikanId)
            {
                return value.BangumiId;
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
        int mikanId,
        int bangumiId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(mikanId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(bangumiId, 1);
        try
        {
            var json = JsonSerializer.Serialize(
                new MikanBangumiIdentityCacheValue(1, mikanId, bangumiId),
                MikanBangumiIdentityCacheJsonContext.Default.MikanBangumiIdentityCacheValue);
            await _store.PutJsonAsync(
                DatabaseName,
                BucketName,
                Key(mikanId),
                json,
                _cacheTtl == TimeSpan.Zero ? null : _cacheTtl,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverableCacheFailure(exception))
        {
            // Successful page discovery remains authoritative when cache persistence is unavailable.
        }
    }

    private async Task TryDeleteAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await _store.DeleteAsync(DatabaseName, BucketName, key, cancellationToken)
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

    private static string Key(int mikanId) =>
        mikanId.ToString(CultureInfo.InvariantCulture);

    private static bool IsRecoverableCacheFailure(Exception exception) =>
        exception is JsonException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException
            or Microsoft.Data.Sqlite.SqliteException;
}

internal sealed record MikanBangumiIdentityCacheValue(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("mikanid")] int MikanId,
    [property: JsonPropertyName("bgmid")] int BangumiId);

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(MikanBangumiIdentityCacheValue))]
internal sealed partial class MikanBangumiIdentityCacheJsonContext : JsonSerializerContext;
