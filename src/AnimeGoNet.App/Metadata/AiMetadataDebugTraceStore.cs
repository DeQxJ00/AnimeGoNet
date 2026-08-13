using System.Text.Json;
using AnimeGoNet.App.Serialization;
using AnimeGoNet.Core.Compatibility;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Diagnostics;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Metadata;

public sealed record AiMetadataDebugValidationFile(
    string Name,
    int Season,
    int? Episode,
    string? OtherReason);

public sealed record AiMetadataDebugValidation(
    bool Success,
    int? ExpectedTmdbSeriesId,
    int? ExpectedSeasonNumber,
    string? FailureKind,
    string? FailureCode,
    IReadOnlyList<AiMetadataDebugValidationFile> Files);

public sealed record AiMetadataDebugDocument(
    int FormatVersion,
    AiMetadataDebugChain Chain,
    AiMetadataDebugValidation Validation);

public sealed class AiMetadataDebugTraceStore(DirectoryLayout layout) : IDisposable
{
    private const int CurrentFormatVersion = 1;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Dispose() => _gate.Dispose();

    public bool Exists(string runId) => File.Exists(PathFor(runId));

    public async Task WriteAsync(
        AiMetadataDebugChain chain,
        AiMetadataValidationResult? validation,
        int? expectedSeriesId,
        int? expectedSeasonNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chain.RunId))
        {
            return;
        }

        var debugValidation = new AiMetadataDebugValidation(
            validation?.IsSuccess == true,
            expectedSeriesId,
            expectedSeasonNumber,
            validation?.Failure?.Kind.ToString().ToLowerInvariant(),
            validation?.Failure?.Code ?? chain.FailureCode,
            validation?.Value?.Files.Select(file => new AiMetadataDebugValidationFile(
                file.Input.Name,
                file.Season.SeasonNumber,
                file.Episode?.EpisodeNumber,
                file.OtherReason)).ToArray() ?? []);
        var document = new AiMetadataDebugDocument(
            CurrentFormatVersion,
            chain,
            debugValidation);
        var path = PathFor(chain.RunId);
        var temporary = path + ".tmp";

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(layout.AiDebugPath);
            await using (var stream = new FileStream(
                temporary,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                65_536,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    ApiJsonContext.Default.AiMetadataDebugDocument,
                    cancellationToken).ConfigureAwait(false);
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
            _gate.Release();
        }
    }

    public async Task<string?> ReadAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        var path = PathFor(runId);
        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public bool Delete(string runId)
    {
        var path = PathFor(runId);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    private string PathFor(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var fileName = StableHash.Sha256LowerHex(runId) + ".json";
        return PathBoundary.Combine(layout.AiDebugPath, fileName);
    }
}
