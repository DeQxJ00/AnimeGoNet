namespace AnimeGoNet.Core.DataUpdate;

public enum DataAssetKind
{
    Subjects,
    Episodes,
}

public sealed record DataManifest(
    int SchemaVersion,
    string DataVersion,
    DateTimeOffset GeneratedAtUtc,
    string MinimumClientVersion,
    DataManifestUpstream Upstream,
    IReadOnlyList<DataManifestAsset> Assets,
    long SubjectCount,
    long EpisodeCount);

public sealed record DataManifestUpstream(
    string Repository,
    string Release,
    string Asset,
    string Sha256);

public sealed record DataManifestAsset(
    DataAssetKind Kind,
    string FileName,
    Uri Url,
    long SizeBytes,
    string Sha256,
    long RecordCount,
    int SubjectIdMin,
    int SubjectIdMax);

public sealed class DataManifestException(string code, string message)
    : FormatException(message)
{
    public string Code { get; } = code;
}
