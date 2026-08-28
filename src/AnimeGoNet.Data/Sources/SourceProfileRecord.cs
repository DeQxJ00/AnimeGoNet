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
    string? DynamicTagTemplate = null,
    string? RssFeedUrl = null,
    bool RssScheduleEnabled = false,
    string RssScheduleCron = AnimeGoNet.Core.Sources.SourceRssSchedulePolicy.DefaultCron,
    bool DuplicateNotificationEnabled = true,
    string MediaType = AnimeGoNet.Core.Media.MediaTypes.Tv,
    bool PreferAniDbTmdbMapping = false,
    string AniDbTmdbMappingUrlTemplate = "https://raw.githubusercontent.com/DeQxJ00/Anime-Lists-Json/refs/heads/main/api/anidb/{anidbid}.json",
    string LinkType = AnimeGoNet.Core.Configuration.SourceDownloadPolicy.HardLinkType)
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
    string? DynamicTagTemplate = null,
    string? RssFeedUrl = null,
    bool RssScheduleEnabled = false,
    string RssScheduleCron = AnimeGoNet.Core.Sources.SourceRssSchedulePolicy.DefaultCron,
    string RssLastRunState = "never",
    DateTimeOffset? RssLastStartedAtUtc = null,
    DateTimeOffset? RssLastCompletedAtUtc = null,
    string? RssLastFailureCode = null,
    string? RssLastBatchId = null,
    bool DuplicateNotificationEnabled = true,
    string MediaType = AnimeGoNet.Core.Media.MediaTypes.Tv,
    bool PreferAniDbTmdbMapping = false,
    string AniDbTmdbMappingUrlTemplate = "https://raw.githubusercontent.com/DeQxJ00/Anime-Lists-Json/refs/heads/main/api/anidb/{anidbid}.json",
    string LinkType = AnimeGoNet.Core.Configuration.SourceDownloadPolicy.HardLinkType)
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
    string? DynamicTagTemplate = null,
    string? RssFeedUrl = null,
    bool RssScheduleEnabled = false,
    string RssScheduleCron = AnimeGoNet.Core.Sources.SourceRssSchedulePolicy.DefaultCron,
    bool DuplicateNotificationEnabled = true,
    string MediaType = AnimeGoNet.Core.Media.MediaTypes.Tv,
    bool PreferAniDbTmdbMapping = false,
    string AniDbTmdbMappingUrlTemplate = "https://raw.githubusercontent.com/DeQxJ00/Anime-Lists-Json/refs/heads/main/api/anidb/{anidbid}.json",
    string LinkType = AnimeGoNet.Core.Configuration.SourceDownloadPolicy.HardLinkType)
{
    public override string ToString() =>
        $"SourceProfileDefinition {{ Adapter = {Adapter}, "
        + $"CredentialsConfigured = {MikanIdentityCookie is not null} }}";
}

public sealed record SourceProfileDeploymentOverride(
    string Id,
    string Adapter,
    bool OverrideCategory,
    string Category,
    bool OverrideDynamicTagTemplate,
    string? DynamicTagTemplate,
    bool OverrideMikanIdentityCookie,
    string? MikanIdentityCookie)
{
    public override string ToString() =>
        $"SourceProfileDeploymentOverride {{ Id = {Id}, "
        + $"OverrideCategory = {OverrideCategory}, "
        + $"OverrideDynamicTagTemplate = {OverrideDynamicTagTemplate}, "
        + $"OverrideCredentials = {OverrideMikanIdentityCookie} }}";
}

public sealed class SourceProfileRevisionException : InvalidOperationException;

public sealed class SourceProfileConflictException(string message) : InvalidOperationException(message);

public sealed class SourceProfileDuplicateException : InvalidOperationException;
