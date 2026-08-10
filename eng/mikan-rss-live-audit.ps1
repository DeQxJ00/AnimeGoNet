param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$SandboxRoot = 'E:\WorkSpaceAI\AnimeGoNet\TestSpace',
    [string]$TmdbBaseUrl = 'http://api.tmdb.local/',
    [string]$BangumiBaseUrl = 'http://api.bgm.local/',
    [switch]$SkipMetadata
)

$ErrorActionPreference = 'Stop'
$rssUrl = [Environment]::GetEnvironmentVariable('ANIMEGONET_MIKAN_RSS_URL', 'Process')
if ([string]::IsNullOrWhiteSpace($rssUrl)) {
    throw 'Set ANIMEGONET_MIKAN_RSS_URL in the current process. The private RSS URL is never accepted as a parameter or written to reports.'
}
if (-not $SkipMetadata -and [string]::IsNullOrWhiteSpace(
    [Environment]::GetEnvironmentVariable('ANIMEGONET_TMDB_API_KEY', 'Process'))) {
    throw 'Set ANIMEGONET_TMDB_API_KEY in the current process. The key is never accepted as a parameter or written to reports.'
}

$outputRoot = Join-Path ([IO.Path]::GetFullPath($SandboxRoot)) 'animegonet_data\mikan-rss-live-audit'
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$savedSwitch = [Environment]::GetEnvironmentVariable('ANIMEGONET_MIKAN_RSS_INTEGRATION', 'Process')
$savedOutput = [Environment]::GetEnvironmentVariable('ANIMEGONET_MIKAN_RSS_AUDIT_OUTPUT', 'Process')
$savedMetadata = [Environment]::GetEnvironmentVariable('ANIMEGONET_MIKAN_RSS_METADATA', 'Process')
$savedTmdbBase = [Environment]::GetEnvironmentVariable('ANIMEGONET_TMDB_BASE_URL', 'Process')
$savedBangumiBase = [Environment]::GetEnvironmentVariable('ANIMEGONET_BANGUMI_BASE_URL', 'Process')
try {
    [Environment]::SetEnvironmentVariable('ANIMEGONET_MIKAN_RSS_INTEGRATION', '1', 'Process')
    [Environment]::SetEnvironmentVariable('ANIMEGONET_MIKAN_RSS_AUDIT_OUTPUT', $outputRoot, 'Process')
    [Environment]::SetEnvironmentVariable('ANIMEGONET_MIKAN_RSS_METADATA', $(if ($SkipMetadata) { '0' } else { '1' }), 'Process')
    [Environment]::SetEnvironmentVariable('ANIMEGONET_TMDB_BASE_URL', $TmdbBaseUrl, 'Process')
    [Environment]::SetEnvironmentVariable('ANIMEGONET_BANGUMI_BASE_URL', $BangumiBaseUrl, 'Process')
    dotnet test (Join-Path $RepositoryRoot 'tests\AnimeGoNet.LocalIntegration.Tests\AnimeGoNet.LocalIntegration.Tests.csproj') `
        --configuration Release `
        --filter 'FullyQualifiedName~MikanRssLiveAuditTests' `
        --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) {
        throw "Mikan RSS live audit failed with exit code $LASTEXITCODE."
    }
    Write-Output "Mikan RSS live audit passed. Redacted reports: $outputRoot"
}
finally {
    [Environment]::SetEnvironmentVariable('ANIMEGONET_MIKAN_RSS_INTEGRATION', $savedSwitch, 'Process')
    [Environment]::SetEnvironmentVariable('ANIMEGONET_MIKAN_RSS_AUDIT_OUTPUT', $savedOutput, 'Process')
    [Environment]::SetEnvironmentVariable('ANIMEGONET_MIKAN_RSS_METADATA', $savedMetadata, 'Process')
    [Environment]::SetEnvironmentVariable('ANIMEGONET_TMDB_BASE_URL', $savedTmdbBase, 'Process')
    [Environment]::SetEnvironmentVariable('ANIMEGONET_BANGUMI_BASE_URL', $savedBangumiBase, 'Process')
}
