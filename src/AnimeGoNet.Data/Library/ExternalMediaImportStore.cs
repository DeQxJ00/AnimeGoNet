using System.Globalization;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Library;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Library;

public sealed class ExternalMediaImportStore(AnimeGoSqliteDatabase database) : IDisposable
{
    public const string SourceId = "external_import";

    private readonly SemaphoreSlim _scanGate = new(1, 1);

    public Task<ExternalMediaImportResult> ScanAllAsync(
        string saveRoot,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        ScanAsync(saveRoot, null, null, utcNow, cancellationToken);

    public async Task<ExternalMediaImportResult?> ScanSeasonAsync(
        string saveRoot,
        int tmdbSeriesId,
        int seasonNumber,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tmdbSeriesId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(seasonNumber, 1);
        var result = await ScanAsync(
            saveRoot,
            tmdbSeriesId,
            seasonNumber,
            utcNow,
            cancellationToken).ConfigureAwait(false);
        return result.ScannedSeasonCount == 0 ? null : result;
    }

    private async Task<ExternalMediaImportResult> ScanAsync(
        string saveRoot,
        int? tmdbSeriesId,
        int? seasonNumber,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveRoot);
        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = Path.GetFullPath(saveRoot);
            var seasons = await LoadSeasonsAsync(
                tmdbSeriesId,
                seasonNumber,
                cancellationToken).ConfigureAwait(false);
            if (seasons.Count == 0)
            {
                return Empty();
            }

            var items = new List<ExternalMediaImportItem>();
            var candidates = new List<ImportCandidate>();
            var candidateFileCount = 0;
            if (!Directory.Exists(root))
            {
                return Result(seasons.Count, candidates.Count, items);
            }

            var rootInfo = new DirectoryInfo(root);
            if (IsSymbolic(rootInfo))
            {
                throw new IOException("External media save root must not be a symbolic link or reparse point.");
            }

            var paths = seasons
                .Select(value => new SeasonPath(
                    value,
                    PathBoundary.Combine(
                        PathBoundary.Combine(
                            root,
                            MediaPathPlanner.SanitizeSegment(value.DisplayName)),
                        $"S{value.SeasonNumber:00}")))
                .ToArray();
            var comparison = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var ambiguousPaths = paths
                .GroupBy(value => value.Path, comparison)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(comparison);

            foreach (var seasonPath in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ambiguousPaths.Contains(seasonPath.Path))
                {
                    items.Add(Skipped(
                        seasonPath.Season,
                        null,
                        Relative(root, seasonPath.Path),
                        "external_media_season_path_ambiguous"));
                    continue;
                }
                if (!Directory.Exists(seasonPath.Path))
                {
                    continue;
                }

                if (HasUnsafeDirectoryBoundary(root, seasonPath.Path))
                {
                    items.Add(Skipped(
                        seasonPath.Season,
                        null,
                        Relative(root, seasonPath.Path),
                        "external_media_season_path_unsafe"));
                    continue;
                }

                foreach (var path in Directory.EnumerateFiles(
                             seasonPath.Path,
                             "*",
                             SearchOption.TopDirectoryOnly)
                         .OrderBy(value => value, comparison))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!SubtitleAssociationResolver.IsVideo(path))
                    {
                        continue;
                    }
                    candidateFileCount++;

                    var relativePath = Relative(root, path);
                    var file = new FileInfo(path);
                    try
                    {
                        if (IsSymbolic(file))
                        {
                            items.Add(Skipped(
                                seasonPath.Season,
                                null,
                                relativePath,
                                "external_media_file_unsafe"));
                            continue;
                        }
                        if (file.Length <= 0)
                        {
                            items.Add(Skipped(
                                seasonPath.Season,
                                null,
                                relativePath,
                                "external_media_file_empty"));
                            continue;
                        }
                    }
                    catch (IOException)
                    {
                        items.Add(Skipped(
                            seasonPath.Season,
                            null,
                            relativePath,
                            "external_media_file_unreadable"));
                        continue;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        items.Add(Skipped(
                            seasonPath.Season,
                            null,
                            relativePath,
                            "external_media_file_unreadable"));
                        continue;
                    }
                    if (!TryParseEpisodeNumber(path, out var episodeNumber))
                    {
                        items.Add(Skipped(
                            seasonPath.Season,
                            null,
                            relativePath,
                            "external_media_filename_invalid"));
                        continue;
                    }
                    if (!seasonPath.Season.EpisodeNumbers.Contains(episodeNumber))
                    {
                        items.Add(Skipped(
                            seasonPath.Season,
                            episodeNumber,
                            relativePath,
                            "external_media_tmdb_episode_missing"));
                        continue;
                    }

                    candidates.Add(new ImportCandidate(
                        seasonPath.Season,
                        episodeNumber,
                        path,
                        relativePath));
                }
            }

            var uniqueCandidates = new List<ImportCandidate>(candidates.Count);
            foreach (var group in candidates.GroupBy(candidate => (
                         candidate.Season.TmdbSeriesId,
                         candidate.Season.SeasonNumber,
                         candidate.EpisodeNumber)))
            {
                var matches = group.ToArray();
                if (matches.Length == 1)
                {
                    uniqueCandidates.Add(matches[0]);
                    continue;
                }

                items.AddRange(matches.Select(candidate => Skipped(
                    candidate.Season,
                    candidate.EpisodeNumber,
                    candidate.RelativePath,
                    "external_media_episode_ambiguous")));
            }

            await ImportAsync(uniqueCandidates, items, utcNow, cancellationToken)
                .ConfigureAwait(false);
            return Result(seasons.Count, candidateFileCount, items);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private async Task<IReadOnlyList<SeasonIdentity>> LoadSeasonsAsync(
        int? tmdbSeriesId,
        int? seasonNumber,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT series.tmdb_series_id,
                   season.season_number,
                   COALESCE(NULLIF(series.canonical_name, ''),
                            NULLIF(series.original_name, ''),
                            'TMDB ' || series.tmdb_series_id),
                   episode.episode_number
            FROM anime_seasons AS season
            JOIN anime_series AS series ON series.id = season.series_id
            LEFT JOIN tmdb_episodes AS episode
              ON episode.series_id = series.id
             AND episode.season_number = season.season_number
            WHERE series.tmdb_series_id > 0
              AND series.needs_tmdb_completion = 0
              AND season.season_number > 0
              AND ($series_id IS NULL OR series.tmdb_series_id = $series_id)
              AND ($season_number IS NULL OR season.season_number = $season_number)
            ORDER BY series.tmdb_series_id, season.season_number, episode.episode_number;
            """;
        command.Parameters.AddWithValue("$series_id", (object?)tmdbSeriesId ?? DBNull.Value);
        command.Parameters.AddWithValue("$season_number", (object?)seasonNumber ?? DBNull.Value);
        var values = new List<SeasonIdentity>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var seriesId = reader.GetInt32(0);
            var currentSeason = reader.GetInt32(1);
            var value = values.LastOrDefault(item =>
                item.TmdbSeriesId == seriesId && item.SeasonNumber == currentSeason);
            if (value is null)
            {
                value = new SeasonIdentity(seriesId, currentSeason, reader.GetString(2), []);
                values.Add(value);
            }
            if (!reader.IsDBNull(3))
            {
                value.EpisodeNumbers.Add(reader.GetInt32(3));
            }
        }
        return values;
    }

    private async Task ImportAsync(
        IReadOnlyList<ImportCandidate> candidates,
        List<ExternalMediaImportItem> items,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return;
        }

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new FileInfo(candidate.FullPath);
            try
            {
                if (!file.Exists || file.Length <= 0 || IsSymbolic(file))
                {
                    items.Add(Skipped(
                        candidate.Season,
                        candidate.EpisodeNumber,
                        candidate.RelativePath,
                        "external_media_file_changed"));
                    continue;
                }
            }
            catch (IOException)
            {
                items.Add(Skipped(
                    candidate.Season,
                    candidate.EpisodeNumber,
                    candidate.RelativePath,
                    "external_media_file_changed"));
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                items.Add(Skipped(
                    candidate.Season,
                    candidate.EpisodeNumber,
                    candidate.RelativePath,
                    "external_media_file_changed"));
                continue;
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO completion_records (
                    id, tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                    source_id, source_item_id, media_path, completed_at_utc)
                SELECT $id, $series, $season, $episode,
                       $source, $source_item, $media_path, $completed
                WHERE EXISTS (
                    SELECT 1
                    FROM tmdb_episodes AS episode_snapshot
                    JOIN anime_series AS series ON series.id = episode_snapshot.series_id
                    WHERE series.tmdb_series_id = $series
                      AND series.needs_tmdb_completion = 0
                      AND episode_snapshot.season_number = $season
                      AND episode_snapshot.episode_number = $episode);
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$series", candidate.Season.TmdbSeriesId);
            command.Parameters.AddWithValue("$season", candidate.Season.SeasonNumber);
            command.Parameters.AddWithValue("$episode", candidate.EpisodeNumber);
            command.Parameters.AddWithValue("$source", SourceId);
            command.Parameters.AddWithValue("$source_item", candidate.RelativePath);
            command.Parameters.AddWithValue("$media_path", candidate.FullPath);
            command.Parameters.AddWithValue(
                "$completed",
                utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            var inserted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
            if (inserted)
            {
                await using var completeClaim = connection.CreateCommand();
                completeClaim.Transaction = transaction;
                completeClaim.CommandText = """
                    UPDATE episode_claims
                    SET state = 'completed', expires_at_utc = NULL
                    WHERE tmdb_series_id = $series
                      AND tmdb_season_number = $season
                      AND tmdb_episode_number = $episode
                      AND state = 'active';
                    """;
                completeClaim.Parameters.AddWithValue("$series", candidate.Season.TmdbSeriesId);
                completeClaim.Parameters.AddWithValue("$season", candidate.Season.SeasonNumber);
                completeClaim.Parameters.AddWithValue("$episode", candidate.EpisodeNumber);
                await completeClaim.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            items.Add(new ExternalMediaImportItem(
                candidate.Season.TmdbSeriesId,
                candidate.Season.SeasonNumber,
                candidate.EpisodeNumber,
                candidate.RelativePath,
                inserted ? "imported" : "already_recorded",
                null));
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool TryParseEpisodeNumber(string path, out int episodeNumber)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        if (stem.Length < 4 || stem[0] is not ('E' or 'e'))
        {
            episodeNumber = 0;
            return false;
        }
        var digits = stem.AsSpan(1);
        if (digits.IndexOfAnyExceptInRange('0', '9') >= 0)
        {
            episodeNumber = 0;
            return false;
        }
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out episodeNumber)
            && episodeNumber > 0;
    }

    private static ExternalMediaImportItem Skipped(
        SeasonIdentity season,
        int? episodeNumber,
        string relativePath,
        string reasonCode) =>
        new(
            season.TmdbSeriesId,
            season.SeasonNumber,
            episodeNumber,
            relativePath,
            "skipped",
            reasonCode);

    private static ExternalMediaImportResult Empty() => new(0, 0, 0, 0, 0, []);

    private static ExternalMediaImportResult Result(
        int seasonCount,
        int candidateCount,
        IReadOnlyList<ExternalMediaImportItem> items) =>
        new(
            seasonCount,
            candidateCount,
            items.Count(item => item.Status == "imported"),
            items.Count(item => item.Status == "already_recorded"),
            items.Count(item => item.Status == "skipped"),
            items
                .OrderBy(item => item.TmdbSeriesId)
                .ThenBy(item => item.TmdbSeasonNumber)
                .ThenBy(item => item.TmdbEpisodeNumber ?? int.MaxValue)
                .ThenBy(item => item.RelativePath, StringComparer.Ordinal)
                .ToArray());

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static bool IsSymbolic(FileSystemInfo info) =>
        info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget is not null;

    private static bool HasUnsafeDirectoryBoundary(string root, string directory)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var current = new DirectoryInfo(directory);
        while (PathBoundary.IsWithin(root, current.FullName))
        {
            if (IsSymbolic(current))
            {
                return true;
            }
            if (string.Equals(
                Path.GetFullPath(current.FullName).TrimEnd(Path.DirectorySeparatorChar),
                rootPath,
                comparison))
            {
                return false;
            }
            if (current.Parent is null)
            {
                break;
            }
            current = current.Parent;
        }
        return true;
    }

    public void Dispose() => _scanGate.Dispose();

    private sealed record SeasonIdentity(
        int TmdbSeriesId,
        int SeasonNumber,
        string DisplayName,
        HashSet<int> EpisodeNumbers);

    private sealed record SeasonPath(SeasonIdentity Season, string Path);

    private sealed record ImportCandidate(
        SeasonIdentity Season,
        int EpisodeNumber,
        string FullPath,
        string RelativePath);
}
