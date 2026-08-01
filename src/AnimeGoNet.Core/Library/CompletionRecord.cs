namespace AnimeGoNet.Core.Library;

public sealed record CompletionRecord
{
    public required string Id { get; init; }

    public required TmdbEpisodeIdentity Episode { get; init; }

    public required string SourceId { get; init; }

    public string? SourceItemId { get; init; }

    public required DateTimeOffset CompletedAtUtc { get; init; }
}

public sealed record CompletionAlias
{
    public required string Id { get; init; }

    public required string CompletionId { get; init; }

    public required string SourceId { get; init; }

    public string? SourceWorkId { get; init; }

    public string? SourceEpisode { get; init; }

    public string? InfoHash { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record CompletionAliasMatch(
    CompletionAlias Alias,
    TmdbEpisodeIdentity Episode,
    DateTimeOffset CompletedAtUtc);
