using System.Text.Json;
using System.Text.Json.Serialization;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Configuration;

public sealed record DownloaderOverrideEntry(
    string BaseUrl,
    string? Username,
    string? Password,
    string DownloadPath,
    bool Enabled,
    long Revision,
    DateTimeOffset UpdatedAtUtc);

public sealed record DownloaderOverrideSnapshot(
    int FormatVersion,
    long Revision,
    IReadOnlyDictionary<string, DownloaderOverrideEntry> Downloaders);

public sealed class DownloaderOverrideRevisionException : InvalidOperationException;

public sealed record DownloaderConfigurationRuntimeState(long AppliedRevision);

public sealed class DownloaderOverrideStore : IDisposable
{
    private const int CurrentFormatVersion = 1;
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DownloaderOverrideStore(string configurationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        _path = Path.Combine(configurationPath, "downloaders.private.json");
    }

    public void Dispose() => _gate.Dispose();

    public async Task<DownloaderOverrideSnapshot> LoadAsync(CancellationToken cancellationToken = default)
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

    public async Task<DownloaderOverrideSnapshot> UpsertAsync(
        string id,
        DownloaderOverrideEntry definition,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(definition);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            if (current.Revision != expectedRevision) throw new DownloaderOverrideRevisionException();
            var entries = new Dictionary<string, DownloaderOverrideEntry>(
                current.Downloaders, StringComparer.OrdinalIgnoreCase)
            {
                [id] = definition with
                {
                    Revision = current.Downloaders.TryGetValue(id, out var existing)
                        ? existing.Revision + 1
                        : 1,
                },
            };
            var saved = new DownloaderOverrideSnapshot(
                CurrentFormatVersion, current.Revision + 1, entries);
            await SaveCoreAsync(saved, cancellationToken).ConfigureAwait(false);
            return saved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DownloaderOverrideSnapshot> DeleteAsync(
        string id,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            if (current.Revision != expectedRevision) throw new DownloaderOverrideRevisionException();
            var entries = new Dictionary<string, DownloaderOverrideEntry>(
                current.Downloaders, StringComparer.OrdinalIgnoreCase);
            if (!entries.Remove(id)) throw new KeyNotFoundException("Downloader override was not found.");
            var saved = new DownloaderOverrideSnapshot(
                CurrentFormatVersion, current.Revision + 1, entries);
            await SaveCoreAsync(saved, cancellationToken).ConfigureAwait(false);
            return saved;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DownloaderOverrideSnapshot> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new DownloaderOverrideSnapshot(
                CurrentFormatVersion,
                0,
                new Dictionary<string, DownloaderOverrideEntry>(StringComparer.OrdinalIgnoreCase));
        }
        await using var stream = new FileStream(
            _path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var snapshot = await JsonSerializer.DeserializeAsync(
            stream, DownloaderOverrideJsonContext.Default.DownloaderOverrideSnapshot, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Downloader private configuration is empty.");
        if (snapshot.FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported downloader private configuration format {snapshot.FormatVersion}.");
        }
        return snapshot with
        {
            Downloaders = new Dictionary<string, DownloaderOverrideEntry>(
                snapshot.Downloaders, StringComparer.OrdinalIgnoreCase),
        };
    }

    private async Task SaveCoreAsync(
        DownloaderOverrideSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".downloaders.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream, snapshot, DownloaderOverrideJsonContext.Default.DownloaderOverrideSnapshot,
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
[JsonSerializable(typeof(DownloaderOverrideSnapshot))]
internal sealed partial class DownloaderOverrideJsonContext : JsonSerializerContext;
