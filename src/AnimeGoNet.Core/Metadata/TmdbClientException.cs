namespace AnimeGoNet.Core.Metadata;

public sealed class TmdbClientException : Exception
{
    public TmdbClientException(
        MetadataFailureKind kind,
        string safeCode,
        bool tmdbAccessConfirmed)
        : base($"TMDB request failed ({safeCode}).")
    {
        if (string.IsNullOrWhiteSpace(safeCode)
            || safeCode.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
        {
            throw new ArgumentException("TMDB error code must be a stable ASCII identifier.", nameof(safeCode));
        }

        Kind = kind;
        SafeCode = safeCode;
        TmdbAccessConfirmed = tmdbAccessConfirmed;
    }

    public MetadataFailureKind Kind { get; }

    public string SafeCode { get; }

    public bool TmdbAccessConfirmed { get; }
}
