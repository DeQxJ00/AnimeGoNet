param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$SandboxRoot = 'E:\WorkSpaceAI\AnimeGoNet\TestSpace'
)

$ErrorActionPreference = 'Stop'
$rssUrl = [Environment]::GetEnvironmentVariable('ANIMEGONET_MIKAN_RSS_URL', 'Process')
if ([string]::IsNullOrWhiteSpace($rssUrl)) {
    throw 'Set ANIMEGONET_MIKAN_RSS_URL in the current process. The private RSS URL is never accepted as a parameter or written to reports.'
}

$outputRoot = Join-Path ([IO.Path]::GetFullPath($SandboxRoot)) 'animegonet_data\mikan-rss-live-audit'
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$savedSwitch = [Environment]::GetEnvironmentVariable('ANIMEGONET_MIKAN_RSS_INTEGRATION', 'Process')
$savedOutput = [Environment]::GetEnvironmentVariable('ANIMEGONET_MIKAN_RSS_AUDIT_OUTPUT', 'Process')
try {
    [Environment]::SetEnvironmentVariable('ANIMEGONET_MIKAN_RSS_INTEGRATION', '1', 'Process')
    [Environment]::SetEnvironmentVariable('ANIMEGONET_MIKAN_RSS_AUDIT_OUTPUT', $outputRoot, 'Process')
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
}
