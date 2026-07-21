using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Metadata;

public sealed class BangumiClientException(
    MetadataFailureKind kind,
    string safeCode) : Exception(safeCode)
{
    public MetadataFailureKind Kind { get; } = kind;

    public string SafeCode { get; } = safeCode;
}
