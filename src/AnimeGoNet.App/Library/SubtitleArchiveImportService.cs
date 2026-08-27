using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Library;

namespace AnimeGoNet.App.Library;

public sealed record SubtitleArchiveCandidate(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("file_name")] string FileName,
    [property: JsonPropertyName("relative_path")] string RelativePath,
    [property: JsonPropertyName("size_bytes")] long SizeBytes,
    [property: JsonPropertyName("parsed_episode")] int? ParsedEpisode,
    [property: JsonPropertyName("parsed_range")] string? ParsedRange,
    [property: JsonPropertyName("selected_episode")] int? SelectedEpisode);

public sealed record SubtitleArchiveImportSession(
    string SessionId,
    string ArchiveName,
    int TmdbSeriesId,
    int SeasonNumber,
    string SeriesName,
    IReadOnlyList<SubtitleArchiveCandidate> Candidates);

public sealed record SubtitleArchiveAssignment(
    [property: JsonPropertyName("candidate_id")] string CandidateId,
    [property: JsonPropertyName("episode_number")] int? EpisodeNumber);

public sealed record SubtitleArchiveImportConfirmation(
    string SessionId,
    int ImportedCount,
    int ExtrasCount,
    IReadOnlyList<string> ImportedPaths);

public sealed class SubtitleArchiveImportService(DirectoryLayout layout)
{
    private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ass", ".ssa", ".srt", ".vtt", ".sub", ".idx", ".sup", ".smi", ".ttml", ".xml"
    };
    private static readonly Regex EpisodeMarker = new(
        @"(?:\bS\d{1,2}[ ._-]*)?E(?<ep>\d{1,4})(?:\b|[-_.])|(?:^|[ ._\-\[\(])(?<ep2>\d{1,3})(?:[-~](?<ep3>\d{1,3}))?(?:[ ._\-\]\)]|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private string Root => Path.Combine(layout.StagingPath, "subtitle-imports");

    public async Task<SubtitleArchiveImportSession> ImportAsync(
        Stream archive,
        string archiveName,
        int tmdbSeriesId,
        int seasonNumber,
        string seriesName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveName);
        ArgumentOutOfRangeException.ThrowIfLessThan(tmdbSeriesId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(seasonNumber, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(seriesName);

        var sessionId = Guid.NewGuid().ToString("N");
        var sessionRoot = Path.Combine(Root, sessionId);
        var filesRoot = Path.Combine(sessionRoot, "files");
        Directory.CreateDirectory(filesRoot);
        var candidates = new List<SubtitleArchiveCandidate>();
        try
        {
            // Kestrel request bodies disallow synchronous reads.  ZipArchive performs
            // synchronous seeks/reads while opening entries, so first spool the upload
            // asynchronously to a private staging file and only then hand a regular
            // FileStream to ZipArchive.  This also avoids retaining a potentially large
            // ZIP in memory during import.
            var archivePath = Path.Combine(sessionRoot, "archive.zip");
            await using (var staged = new FileStream(
                archivePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var totalBytes = 0L;
                var buffer = new byte[64 * 1024];
                while (true)
                {
                    var read = await archive.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    totalBytes = checked(totalBytes + read);
                    if (totalBytes > 512L * 1024 * 1024)
                    {
                        throw new InvalidDataException("字幕压缩包不能超过 512 MiB。");
                    }

                    await staged.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            await using var stagedArchive = new FileStream(
                archivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan);
            using var zip = new ZipArchive(stagedArchive, ZipArchiveMode.Read, leaveOpen: false);
            long extractedBytes = 0;
            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }
                var extension = Path.GetExtension(entry.Name);
                if (!SubtitleExtensions.Contains(extension))
                {
                    continue;
                }
                if (candidates.Count >= 500 || entry.Length > 128L * 1024 * 1024
                    || (extractedBytes = checked(extractedBytes + entry.Length)) > 512L * 1024 * 1024)
                {
                    throw new InvalidDataException("字幕压缩包文件数量或解压后大小超过限制。");
                }
                var relative = NormalizeEntry(entry.FullName);
                var target = PathBoundary.Combine(filesRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using (var input = entry.Open())
                await using (var output = File.Create(target))
                {
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }

                var parsed = ParseEpisode(entry.Name);
                candidates.Add(new SubtitleArchiveCandidate(
                    Guid.NewGuid().ToString("N"),
                    entry.Name,
                    relative,
                    entry.Length,
                    parsed.Episode,
                    parsed.Range,
                    parsed.Episode));
            }

            var session = new SubtitleArchiveImportSession(
                sessionId,
                Path.GetFileName(archiveName),
                tmdbSeriesId,
                seasonNumber,
                seriesName,
                candidates);
            await File.WriteAllTextAsync(
                Path.Combine(sessionRoot, "session.json"),
                System.Text.Json.JsonSerializer.Serialize(session, SubtitleArchiveImportJsonContext.Default.SubtitleArchiveImportSession),
                cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            TryDelete(sessionRoot);
            throw;
        }
    }

    public async Task<SubtitleArchiveImportSession?> GetAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!IsSafeId(sessionId)) return null;
        var path = Path.Combine(Root, sessionId, "session.json");
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await System.Text.Json.JsonSerializer.DeserializeAsync(
            stream, SubtitleArchiveImportJsonContext.Default.SubtitleArchiveImportSession, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SubtitleArchiveImportConfirmation?> ConfirmAsync(
        string sessionId,
        IReadOnlyList<SubtitleArchiveAssignment> assignments,
        string saveRoot,
        CancellationToken cancellationToken = default)
    {
        var session = await GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null) return null;
        var byId = session.Candidates.ToDictionary(value => value.Id, StringComparer.Ordinal);
        var seasonRoot = PathBoundary.Combine(
            PathBoundary.Combine(saveRoot, MediaPathPlanner.SanitizeSegment(session.SeriesName)),
            $"S{session.SeasonNumber:00}");
        var imported = new List<string>();
        var extras = 0;
        foreach (var assignment in assignments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!byId.TryGetValue(assignment.CandidateId, out var candidate)) continue;
            var source = PathBoundary.Combine(Path.Combine(Root, session.SessionId, "files"), candidate.RelativePath);
            if (!File.Exists(source)) continue;
            var episode = assignment.EpisodeNumber ?? candidate.SelectedEpisode;
            string relative;
            if (episode is > 0)
            {
                relative = MediaPathPlanner.PlanRelativePath(new MediaPathInput(
                    session.SeriesName, session.SeasonNumber, "episode", episode,
                    candidate.FileName, SubtitleSuffix(candidate.FileName)));
                imported.Add(relative);
            }
            else
            {
                relative = MediaPathPlanner.PlanRelativePath(new MediaPathInput(
                    session.SeriesName, session.SeasonNumber, "extras", null, candidate.FileName));
                extras++;
            }
            var target = PathBoundary.Combine(saveRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }
        TryDelete(Path.Combine(Root, session.SessionId));
        return new SubtitleArchiveImportConfirmation(session.SessionId, imported.Count, extras, imported);
    }

    private static (int? Episode, string? Range) ParseEpisode(string name)
    {
        var match = EpisodeMarker.Match(Path.GetFileNameWithoutExtension(name));
        if (!match.Success) return (null, null);
        var value = match.Groups["ep"].Success ? match.Groups["ep"].Value : match.Groups["ep2"].Value;
        if (!int.TryParse(value, out var episode) || episode <= 0) return (null, null);
        var end = match.Groups["ep3"];
        return (episode, end.Success ? $"{episode}-{end.Value}" : null);
    }

    private static string SubtitleSuffix(string name)
    {
        var match = Regex.Match(
            name,
            @"\.(?<language>[A-Za-z]{2,8}(?:[-_][A-Za-z0-9]{2,8})?)\.(?<extension>ass|ssa|srt|vtt|sub|idx|sup|smi|ttml|xml)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Value : Path.GetExtension(name);
    }

    private static string NormalizeEntry(string value)
    {
        var normalized = value.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Split('/').Any(part => part is ".." or "."))
            throw new InvalidDataException("Subtitle archive contains an unsafe path.");
        return string.Join('/', normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(MediaPathPlanner.SanitizeSegment));
    }

    private static bool IsSafeId(string value) =>
        value.Length == 32 && value.All(char.IsAsciiLetterOrDigit);

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best effort cleanup */ }
    }
}

[JsonSerializable(typeof(SubtitleArchiveImportSession))]
internal partial class SubtitleArchiveImportJsonContext : JsonSerializerContext;
