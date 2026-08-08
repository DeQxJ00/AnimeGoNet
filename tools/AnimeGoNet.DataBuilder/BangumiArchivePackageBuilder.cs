using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AnimeGoNet.Core.DataUpdate;

namespace AnimeGoNet.DataBuilder;

public sealed record BangumiArchiveBuildOptions(
    string InputArchivePath,
    string OutputDirectory,
    string DataVersion,
    Uri AssetBaseUrl,
    string UpstreamRepository,
    string UpstreamRelease,
    string UpstreamAsset,
    string UpstreamSha256,
    DateTimeOffset GeneratedAtUtc,
    string MinimumClientVersion = "0.1.0",
    int SubjectsPerShard = 25_000,
    int MinimumSubjectCount = 1,
    int MinimumEpisodeCount = 1);

public sealed record BangumiArchiveBuildResult(
    string OutputDirectory,
    string ManifestPath,
    string OfflinePackagePath,
    string ManifestSha256,
    DataManifest Manifest);

public static partial class BangumiArchivePackageBuilder
{
    private const string SubjectEntryName = "subject.jsonlines";
    private const string EpisodeEntryName = "episode.jsonlines";
    private const int MaximumLineBytes = 8 * 1024 * 1024;

    public static async Task<BangumiArchiveBuildResult> BuildAsync(
        BangumiArchiveBuildOptions options,
        CancellationToken cancellationToken = default)
    {
        Validate(options);
        var inputPath = Path.GetFullPath(options.InputArchivePath);
        var outputPath = Path.GetFullPath(options.OutputDirectory);
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("The Bangumi Archive ZIP does not exist.", inputPath);
        }
        if (!string.Equals(
            Path.GetFileName(inputPath),
            options.UpstreamAsset,
            StringComparison.Ordinal))
        {
            throw new InvalidDataException("The declared upstream asset name does not match the input ZIP.");
        }
        if (Directory.Exists(outputPath) || File.Exists(outputPath))
        {
            throw new IOException("The output path must not already exist.");
        }

        var actualUpstreamSha256 = await Sha256FileAsync(inputPath, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
            actualUpstreamSha256,
            options.UpstreamSha256,
            StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Bangumi Archive ZIP SHA-256 does not match.");
        }

        var parent = Path.GetDirectoryName(outputPath)
            ?? throw new ArgumentException("The output directory has no parent.", nameof(options));
        Directory.CreateDirectory(parent);
        var stagingPath = Path.Combine(
            parent,
            $".{Path.GetFileName(outputPath)}.partial-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingPath);
        try
        {
            var data = await ReadArchiveAsync(inputPath, cancellationToken).ConfigureAwait(false);
            if (data.Subjects.Count < options.MinimumSubjectCount)
            {
                throw new InvalidDataException(
                    $"The Bangumi Archive contains {data.Subjects.Count} anime Subjects, below the configured minimum of {options.MinimumSubjectCount}.");
            }
            if (data.Episodes.Count < options.MinimumEpisodeCount)
            {
                throw new InvalidDataException(
                    $"The Bangumi Archive contains {data.Episodes.Count} normal anime Episodes, below the configured minimum of {options.MinimumEpisodeCount}.");
            }
            var assets = await WriteAssetsAsync(stagingPath, options, data, cancellationToken)
                .ConfigureAwait(false);
            var manifest = new DataManifest(
                DataManifestParser.CurrentSchemaVersion,
                options.DataVersion,
                options.GeneratedAtUtc,
                options.MinimumClientVersion,
                new DataManifestUpstream(
                    options.UpstreamRepository,
                    options.UpstreamRelease,
                    options.UpstreamAsset,
                    options.UpstreamSha256),
                assets,
                data.Subjects.Count,
                data.Episodes.Count);
            var manifestBytes = RenderManifest(manifest);
            _ = DataManifestParser.Parse(manifestBytes);
            var manifestPath = Path.Combine(stagingPath, "manifest.json");
            await File.WriteAllBytesAsync(manifestPath, manifestBytes, cancellationToken)
                .ConfigureAwait(false);
            var manifestSha256 = Convert.ToHexStringLower(SHA256.HashData(manifestBytes));
            var packageName = $"animegonet-data-{options.DataVersion}.zip";
            var packagePath = Path.Combine(stagingPath, packageName);
            await WriteOfflinePackageAsync(
                packagePath,
                manifestPath,
                assets.Select(asset => Path.Combine(stagingPath, asset.FileName)),
                options.GeneratedAtUtc,
                cancellationToken).ConfigureAwait(false);

            Directory.Move(stagingPath, outputPath);
            return new BangumiArchiveBuildResult(
                outputPath,
                Path.Combine(outputPath, "manifest.json"),
                Path.Combine(outputPath, packageName),
                manifestSha256,
                manifest);
        }
        catch
        {
            TryDeleteDirectory(stagingPath);
            throw;
        }
    }

    private static async Task<ArchiveData> ReadArchiveAsync(
        string inputPath,
        CancellationToken cancellationToken)
    {
        using var file = new FileStream(
            inputPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
        var subjectEntry = RequireSingleRootEntry(archive, SubjectEntryName);
        var episodeEntry = RequireSingleRootEntry(archive, EpisodeEntryName);
        var subjects = new SortedDictionary<int, NormalizedSubject>();
        await ReadJsonLinesAsync(
            subjectEntry,
            line =>
            {
                using var document = ParseLine(line, "subject");
                var root = document.RootElement;
                if (OptionalInt(root, "type") != 2)
                {
                    return;
                }

                var id = RequiredPositiveInt(root, "id", "subject");
                var name = NormalizeRequiredText(root, "name", "subject");
                var chineseName = NormalizeOptionalText(root, "name_cn");
                var airDate = NormalizeDate(root, "date");
                if (!subjects.TryAdd(id, new NormalizedSubject(id, name, chineseName, airDate)))
                {
                    throw new InvalidDataException("The Bangumi Archive contains a duplicate anime Subject ID.");
                }
            },
            cancellationToken).ConfigureAwait(false);
        if (subjects.Count == 0)
        {
            throw new InvalidDataException("The Bangumi Archive contains no anime Subjects.");
        }

        var episodes = new List<NormalizedEpisode>();
        var episodeIds = new HashSet<int>();
        await ReadJsonLinesAsync(
            episodeEntry,
            line =>
            {
                using var document = ParseLine(line, "episode");
                var root = document.RootElement;
                if (OptionalInt(root, "type") != 0)
                {
                    return;
                }

                var subjectId = RequiredPositiveInt(root, "subject_id", "episode");
                if (!subjects.ContainsKey(subjectId))
                {
                    return;
                }
                var id = RequiredPositiveInt(root, "id", "episode");
                if (!episodeIds.Add(id))
                {
                    throw new InvalidDataException("The Bangumi Archive contains a duplicate normal Episode ID.");
                }
                var episodeNumber = RequiredPositiveDecimal(root, "sort");
                episodes.Add(new NormalizedEpisode(
                    id,
                    subjectId,
                    episodeNumber,
                    0,
                    NormalizeDate(root, "airdate")));
            },
            cancellationToken).ConfigureAwait(false);
        if (episodes.Count == 0)
        {
            throw new InvalidDataException("The Bangumi Archive contains no normal anime Episodes.");
        }

        episodes.Sort(static (left, right) =>
        {
            var subject = left.SubjectId.CompareTo(right.SubjectId);
            if (subject != 0) return subject;
            var episode = left.EpisodeNumber.CompareTo(right.EpisodeNumber);
            return episode != 0 ? episode : left.Id.CompareTo(right.Id);
        });
        var currentSubjectId = 0;
        var subjectSort = 0;
        for (var index = 0; index < episodes.Count; index++)
        {
            if (episodes[index].SubjectId != currentSubjectId)
            {
                currentSubjectId = episodes[index].SubjectId;
                subjectSort = 0;
            }
            episodes[index] = episodes[index] with { Sort = ++subjectSort };
        }
        var episodeCounts = episodes
            .GroupBy(episode => episode.SubjectId)
            .ToDictionary(group => group.Key, group => group.Count());
        return new ArchiveData(subjects.Values.ToArray(), episodes, episodeCounts);
    }

    private static async Task<IReadOnlyList<DataManifestAsset>> WriteAssetsAsync(
        string stagingPath,
        BangumiArchiveBuildOptions options,
        ArchiveData data,
        CancellationToken cancellationToken)
    {
        var assets = new List<DataManifestAsset>();
        var episodeIndex = 0;
        for (var start = 0; start < data.Subjects.Count; start += options.SubjectsPerShard)
        {
            var count = Math.Min(options.SubjectsPerShard, data.Subjects.Count - start);
            var subjectSlice = data.Subjects.Skip(start).Take(count).ToArray();
            var minimumId = subjectSlice[0].Id;
            var maximumId = subjectSlice[^1].Id;
            var suffix = ((start / options.SubjectsPerShard) + 1)
                .ToString("D4", CultureInfo.InvariantCulture);
            assets.Add(await WriteAssetAsync(
                stagingPath,
                options.AssetBaseUrl,
                DataAssetKind.Subjects,
                $"subjects-{suffix}.jsonl.gz",
                subjectSlice.Length,
                minimumId,
                maximumId,
                async gzip =>
                {
                    foreach (var subject in subjectSlice)
                    {
                        data.EpisodeCounts.TryGetValue(subject.Id, out var episodeCount);
                        await WriteSubjectAsync(gzip, subject, episodeCount, cancellationToken)
                            .ConfigureAwait(false);
                    }
                },
                cancellationToken).ConfigureAwait(false));

            var episodeStart = episodeIndex;
            while (episodeIndex < data.Episodes.Count
                && data.Episodes[episodeIndex].SubjectId <= maximumId)
            {
                episodeIndex++;
            }
            var episodeCountInShard = episodeIndex - episodeStart;
            if (episodeCountInShard == 0)
            {
                continue;
            }
            var episodeSlice = data.Episodes.Skip(episodeStart).Take(episodeCountInShard).ToArray();
            assets.Add(await WriteAssetAsync(
                stagingPath,
                options.AssetBaseUrl,
                DataAssetKind.Episodes,
                $"episodes-{suffix}.jsonl.gz",
                episodeSlice.Length,
                episodeSlice[0].SubjectId,
                episodeSlice[^1].SubjectId,
                async gzip =>
                {
                    foreach (var episode in episodeSlice.OrderBy(static episode => episode.Id))
                    {
                        await WriteEpisodeAsync(gzip, episode, cancellationToken)
                            .ConfigureAwait(false);
                    }
                },
                cancellationToken).ConfigureAwait(false));
        }
        return assets;
    }

    private static async Task<DataManifestAsset> WriteAssetAsync(
        string directory,
        Uri baseUrl,
        DataAssetKind kind,
        string fileName,
        long recordCount,
        int minimumId,
        int maximumId,
        Func<Stream, Task> writeRecords,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, fileName);
        await using (var file = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize, leaveOpen: false))
        {
            await writeRecords(gzip).ConfigureAwait(false);
        }

        var info = new FileInfo(path);
        return new DataManifestAsset(
            kind,
            fileName,
            new Uri(baseUrl, fileName),
            info.Length,
            await Sha256FileAsync(path, cancellationToken).ConfigureAwait(false),
            recordCount,
            minimumId,
            maximumId);
    }

    private static async Task WriteSubjectAsync(
        Stream output,
        NormalizedSubject subject,
        int episodeCount,
        CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", subject.Id);
            writer.WriteString("name", subject.Name);
            if (subject.ChineseName is null) writer.WriteNull("name_cn");
            else writer.WriteString("name_cn", subject.ChineseName);
            if (subject.AirDate is null) writer.WriteNull("air_date");
            else writer.WriteString("air_date", subject.AirDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteNumber("episode_count", episodeCount);
            writer.WriteEndObject();
        }
        await WriteLineAsync(output, buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteEpisodeAsync(
        Stream output,
        NormalizedEpisode episode,
        CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", episode.Id);
            writer.WriteNumber("subject_id", episode.SubjectId);
            writer.WriteNumber("sort", episode.Sort);
            writer.WriteString(
                "episode",
                episode.EpisodeNumber.ToString("0.############################", CultureInfo.InvariantCulture));
            if (episode.AirDate is null) writer.WriteNull("air_date");
            else writer.WriteString("air_date", episode.AirDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }
        await WriteLineAsync(output, buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteLineAsync(
        Stream output,
        ReadOnlyMemory<byte> value,
        CancellationToken cancellationToken)
    {
        await output.WriteAsync(value, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    private static byte[] RenderManifest(DataManifest manifest)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", manifest.SchemaVersion);
            writer.WriteString("data_version", manifest.DataVersion);
            writer.WriteString("generated_at_utc", manifest.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("minimum_client_version", manifest.MinimumClientVersion);
            writer.WriteStartObject("upstream");
            writer.WriteString("repository", manifest.Upstream.Repository);
            writer.WriteString("release", manifest.Upstream.Release);
            writer.WriteString("asset", manifest.Upstream.Asset);
            writer.WriteString("sha256", manifest.Upstream.Sha256);
            writer.WriteEndObject();
            writer.WriteStartArray("assets");
            foreach (var asset in manifest.Assets)
            {
                writer.WriteStartObject();
                writer.WriteString("kind", asset.Kind == DataAssetKind.Subjects ? "subjects" : "episodes");
                writer.WriteString("file_name", asset.FileName);
                writer.WriteString("url", asset.Url.AbsoluteUri);
                writer.WriteNumber("size_bytes", asset.SizeBytes);
                writer.WriteString("sha256", asset.Sha256);
                writer.WriteNumber("record_count", asset.RecordCount);
                writer.WriteNumber("subject_id_min", asset.SubjectIdMin);
                writer.WriteNumber("subject_id_max", asset.SubjectIdMax);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartObject("totals");
            writer.WriteNumber("subjects", manifest.SubjectCount);
            writer.WriteNumber("episodes", manifest.EpisodeCount);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        var bytes = new byte[buffer.WrittenCount + 1];
        buffer.WrittenSpan.CopyTo(bytes);
        bytes[^1] = (byte)'\n';
        return bytes;
    }

    private static async Task WriteOfflinePackageAsync(
        string packagePath,
        string manifestPath,
        IEnumerable<string> assetPaths,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            packagePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var path in new[] { manifestPath }.Concat(assetPaths.Order(StringComparer.Ordinal)))
        {
            var entry = archive.CreateEntry(Path.GetFileName(path), CompressionLevel.NoCompression);
            entry.LastWriteTime = generatedAtUtc;
            await using var target = entry.Open();
            await using var source = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ReadJsonLinesAsync(
        ZipArchiveEntry entry,
        Action<ReadOnlyMemory<byte>> consume,
        CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        var readBuffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        var line = new ArrayBufferWriter<byte>();
        try
        {
            while (true)
            {
                var read = await stream
                    .ReadAsync(readBuffer.AsMemory(0, readBuffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0) break;
                for (var index = 0; index < read; index++)
                {
                    var value = readBuffer[index];
                    if (value == (byte)'\n')
                    {
                        ConsumeLine(line, consume);
                        continue;
                    }
                    if (line.WrittenCount >= MaximumLineBytes)
                    {
                        throw new InvalidDataException("A Bangumi Archive JSONL record is too large.");
                    }
                    line.GetSpan(1)[0] = value;
                    line.Advance(1);
                }
            }
            if (line.WrittenCount > 0) ConsumeLine(line, consume);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(readBuffer);
        }
    }

    private static void ConsumeLine(
        ArrayBufferWriter<byte> line,
        Action<ReadOnlyMemory<byte>> consume)
    {
        var length = line.WrittenCount;
        if (length > 0 && line.WrittenSpan[length - 1] == (byte)'\r') length--;
        if (length > 0) consume(line.WrittenMemory[..length]);
        line.Clear();
    }

    private static ZipArchiveEntry RequireSingleRootEntry(ZipArchive archive, string name)
    {
        var matches = archive.Entries
            .Where(entry => string.Equals(entry.FullName, name, StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidDataException($"The Bangumi Archive ZIP must contain exactly one root {name} entry.");
    }

    private static JsonDocument ParseLine(ReadOnlyMemory<byte> line, string kind)
    {
        try
        {
            var document = JsonDocument.Parse(line, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return document;
            }
            document.Dispose();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"A Bangumi Archive {kind} record is invalid JSON.", exception);
        }
        throw new InvalidDataException($"A Bangumi Archive {kind} record must be an object.");
    }

    private static int OptionalInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : -1;

    private static int RequiredPositiveInt(JsonElement root, string name, string kind) =>
        root.TryGetProperty(name, out var value)
        && value.TryGetInt32(out var result)
        && result > 0
            ? result
            : throw new InvalidDataException($"A Bangumi Archive {kind} ID is invalid.");

    private static decimal RequiredPositiveDecimal(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
        && value.TryGetDecimal(out var result)
        && result > 0
            ? result
            : throw new InvalidDataException("A Bangumi Archive normal Episode number is invalid.");

    private static string NormalizeRequiredText(JsonElement root, string name, string kind)
    {
        var value = NormalizeOptionalText(root, name);
        return value ?? throw new InvalidDataException($"A Bangumi Archive {kind} name is empty.");
    }

    private static string? NormalizeOptionalText(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)
            || value.ValueKind is JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("A Bangumi Archive title has an invalid type.");
        }
        var text = value.GetString()!;
        var normalized = new string(text.Select(character => char.IsControl(character) ? ' ' : character).ToArray())
            .Trim();
        if (normalized.Length == 0) return null;
        return normalized.Length <= 1024 ? normalized : normalized[..1024].TrimEnd();
    }

    private static DateOnly? NormalizeDate(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        return DateOnly.TryParseExact(
            value.GetString(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
    }

    private static async Task<string> Sha256FileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static void Validate(BangumiArchiveBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!StableVersion().IsMatch(options.DataVersion))
            throw new ArgumentException("The data version is invalid.", nameof(options));
        if (!LowerSha256().IsMatch(options.UpstreamSha256))
            throw new ArgumentException("The upstream SHA-256 is invalid.", nameof(options));
        if (!Version.TryParse(options.MinimumClientVersion, out _))
            throw new ArgumentException("The minimum client version is invalid.", nameof(options));
        if (options.GeneratedAtUtc.Offset != TimeSpan.Zero
            || options.GeneratedAtUtc.Year is < 1980 or > 2107)
            throw new ArgumentException("The generated timestamp must be UTC and ZIP-compatible.", nameof(options));
        if (options.SubjectsPerShard is < 1 or > 1_000_000)
            throw new ArgumentException("Subjects per shard must be between 1 and 1000000.", nameof(options));
        if (options.MinimumSubjectCount is < 1 or > 10_000_000)
            throw new ArgumentException("Minimum Subject count must be between 1 and 10000000.", nameof(options));
        if (options.MinimumEpisodeCount is < 1 or > 100_000_000)
            throw new ArgumentException("Minimum Episode count must be between 1 and 100000000.", nameof(options));
        if (!IsSafeHttpBaseUrl(options.AssetBaseUrl))
            throw new ArgumentException("The asset base URL is invalid.", nameof(options));
        ValidateText(options.UpstreamRepository, 512, "upstream repository");
        ValidateText(options.UpstreamRelease, 256, "upstream release");
        ValidateText(options.UpstreamAsset, 256, "upstream asset");
    }

    private static bool IsSafeHttpBaseUrl(Uri value) =>
        value.IsAbsoluteUri
        && value.Scheme is "http" or "https"
        && string.IsNullOrEmpty(value.UserInfo)
        && string.IsNullOrEmpty(value.Query)
        && string.IsNullOrEmpty(value.Fragment)
        && value.AbsolutePath.EndsWith('/');

    private static void ValidateText(string value, int maximumLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Length > maximumLength
            || value.Any(char.IsControl))
        {
            throw new ArgumentException($"The {name} is invalid.");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableVersion();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex LowerSha256();

    private sealed record NormalizedSubject(int Id, string Name, string? ChineseName, DateOnly? AirDate);

    private sealed record NormalizedEpisode(
        int Id,
        int SubjectId,
        decimal EpisodeNumber,
        int Sort,
        DateOnly? AirDate);

    private sealed record ArchiveData(
        IReadOnlyList<NormalizedSubject> Subjects,
        IReadOnlyList<NormalizedEpisode> Episodes,
        IReadOnlyDictionary<int, int> EpisodeCounts);
}
