using System.Text.Json;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Library;

namespace AnimeGoNet.Data.Library;

public sealed class DirectoryDatabaseWriter
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The writer is an injectable media-organization service boundary.")]
    public async Task<IReadOnlyList<DirectoryDatabaseEntry>> WriteAsync(
        DirectoryDatabaseWriteRequest request,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SaveRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InfoHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AnimeName);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.SeasonNumber, 1);
        var root = Path.GetFullPath(request.SaveRootPath);
        var seriesName = MediaPathPlanner.SanitizeSegment(request.AnimeName);
        var seriesDirectory = PathBoundary.Combine(root, seriesName);
        var seasonDirectory = PathBoundary.Combine(
            seriesDirectory,
            $"S{request.SeasonNumber:00}");
        EnsureSafeDirectory(root, seriesDirectory);
        EnsureSafeDirectory(root, seasonDirectory);

        var now = utcNow.ToUnixTimeSeconds();
        var entries = new List<DirectoryDatabaseEntry>(request.Episodes.Count + 2);
        var animePath = Path.Combine(seriesDirectory, DirectoryDatabaseScanner.AnimeFileName);
        var animeExisting = await TryReadExistingAsync(
            root, animePath, DirectoryDatabaseEntryKind.Anime, cancellationToken).ConfigureAwait(false);
        var anime = new DirectoryDatabaseEntry(
            DirectoryDatabaseScanner.NormalizeRelative(root, animePath),
            DirectoryDatabaseEntryKind.Anime,
            animeExisting?.InfoHash ?? request.InfoHash,
            request.AnimeName,
            animeExisting?.CreateAtUnix ?? now,
            now);
        await WriteAtomicAsync(animePath, anime, cancellationToken).ConfigureAwait(false);
        entries.Add(anime);

        var seasonPath = Path.Combine(seasonDirectory, DirectoryDatabaseScanner.SeasonFileName);
        var seasonExisting = await TryReadExistingAsync(
            root, seasonPath, DirectoryDatabaseEntryKind.Season, cancellationToken).ConfigureAwait(false);
        var season = new DirectoryDatabaseEntry(
            DirectoryDatabaseScanner.NormalizeRelative(root, seasonPath),
            DirectoryDatabaseEntryKind.Season,
            seasonExisting?.InfoHash ?? anime.InfoHash,
            request.AnimeName,
            seasonExisting?.CreateAtUnix ?? now,
            now,
            request.SeasonNumber);
        await WriteAtomicAsync(seasonPath, season, cancellationToken).ConfigureAwait(false);
        entries.Add(season);

        foreach (var episodeRequest in request.Episodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (episodeRequest.EpisodeType is < 0 or > 2
                || episodeRequest.EpisodeNumber < 0
                || (episodeRequest.EpisodeType == 1 && episodeRequest.EpisodeNumber == 0))
            {
                throw new ArgumentException("Episode sidecar identity is invalid.", nameof(request));
            }
            var mediaPath = Path.GetFullPath(episodeRequest.MediaPath);
            if (!PathBoundary.IsWithin(root, mediaPath))
            {
                throw new IOException("Episode media path is outside the captured save root.");
            }
            var mediaDirectory = Path.GetDirectoryName(mediaPath)
                ?? throw new IOException("Episode media path has no directory.");
            EnsureSafeDirectory(root, mediaDirectory);
            var sidecarPath = Path.Combine(
                mediaDirectory,
                Path.GetFileNameWithoutExtension(mediaPath) + DirectoryDatabaseScanner.EpisodeSuffix);
            var existing = await TryReadExistingAsync(
                root, sidecarPath, DirectoryDatabaseEntryKind.Episode, cancellationToken).ConfigureAwait(false);
            var episode = new DirectoryDatabaseEntry(
                DirectoryDatabaseScanner.NormalizeRelative(root, sidecarPath),
                DirectoryDatabaseEntryKind.Episode,
                request.InfoHash,
                request.AnimeName,
                existing?.CreateAtUnix ?? now,
                now,
                request.SeasonNumber,
                episodeRequest.EpisodeType,
                episodeRequest.EpisodeNumber,
                request.Seeded || existing?.Seeded == true,
                true,
                true,
                true);
            await WriteAtomicAsync(sidecarPath, episode, cancellationToken).ConfigureAwait(false);
            entries.Add(episode);
        }

        return entries;
    }

    private static async Task<DirectoryDatabaseEntry?> TryReadExistingAsync(
        string root,
        string path,
        DirectoryDatabaseEntryKind kind,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        var info = new FileInfo(path);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)
            || info.LinkTarget is not null
            || info.Length is <= 0 or > DirectoryDatabaseScanner.MaximumSidecarBytes)
        {
            throw new IOException("Existing directory database sidecar is unsafe or invalid.");
        }
        try
        {
            return await DirectoryDatabaseScanner.ReadAsync(
                path,
                DirectoryDatabaseScanner.NormalizeRelative(root, path),
                kind,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new IOException("Existing directory database sidecar is invalid.", exception);
        }
        catch (DirectoryDatabaseScanner.SidecarException exception)
        {
            throw new IOException("Existing directory database sidecar is invalid.", exception);
        }
    }

    private static async Task WriteAtomicAsync(
        string path,
        DirectoryDatabaseEntry entry,
        CancellationToken cancellationToken)
    {
        var temporary = path + $".animegonet-{Guid.NewGuid():N}.partial";
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                writer.WriteStartObject();
                writer.WritePropertyName("info");
                writer.WriteStartObject();
                writer.WriteString("hash", entry.InfoHash);
                writer.WriteString("name", entry.AnimeName);
                writer.WriteNumber("create_at", entry.CreateAtUnix);
                writer.WriteNumber("update_at", entry.UpdateAtUnix);
                writer.WriteEndObject();
                if (entry.Kind == DirectoryDatabaseEntryKind.Season)
                {
                    writer.WriteNumber("season", entry.SeasonNumber!.Value);
                }
                else if (entry.Kind == DirectoryDatabaseEntryKind.Episode)
                {
                    writer.WritePropertyName("state");
                    writer.WriteStartObject();
                    writer.WriteBoolean("seeded", entry.Seeded!.Value);
                    writer.WriteBoolean("downloaded", entry.Downloaded!.Value);
                    writer.WriteBoolean("renamed", entry.Renamed!.Value);
                    writer.WriteBoolean("scraped", entry.Scraped!.Value);
                    writer.WriteEndObject();
                    writer.WriteNumber("season", entry.SeasonNumber!.Value);
                    writer.WriteNumber("type", entry.EpisodeType!.Value);
                    writer.WriteNumber("ep", entry.EpisodeNumber!.Value);
                }
                writer.WriteEndObject();
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void EnsureSafeDirectory(string root, string directory)
    {
        if (!PathBoundary.IsWithin(root, directory))
        {
            throw new IOException("Directory database path is outside the captured save root.");
        }
        Directory.CreateDirectory(directory);
        var current = new DirectoryInfo(directory);
        while (current is not null && PathBoundary.IsWithin(root, current.FullName))
        {
            if (current.Attributes.HasFlag(FileAttributes.ReparsePoint) || current.LinkTarget is not null)
            {
                throw new IOException("Symbolic links are not allowed in directory database paths.");
            }
            if (string.Equals(
                    Path.GetFullPath(current.FullName).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                return;
            }
            current = current.Parent;
        }
        throw new IOException("Directory database path does not reach the captured save root.");
    }
}
