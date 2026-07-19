namespace AnimeGoNet.Core.Library;

public sealed record CompletionRecord
{
    public required string Id { get; init; }

    public required TmdbEpisodeIdentity Episode { get; init; }

    public required string SourceId { get; init; }

    public string? SourceItemId { get; init; }

    public required DateTimeOffset CompletedAtUtc { get; init; }
}
