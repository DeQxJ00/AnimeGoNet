using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AnimeGoNet.Core.DataUpdate;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.DataUpdate;

public sealed class DataPackageStore(AnimeGoSqliteDatabase database) : IDisposable
{
    internal const int MaximumJsonLineBytes = 1024 * 1024;
    private const int ReadBufferBytes = 64 * 1024;
    private const int InsertBatchSize = 1000;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public async Task<DataPackageImportResult> ImportAsync(
        DataPackageImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await FindVersionAsync(request.Manifest.DataVersion, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                if (!string.Equals(
                        existing.ManifestSha256,
                        request.ManifestSha256,
                        StringComparison.Ordinal))
                {
                    throw Error(
                        "data_version_immutable_conflict",
                        "The data version is already installed with a different manifest.");
                }

                if (existing.IsActive)
                {
                    return new DataPackageImportResult(
                        string.Empty,
                        request.Manifest.DataVersion,
                        true,
                        existing.SubjectCount,
                        existing.EpisodeCount,
                        await ReadPreviousVersionAsync(cancellationToken).ConfigureAwait(false),
                        []);
                }

                return await ActivateExistingAsync(request, cancellationToken).ConfigureAwait(false);
            }

            var runId = Guid.NewGuid().ToString("N");
            long subjectCount = 0;
            long episodeCount = 0;
            await StartRunAsync(
                runId,
                "import",
                request.Manifest.DataVersion,
                request.UtcNow,
                cancellationToken).ConfigureAwait(false);
            try
            {
                await using var connection = await database.OpenConnectionAsync(cancellationToken)
                    .ConfigureAwait(false);
                foreach (var asset in request.Manifest.Assets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var assetPath = ResolveAssetPath(request.AssetDirectory, asset.FileName);
                    await ValidateCompressedAssetAsync(assetPath, asset, cancellationToken)
                        .ConfigureAwait(false);

                    var imported = await ImportAssetAsync(
                        connection,
                        runId,
                        assetPath,
                        asset,
                        cancellationToken).ConfigureAwait(false);
                    if (imported != asset.RecordCount)
                    {
                        throw Error(
                            "data_asset_record_count_mismatch",
                            "The decompressed asset record count does not match the manifest.");
                    }

                    if (asset.Kind == DataAssetKind.Subjects)
                    {
                        subjectCount = checked(subjectCount + imported);
                    }
                    else
                    {
                        episodeCount = checked(episodeCount + imported);
                    }
                }

                if (subjectCount != request.Manifest.SubjectCount
                    || episodeCount != request.Manifest.EpisodeCount)
                {
                    throw Error(
                        "data_package_total_count_mismatch",
                        "Imported totals do not match the manifest.");
                }

                await ValidateStagingAsync(connection, runId, cancellationToken).ConfigureAwait(false);
                return await ActivateStagingAsync(
                    connection,
                    runId,
                    request,
                    subjectCount,
                    episodeCount,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await BestEffortFailAsync(
                    runId,
                    "data_import_cancelled",
                    subjectCount,
                    episodeCount,
                    request.UtcNow).ConfigureAwait(false);
                throw;
            }
            catch (DataPackageException exception)
            {
                await BestEffortFailAsync(
                    runId,
                    exception.Code,
                    subjectCount,
                    episodeCount,
                    request.UtcNow).ConfigureAwait(false);
                throw;
            }
            catch (Exception exception)
            {
                await BestEffortFailAsync(
                    runId,
                    "data_import_failed",
                    subjectCount,
                    episodeCount,
                    request.UtcNow).ConfigureAwait(false);
                throw Error("data_import_failed", "The data package import failed.", exception);
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<DataPackageRollbackResult> RollbackAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var runId = Guid.NewGuid().ToString("N");
            await StartRunAsync(runId, "rollback", null, utcNow, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                await using var connection = await database.OpenConnectionAsync(cancellationToken)
                    .ConfigureAwait(false);
                await using var transaction = (SqliteTransaction)await connection
                    .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                var (active, previous) = await ReadStateAsync(
                    connection,
                    transaction,
                    cancellationToken).ConfigureAwait(false);
                if (active is null || previous is null)
                {
                    throw Error(
                        "data_rollback_version_unavailable",
                        "No previous data version is available for rollback.");
                }

                await SetVersionStateAsync(
                    connection,
                    transaction,
                    active,
                    "inactive",
                    null,
                    cancellationToken).ConfigureAwait(false);
                await SetVersionStateAsync(
                    connection,
                    transaction,
                    previous,
                    "active",
                    utcNow,
                    cancellationToken).ConfigureAwait(false);
                await UpdateStateAsync(
                    connection,
                    transaction,
                    previous,
                    active,
                    utcNow,
                    cancellationToken).ConfigureAwait(false);
                await CompleteRunAsync(
                    connection,
                    transaction,
                    runId,
                    previous,
                    await CountAsync(
                        connection,
                        transaction,
                        "bangumi_archive_subjects",
                        previous,
                        cancellationToken).ConfigureAwait(false),
                    await CountAsync(
                        connection,
                        transaction,
                        "bangumi_archive_episodes",
                        previous,
                        cancellationToken).ConfigureAwait(false),
                    utcNow,
                    cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new DataPackageRollbackResult(runId, previous, active);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await BestEffortFailAsync(
                    runId,
                    "data_rollback_cancelled",
                    0,
                    0,
                    utcNow).ConfigureAwait(false);
                throw;
            }
            catch (DataPackageException exception)
            {
                await BestEffortFailAsync(runId, exception.Code, 0, 0, utcNow)
                    .ConfigureAwait(false);
                throw;
            }
            catch (Exception exception)
            {
                await BestEffortFailAsync(runId, "data_rollback_failed", 0, 0, utcNow)
                    .ConfigureAwait(false);
                throw Error("data_rollback_failed", "The data package rollback failed.", exception);
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<DataPackageStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        string? active;
        string? previous;
        DateTimeOffset updated;
        await using (var state = connection.CreateCommand())
        {
            state.CommandText = """
                SELECT active_version, previous_version, updated_at_utc
                FROM data_update_state
                WHERE singleton = 1;
                """;
            await using var reader = await state.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Data update state is missing.");
            }
            active = reader.IsDBNull(0) ? null : reader.GetString(0);
            previous = reader.IsDBNull(1) ? null : reader.GetString(1);
            updated = ParseTimestamp(reader.GetString(2));
        }

        var versions = new List<DataPackageVersionInfo>();
        await using (var query = connection.CreateCommand())
        {
            query.CommandText = """
                SELECT data_version, state, subject_count, episode_count,
                       installed_at_utc, activated_at_utc
                FROM data_update_versions
                ORDER BY installed_at_utc DESC, data_version DESC;
                """;
            await using var reader = await query.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                versions.Add(new DataPackageVersionInfo(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    reader.GetInt64(3),
                    ParseTimestamp(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : ParseTimestamp(reader.GetString(5))));
            }
        }

        DataPackageRunInfo? lastRun = null;
        await using (var query = connection.CreateCommand())
        {
            query.CommandText = """
                SELECT id, operation, data_version, status, failure_code,
                       subject_count, episode_count, started_at_utc, completed_at_utc
                FROM data_update_runs
                ORDER BY started_at_utc DESC, id DESC
                LIMIT 1;
                """;
            await using var reader = await query.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                lastRun = new DataPackageRunInfo(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetInt64(5),
                    reader.GetInt64(6),
                    ParseTimestamp(reader.GetString(7)),
                    reader.IsDBNull(8) ? null : ParseTimestamp(reader.GetString(8)));
            }
        }

        return new DataPackageStatus(active, previous, updated, versions, lastRun);
    }

    private async Task<DataPackageImportResult> ActivateExistingAsync(
        DataPackageImportRequest request,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N");
        await StartRunAsync(
            runId,
            "import",
            request.Manifest.DataVersion,
            request.UtcNow,
            cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await database.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var (active, _) = await ReadStateAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
            if (active is not null)
            {
                await SetVersionStateAsync(
                    connection,
                    transaction,
                    active,
                    "inactive",
                    null,
                    cancellationToken).ConfigureAwait(false);
            }
            await SetVersionStateAsync(
                connection,
                transaction,
                request.Manifest.DataVersion,
                "active",
                request.UtcNow,
                cancellationToken).ConfigureAwait(false);
            await UpdateStateAsync(
                connection,
                transaction,
                request.Manifest.DataVersion,
                active,
                request.UtcNow,
                cancellationToken).ConfigureAwait(false);
            var subjectCount = await CountAsync(
                connection,
                transaction,
                "bangumi_archive_subjects",
                request.Manifest.DataVersion,
                cancellationToken).ConfigureAwait(false);
            var episodeCount = await CountAsync(
                connection,
                transaction,
                "bangumi_archive_episodes",
                request.Manifest.DataVersion,
                cancellationToken).ConfigureAwait(false);
            await CompleteRunAsync(
                connection,
                transaction,
                runId,
                request.Manifest.DataVersion,
                subjectCount,
                episodeCount,
                request.UtcNow,
                cancellationToken).ConfigureAwait(false);
            var pruned = await PruneVersionsAsync(
                connection,
                transaction,
                request.KeepVersions,
                request.Manifest.DataVersion,
                active,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new DataPackageImportResult(
                runId,
                request.Manifest.DataVersion,
                false,
                subjectCount,
                episodeCount,
                active,
                pruned);
        }
        catch (DataPackageException exception)
        {
            await BestEffortFailAsync(runId, exception.Code, 0, 0, request.UtcNow)
                .ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await BestEffortFailAsync(runId, "data_import_failed", 0, 0, request.UtcNow)
                .ConfigureAwait(false);
            throw Error("data_import_failed", "The stored data version could not be activated.", exception);
        }
    }

    private static void ValidateRequest(DataPackageImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AssetDirectory);
        ArgumentNullException.ThrowIfNull(request.ClientVersion);
        if (request.KeepVersions is < 2 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Data update retention must be between 2 and 10 versions.");
        }
        if (!IsLowerSha256(request.ManifestSha256))
        {
            throw Error("data_manifest_sha256_invalid", "The manifest SHA-256 is invalid.");
        }
        if (Version.Parse(request.Manifest.MinimumClientVersion) > request.ClientVersion)
        {
            throw Error(
                "data_client_version_too_old",
                "This data package requires a newer AnimeGoNet client.");
        }
    }

    private static string ResolveAssetPath(string assetDirectory, string fileName)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(assetDirectory));
        var candidate = Path.GetFullPath(Path.Combine(root, fileName));
        if (!string.Equals(Path.GetDirectoryName(candidate), root, PathComparison))
        {
            throw Error("data_asset_path_invalid", "The data asset path escapes its package directory.");
        }
        return candidate;
    }

    private static async Task ValidateCompressedAssetAsync(
        string assetPath,
        DataManifestAsset asset,
        CancellationToken cancellationToken)
    {
        FileInfo file;
        try
        {
            file = new FileInfo(assetPath);
            if (!file.Exists
                || (file.Attributes & FileAttributes.ReparsePoint) != 0
                || file.Length != asset.SizeBytes)
            {
                throw Error(
                    "data_asset_size_mismatch",
                    "The compressed data asset size does not match the manifest.");
            }
        }
        catch (DataPackageException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Error("data_asset_unreadable", "The compressed data asset cannot be read.", exception);
        }

        try
        {
            await using var stream = new FileStream(
                assetPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                ReadBufferBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(
                    Convert.ToHexStringLower(digest),
                    asset.Sha256,
                    StringComparison.Ordinal))
            {
                throw Error(
                    "data_asset_sha256_mismatch",
                    "The compressed data asset SHA-256 does not match the manifest.");
            }
        }
        catch (DataPackageException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Error("data_asset_unreadable", "The compressed data asset cannot be read.", exception);
        }
    }

    private static async Task<long> ImportAssetAsync(
        SqliteConnection connection,
        string runId,
        string assetPath,
        DataManifestAsset asset,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var file = new FileStream(
                assetPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                ReadBufferBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var gzip = new GZipStream(file, CompressionMode.Decompress, leaveOpen: false);
            await using var writer = new StagingWriter(connection, runId, asset.Kind);
            long count = 0;
            var previousId = 0;
            await ReadJsonLinesAsync(
                gzip,
                async line =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    count = checked(count + 1);
                    if (count > asset.RecordCount)
                    {
                        throw Error(
                            "data_asset_record_count_mismatch",
                            "The data asset contains more records than declared.");
                    }

                    if (asset.Kind == DataAssetKind.Subjects)
                    {
                        var subject = ParseSubject(line);
                        if (subject.Id < asset.SubjectIdMin || subject.Id > asset.SubjectIdMax)
                        {
                            throw Error(
                                "data_asset_subject_range_invalid",
                                "A subject is outside the asset Subject ID range.");
                        }
                        if (subject.Id <= previousId)
                        {
                            throw Error(
                                "data_asset_order_invalid",
                                "Subject records are not strictly ordered by ID.");
                        }
                        previousId = subject.Id;
                        await writer.InsertSubjectAsync(subject, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        var episode = ParseEpisode(line);
                        if (episode.SubjectId < asset.SubjectIdMin
                            || episode.SubjectId > asset.SubjectIdMax)
                        {
                            throw Error(
                                "data_asset_subject_range_invalid",
                                "An episode references a Subject ID outside the asset range.");
                        }
                        if (episode.Id <= previousId)
                        {
                            throw Error(
                                "data_asset_order_invalid",
                                "Episode records are not strictly ordered by ID.");
                        }
                        previousId = episode.Id;
                        await writer.InsertEpisodeAsync(episode, cancellationToken).ConfigureAwait(false);
                    }
                },
                cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            return count;
        }
        catch (DataPackageException)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            throw Error("data_asset_gzip_invalid", "The compressed data asset is invalid.", exception);
        }
        catch (JsonException exception)
        {
            throw Error("data_asset_json_invalid", "A JSONL record is invalid.", exception);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw Error("data_asset_duplicate_id", "The data package contains a duplicate ID.", exception);
        }
        catch (OverflowException exception)
        {
            throw Error("data_asset_record_count_overflow", "The data asset contains too many records.", exception);
        }
    }

    private static async Task ReadJsonLinesAsync(
        Stream stream,
        Func<ReadOnlyMemory<byte>, ValueTask> consume,
        CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(ReadBufferBytes);
        var line = new ArrayBufferWriter<byte>(4096);
        var sawAny = false;
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(rented, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                sawAny = true;
                var offset = 0;
                while (offset < read)
                {
                    var newline = Array.IndexOf(rented, (byte)'\n', offset, read - offset);
                    var segmentLength = newline < 0 ? read - offset : newline - offset;
                    if (line.WrittenCount + segmentLength > MaximumJsonLineBytes)
                    {
                        throw Error(
                            "data_asset_line_too_long",
                            "A decompressed JSONL record exceeds the supported size.");
                    }
                    line.Write(rented.AsSpan(offset, segmentLength));
                    if (newline < 0)
                    {
                        break;
                    }

                    await ConsumeLineAsync(line, consume).ConfigureAwait(false);
                    line.Clear();
                    offset = newline + 1;
                }
            }

            if (!sawAny)
            {
                throw Error("data_asset_empty", "The decompressed data asset is empty.");
            }
            if (line.WrittenCount != 0)
            {
                throw Error(
                    "data_asset_line_ending_invalid",
                    "The decompressed JSONL asset must end each record with LF.");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static async ValueTask ConsumeLineAsync(
        ArrayBufferWriter<byte> line,
        Func<ReadOnlyMemory<byte>, ValueTask> consume)
    {
        var length = line.WrittenCount;
        if (length > 0 && line.WrittenSpan[length - 1] == (byte)'\r')
        {
            throw Error(
                "data_asset_line_ending_invalid",
                "The decompressed JSONL asset must use LF line endings.");
        }
        if (length == 0)
        {
            throw Error("data_asset_empty_line", "The JSONL asset contains an empty line.");
        }
        var bytes = line.WrittenMemory[..length];
        if (bytes.Span.StartsWith("\uFEFF"u8))
        {
            throw Error("data_asset_bom_invalid", "The JSONL asset must not contain a UTF-8 BOM.");
        }
        await consume(bytes).ConfigureAwait(false);
    }

    private static SubjectRow ParseSubject(ReadOnlyMemory<byte> line)
    {
        using var document = ParseRecord(line);
        var root = document.RootElement;
        return new SubjectRow(
            RequiredPositiveInt(root, "id"),
            RequiredText(root, "name", 1024),
            RequiredNullableText(root, "name_cn", 1024),
            RequiredNullableDate(root, "air_date"),
            RequiredNonNegativeInt(root, "episode_count"));
    }

    private static EpisodeRow ParseEpisode(ReadOnlyMemory<byte> line)
    {
        using var document = ParseRecord(line);
        var root = document.RootElement;
        var episode = RequiredText(root, "episode", 32);
        if (!IsPositiveDecimal(episode))
        {
            throw Error(
                "data_asset_episode_number_invalid",
                "An episode number is not a positive invariant decimal string.");
        }
        return new EpisodeRow(
            RequiredPositiveInt(root, "id"),
            RequiredPositiveInt(root, "subject_id"),
            RequiredPositiveInt(root, "sort"),
            episode,
            RequiredNullableDate(root, "air_date"));
    }

    private static JsonDocument ParseRecord(ReadOnlyMemory<byte> line)
    {
        var document = JsonDocument.Parse(
            line,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 4,
            });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw Error("data_asset_record_shape_invalid", "A JSONL record is not an object.");
        }
        return document;
    }

    private static int RequiredPositiveInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var result)
            || result <= 0)
        {
            throw Error("data_asset_record_value_invalid", "A required positive integer is invalid.");
        }
        return result;
    }

    private static int RequiredNonNegativeInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var result)
            || result < 0)
        {
            throw Error("data_asset_record_value_invalid", "A required non-negative integer is invalid.");
        }
        return result;
    }

    private static string RequiredText(JsonElement root, string name, int maximumLength)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw Error("data_asset_record_value_invalid", "A required text value is invalid.");
        }
        var result = value.GetString()!;
        if (!IsValidText(result, maximumLength))
        {
            throw Error("data_asset_record_value_invalid", "A required text value is invalid.");
        }
        return result;
    }

    private static string? RequiredNullableText(JsonElement root, string name, int maximumLength)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            throw Error("data_asset_record_value_invalid", "A required nullable text value is missing.");
        }
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Error("data_asset_record_value_invalid", "An optional text value is invalid.");
        }
        var result = value.GetString()!;
        if (!IsValidText(result, maximumLength))
        {
            throw Error("data_asset_record_value_invalid", "An optional text value is invalid.");
        }
        return result;
    }

    private static string? RequiredNullableDate(JsonElement root, string name)
    {
        var value = RequiredNullableText(root, name, 10);
        if (value is null)
        {
            return null;
        }
        if (!DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            throw Error("data_asset_record_date_invalid", "A record date is invalid.");
        }
        return value;
    }

    private static bool IsValidText(string value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && !value.Any(char.IsControl);

    private static bool IsPositiveDecimal(string value)
    {
        var separator = false;
        var digitsBefore = 0;
        var digitsAfter = 0;
        foreach (var character in value)
        {
            if (character == '.')
            {
                if (separator || digitsBefore == 0)
                {
                    return false;
                }
                separator = true;
                continue;
            }
            if (character is < '0' or > '9')
            {
                return false;
            }
            if (separator)
            {
                digitsAfter++;
            }
            else
            {
                digitsBefore++;
            }
        }
        if (digitsBefore == 0 || (separator && digitsAfter == 0))
        {
            return false;
        }
        return decimal.TryParse(
            value,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var number) && number > 0;
    }

    private static async Task ValidateStagingAsync(
        SqliteConnection connection,
        string runId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM data_update_staging_episodes AS episode
                LEFT JOIN data_update_staging_subjects AS subject
                  ON subject.run_id = episode.run_id
                 AND subject.subject_id = episode.subject_id
                WHERE episode.run_id = $run_id
                  AND subject.subject_id IS NULL
            );
            """;
        command.Parameters.AddWithValue("$run_id", runId);
        if (Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture) != 0)
        {
            throw Error(
                "data_package_subject_reference_missing",
                "An episode references a Subject that is not present in the package.");
        }
    }

    private static async Task<DataPackageImportResult> ActivateStagingAsync(
        SqliteConnection connection,
        string runId,
        DataPackageImportRequest request,
        long subjectCount,
        long episodeCount,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var (active, _) = await ReadStateAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);
        await using (var insertVersion = connection.CreateCommand())
        {
            insertVersion.Transaction = transaction;
            insertVersion.CommandText = """
                INSERT INTO data_update_versions (
                    data_version, schema_version, generated_at_utc,
                    minimum_client_version, manifest_sha256,
                    upstream_repository, upstream_release, upstream_asset, upstream_sha256,
                    subject_count, episode_count, state,
                    installed_at_utc, activated_at_utc)
                VALUES (
                    $version, $schema, $generated,
                    $minimum_client, $manifest_sha,
                    $repository, $release, $asset, $upstream_sha,
                    $subjects, $episodes, 'inactive',
                    $now, NULL);
                """;
            insertVersion.Parameters.AddWithValue("$version", request.Manifest.DataVersion);
            insertVersion.Parameters.AddWithValue("$schema", request.Manifest.SchemaVersion);
            insertVersion.Parameters.AddWithValue("$generated", Format(request.Manifest.GeneratedAtUtc));
            insertVersion.Parameters.AddWithValue(
                "$minimum_client",
                request.Manifest.MinimumClientVersion);
            insertVersion.Parameters.AddWithValue("$manifest_sha", request.ManifestSha256);
            insertVersion.Parameters.AddWithValue(
                "$repository",
                request.Manifest.Upstream.Repository);
            insertVersion.Parameters.AddWithValue("$release", request.Manifest.Upstream.Release);
            insertVersion.Parameters.AddWithValue("$asset", request.Manifest.Upstream.Asset);
            insertVersion.Parameters.AddWithValue("$upstream_sha", request.Manifest.Upstream.Sha256);
            insertVersion.Parameters.AddWithValue("$subjects", subjectCount);
            insertVersion.Parameters.AddWithValue("$episodes", episodeCount);
            insertVersion.Parameters.AddWithValue("$now", Format(request.UtcNow));
            try
            {
                await insertVersion.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
            {
                throw Error(
                    "data_version_immutable_conflict",
                    "The data version was installed concurrently.",
                    exception);
            }
        }

        await CopyStagingAsync(
            connection,
            transaction,
            runId,
            request.Manifest.DataVersion,
            cancellationToken).ConfigureAwait(false);
        if (active is not null)
        {
            await SetVersionStateAsync(
                connection,
                transaction,
                active,
                "inactive",
                null,
                cancellationToken).ConfigureAwait(false);
        }
        await SetVersionStateAsync(
            connection,
            transaction,
            request.Manifest.DataVersion,
            "active",
            request.UtcNow,
            cancellationToken).ConfigureAwait(false);
        await UpdateStateAsync(
            connection,
            transaction,
            request.Manifest.DataVersion,
            active,
            request.UtcNow,
            cancellationToken).ConfigureAwait(false);
        await CompleteRunAsync(
            connection,
            transaction,
            runId,
            request.Manifest.DataVersion,
            subjectCount,
            episodeCount,
            request.UtcNow,
            cancellationToken).ConfigureAwait(false);
        var pruned = await PruneVersionsAsync(
            connection,
            transaction,
            request.KeepVersions,
            request.Manifest.DataVersion,
            active,
            cancellationToken).ConfigureAwait(false);
        await DeleteStagingAsync(connection, transaction, runId, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new DataPackageImportResult(
            runId,
            request.Manifest.DataVersion,
            false,
            subjectCount,
            episodeCount,
            active,
            pruned);
    }

    private static async Task CopyStagingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string runId,
        string version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO bangumi_archive_subjects (
                data_version, subject_id, name, name_cn, air_date, episode_count)
            SELECT $version, subject_id, name, name_cn, air_date, episode_count
            FROM data_update_staging_subjects
            WHERE run_id = $run_id;

            INSERT INTO bangumi_archive_episodes (
                data_version, episode_id, subject_id, sort_number, episode_number, air_date)
            SELECT $version, episode_id, subject_id, sort_number, episode_number, air_date
            FROM data_update_staging_episodes
            WHERE run_id = $run_id;
            """;
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$run_id", runId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task DeleteStagingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string runId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM data_update_staging_episodes WHERE run_id = $run_id;
            DELETE FROM data_update_staging_subjects WHERE run_id = $run_id;
            """;
        command.Parameters.AddWithValue("$run_id", runId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(string? Active, string? Previous)> ReadStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT active_version, previous_version
            FROM data_update_state
            WHERE singleton = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Data update state is missing.");
        }
        return (
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private static async Task SetVersionStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string version,
        string state,
        DateTimeOffset? activatedAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE data_update_versions
            SET state = $state,
                activated_at_utc = COALESCE($activated, activated_at_utc)
            WHERE data_version = $version;
            """;
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$activated", (object?)activatedAt is null
            ? DBNull.Value
            : Format(activatedAt.Value));
        command.Parameters.AddWithValue("$version", version);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw Error("data_version_missing", "The requested data version is not installed.");
        }
    }

    private static async Task UpdateStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string active,
        string? previous,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE data_update_state
            SET active_version = $active,
                previous_version = $previous,
                updated_at_utc = $now
            WHERE singleton = 1;
            """;
        command.Parameters.AddWithValue("$active", active);
        command.Parameters.AddWithValue("$previous", (object?)previous ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", Format(utcNow));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("Data update state changed concurrently.");
        }
    }

    private static async Task CompleteRunAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string runId,
        string dataVersion,
        long subjectCount,
        long episodeCount,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE data_update_runs
            SET data_version = $version,
                status = 'completed',
                subject_count = $subjects,
                episode_count = $episodes,
                completed_at_utc = $now
            WHERE id = $id AND status = 'running';
            """;
        command.Parameters.AddWithValue("$version", dataVersion);
        command.Parameters.AddWithValue("$subjects", subjectCount);
        command.Parameters.AddWithValue("$episodes", episodeCount);
        command.Parameters.AddWithValue("$now", Format(utcNow));
        command.Parameters.AddWithValue("$id", runId);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("Data update run changed concurrently.");
        }
    }

    private static async Task<IReadOnlyList<string>> PruneVersionsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int keepVersions,
        string active,
        string? previous,
        CancellationToken cancellationToken)
    {
        var retained = new HashSet<string>(StringComparer.Ordinal)
        {
            active,
        };
        if (previous is not null)
        {
            retained.Add(previous);
        }

        var ordered = new List<string>();
        await using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = """
                SELECT data_version
                FROM data_update_versions
                ORDER BY COALESCE(activated_at_utc, installed_at_utc) DESC,
                         installed_at_utc DESC,
                         data_version DESC;
                """;
            await using var reader = await query.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                ordered.Add(reader.GetString(0));
            }
        }

        foreach (var version in ordered)
        {
            if (retained.Count >= keepVersions)
            {
                break;
            }
            retained.Add(version);
        }

        var pruned = ordered.Where(version => !retained.Contains(version)).ToArray();
        foreach (var version in pruned)
        {
            await using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = """
                DELETE FROM data_update_versions
                WHERE data_version = $version AND state = 'inactive';
                """;
            delete.Parameters.AddWithValue("$version", version);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        return pruned;
    }

    private async Task StartRunAsync(
        string runId,
        string operation,
        string? dataVersion,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO data_update_runs (
                id, operation, data_version, status, failure_code,
                subject_count, episode_count, started_at_utc, completed_at_utc)
            VALUES (
                $id, $operation, $version, 'running', NULL,
                0, 0, $now, NULL);
            """;
        command.Parameters.AddWithValue("$id", runId);
        command.Parameters.AddWithValue("$operation", operation);
        command.Parameters.AddWithValue("$version", (object?)dataVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", Format(utcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task BestEffortFailAsync(
        string runId,
        string failureCode,
        long subjectCount,
        long episodeCount,
        DateTimeOffset utcNow)
    {
        try
        {
            await using var connection = await database.OpenConnectionAsync().ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync()
                .ConfigureAwait(false);
            await DeleteStagingAsync(connection, transaction, runId, CancellationToken.None)
                .ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE data_update_runs
                SET status = 'failed',
                    failure_code = $failure,
                    subject_count = $subjects,
                    episode_count = $episodes,
                    completed_at_utc = $now
                WHERE id = $id AND status = 'running';
                """;
            command.Parameters.AddWithValue("$failure", failureCode);
            command.Parameters.AddWithValue("$subjects", subjectCount);
            command.Parameters.AddWithValue("$episodes", episodeCount);
            command.Parameters.AddWithValue("$now", Format(utcNow));
            command.Parameters.AddWithValue("$id", runId);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            await transaction.CommitAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The original failure is authoritative; audit cleanup is best effort.
        }
    }

    private async Task<StoredVersion?> FindVersionAsync(
        string dataVersion,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT manifest_sha256, subject_count, episode_count, state
            FROM data_update_versions
            WHERE data_version = $version;
            """;
        command.Parameters.AddWithValue("$version", dataVersion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new StoredVersion(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                string.Equals(reader.GetString(3), "active", StringComparison.Ordinal))
            : null;
    }

    private async Task<string?> ReadPreviousVersionAsync(CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT previous_version FROM data_update_state WHERE singleton = 1;
            """;
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }

    private static async Task<long> CountAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string dataVersion,
        CancellationToken cancellationToken)
    {
        var sql = table switch
        {
            "bangumi_archive_subjects" => """
                SELECT COUNT(*) FROM bangumi_archive_subjects WHERE data_version = $version;
                """,
            "bangumi_archive_episodes" => """
                SELECT COUNT(*) FROM bangumi_archive_episodes WHERE data_version = $version;
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$version", dataVersion);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static bool IsLowerSha256(string value) =>
        value is not null
        && value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static DataPackageException Error(
        string code,
        string message,
        Exception? innerException = null) =>
        new(code, message, innerException);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public void Dispose() => _writeGate.Dispose();

    private sealed record StoredVersion(
        string ManifestSha256,
        long SubjectCount,
        long EpisodeCount,
        bool IsActive);

    private sealed record SubjectRow(
        int Id,
        string Name,
        string? NameCn,
        string? AirDate,
        int EpisodeCount);

    private sealed record EpisodeRow(
        int Id,
        int SubjectId,
        int Sort,
        string Episode,
        string? AirDate);

    private sealed class StagingWriter : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly string _runId;
        private readonly DataAssetKind _kind;
        private SqliteTransaction? _transaction;
        private int _pending;

        public StagingWriter(SqliteConnection connection, string runId, DataAssetKind kind)
        {
            _connection = connection;
            _runId = runId;
            _kind = kind;
        }

        public async ValueTask InsertSubjectAsync(
            SubjectRow row,
            CancellationToken cancellationToken)
        {
            if (_kind != DataAssetKind.Subjects)
            {
                throw new InvalidOperationException("Subject inserted through an episode writer.");
            }
            await EnsureTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = """
                INSERT INTO data_update_staging_subjects (
                    run_id, subject_id, name, name_cn, air_date, episode_count)
                VALUES ($run_id, $id, $name, $name_cn, $air_date, $episode_count);
                """;
            command.Parameters.AddWithValue("$run_id", _runId);
            command.Parameters.AddWithValue("$id", row.Id);
            command.Parameters.AddWithValue("$name", row.Name);
            command.Parameters.AddWithValue("$name_cn", (object?)row.NameCn ?? DBNull.Value);
            command.Parameters.AddWithValue("$air_date", (object?)row.AirDate ?? DBNull.Value);
            command.Parameters.AddWithValue("$episode_count", row.EpisodeCount);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await AfterInsertAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask InsertEpisodeAsync(
            EpisodeRow row,
            CancellationToken cancellationToken)
        {
            if (_kind != DataAssetKind.Episodes)
            {
                throw new InvalidOperationException("Episode inserted through a subject writer.");
            }
            await EnsureTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = """
                INSERT INTO data_update_staging_episodes (
                    run_id, episode_id, subject_id, sort_number, episode_number, air_date)
                VALUES ($run_id, $id, $subject_id, $sort, $episode, $air_date);
                """;
            command.Parameters.AddWithValue("$run_id", _runId);
            command.Parameters.AddWithValue("$id", row.Id);
            command.Parameters.AddWithValue("$subject_id", row.SubjectId);
            command.Parameters.AddWithValue("$sort", row.Sort);
            command.Parameters.AddWithValue("$episode", row.Episode);
            command.Parameters.AddWithValue("$air_date", (object?)row.AirDate ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await AfterInsertAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask FlushAsync(CancellationToken cancellationToken)
        {
            if (_transaction is null)
            {
                return;
            }
            await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
            _pending = 0;
        }

        private async ValueTask EnsureTransactionAsync(CancellationToken cancellationToken)
        {
            _transaction ??= (SqliteTransaction)await _connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask AfterInsertAsync(CancellationToken cancellationToken)
        {
            _pending++;
            if (_pending >= InsertBatchSize)
            {
                await FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_transaction is not null)
            {
                await _transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
