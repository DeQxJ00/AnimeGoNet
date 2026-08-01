namespace AnimeGoNet.Data.Sources;

public sealed record SourceProfileRecord(
    string Id,
    string Adapter,
    string DownloaderId,
    string FileStrategy,
    IReadOnlyList<string> AllowedTorrentHosts,
    string Category,
    IReadOnlyList<string> Tags,
    int SeedingTimeMinutes,
    bool RssFilterEnabled,
    bool RssPriorityEnabled,
    long Revision,
    string? MikanIdentityCookie = null,
    string? DynamicTagTemplate = null)
{
    public override string ToString() =>
        $"SourceProfileRecord {{ Id = {Id}, Adapter = {Adapter}, "
        + $"Revision = {Revision}, CredentialsConfigured = "
        + $"{MikanIdentityCookie is not null} }}";
}

public sealed record SourceProfileAdminRecord(
    string Id,
    string DisplayName,
    string Adapter,
    string DownloaderId,
    string FileStrategy,
    IReadOnlyList<string> AllowedTorrentHosts,
    string Category,
    IReadOnlyList<string> Tags,
    int SeedingTimeMinutes,
    bool RssFilterEnabled,
    bool RssPriorityEnabled,
    bool Enabled,
    long Revision,
    long IngestTaskCount,
    long RssBatchCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? MikanIdentityCookie = null,
    string? DynamicTagTemplate = null)
{
    public override string ToString() =>
        $"SourceProfileAdminRecord {{ Id = {Id}, Adapter = {Adapter}, "
        + $"Revision = {Revision}, CredentialsConfigured = "
        + $"{MikanIdentityCookie is not null} }}";
}

public sealed record SourceProfileDefinition(
    string DisplayName,
    string Adapter,
    string DownloaderId,
    string FileStrategy,
    IReadOnlyList<string> AllowedTorrentHosts,
    string Category,
    IReadOnlyList<string> Tags,
    int SeedingTimeMinutes,
    bool RssFilterEnabled,
    bool RssPriorityEnabled,
    bool Enabled,
    string? MikanIdentityCookie = null,
    string? DynamicTagTemplate = null)
{
    public override string ToString() =>
        $"SourceProfileDefinition {{ Adapter = {Adapter}, "
        + $"CredentialsConfigured = {MikanIdentityCookie is not null} }}";
}

public sealed class SourceProfileRevisionException : InvalidOperationException;

public sealed class SourceProfileConflictException(string message) : InvalidOperationException(message);

public sealed class SourceProfileDuplicateException : InvalidOperationException;
