namespace AnimeGoNet.Core.Feeds;

public static class MikanBangumiDiscoveryStates
{
    public const string NotAttempted = "not_attempted";
    public const string Resolved = "resolved";
    public const string NotFound = "not_found";
    public const string Failed = "failed";
    public const string NotApplicable = "not_applicable";
}

public sealed record MikanBangumiDiscovery(
    int? BangumiSubjectId,
    string State,
    string? FailureCode)
{
    public static MikanBangumiDiscovery NotAttempted { get; } =
        new(null, MikanBangumiDiscoveryStates.NotAttempted, null);

    public bool IsResolved =>
        State == MikanBangumiDiscoveryStates.Resolved && BangumiSubjectId is > 0;
}
