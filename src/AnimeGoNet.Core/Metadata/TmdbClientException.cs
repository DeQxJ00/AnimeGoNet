using AnimeGoNet.Core.Diagnostics;

namespace AnimeGoNet.Core.Metadata;

public sealed class TmdbClientException : Exception
{
    public TmdbClientException(
        MetadataFailureKind kind,
        string safeCode,
        bool tmdbAccessConfirmed)
        : base($"TMDB request failed ({safeCode}).")
    {
        Kind = kind;
        SafeCode = StableErrorCode.Require(safeCode, nameof(safeCode));
        TmdbAccessConfirmed = tmdbAccessConfirmed;
    }

    public MetadataFailureKind Kind { get; }

    public string SafeCode { get; }

    public bool TmdbAccessConfirmed { get; }
}
