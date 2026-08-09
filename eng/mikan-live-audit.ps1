param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$SandboxRoot = 'E:\WorkSpaceAI\AnimeGoNet\TestSpace',
    [string]$CaseCsv = 'E:\WorkSpaceAI\AnimeGoNet\测试数据.csv',
    [string]$QbittorrentBaseUrl = 'http://192.168.1.17:8080/',
    [string]$MikanBaseUrl = 'http://mikan.local/',
    [string]$TmdbBaseUrl = 'http://api.tmdb.local/',
    [string]$BangumiBaseUrl = 'http://api.bgm.local/',
    [string]$AiBaseUrl = 'http://openai.local/',
    [string]$AiModel = 'gpt-5.4-mini',
    [string]$TmdbMcpUrl = 'http://tmdb.mcp.local/mcp',
    [string]$BangumiMcpUrl = 'http://bgm.mcp.local/mcp',
    [switch]$RealDownload,
    [switch]$SyntheticPayload,
    [ValidateRange(1, 1440)]
    [int]$DownloadTimeoutMinutes = 180,
    [ValidateRange(1, 60)]
    [int]$ZeroProgressSkipMinutes = 5,
    [ValidateRange(2, 30)]
    [int]$StartRow = 2,
    [ValidateRange(1, 29)]
    [int]$MaxCases = 29
)

$ErrorActionPreference = 'Stop'
if ($RealDownload -and $SyntheticPayload) {
    throw '-RealDownload and -SyntheticPayload are mutually exclusive.'
}
$sandbox = [IO.Path]::GetFullPath($SandboxRoot)
$csv = [IO.Path]::GetFullPath($CaseCsv)
$downloadPath = Join-Path $sandbox 'download_temp'
$savePath = Join-Path $sandbox 'jellyfin_data'
$auditOutput = Join-Path $sandbox 'animegonet_data\mikan-live-audit'

foreach ($path in @($sandbox, $downloadPath, $savePath, $csv)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required Mikan audit path is missing: $path"
    }
}

foreach ($secret in @(
    'ANIMEGONET_QBIT_USERNAME',
    'ANIMEGONET_QBIT_PASSWORD',
    'ANIMEGONET_TMDB_API_KEY',
    'ANIMEGONET_AI_API_KEY')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($secret, 'Process'))) {
        throw "Set $secret in the current process. Secrets are never accepted as script parameters or written to the report."
    }
}

[IO.Directory]::CreateDirectory($auditOutput) | Out-Null
$values = @{
    ANIMEGONET_MIKAN_LIVE_AUDIT = '1'
    ANIMEGONET_MIKAN_AUDIT_CSV = $csv
    ANIMEGONET_MIKAN_AUDIT_OUTPUT = $auditOutput
    ANIMEGONET_QBIT_BASE_URL = $QbittorrentBaseUrl
    ANIMEGONET_QBIT_DOWNLOAD_PATH = $downloadPath
    ANIMEGONET_QBIT_SAVE_PATH = $savePath
    ANIMEGONET_MIKAN_BASE_URL = $MikanBaseUrl
    ANIMEGONET_TMDB_BASE_URL = $TmdbBaseUrl
    ANIMEGONET_BANGUMI_BASE_URL = $BangumiBaseUrl
    ANIMEGONET_AI_BASE_URL = $AiBaseUrl
    ANIMEGONET_AI_MODEL = $AiModel
    ANIMEGONET_TMDB_MCP_URL = $TmdbMcpUrl
    ANIMEGONET_BANGUMI_MCP_URL = $BangumiMcpUrl
    ANIMEGONET_MIKAN_REAL_DOWNLOAD = $(if ($RealDownload) { '1' } else { '0' })
    ANIMEGONET_MIKAN_SYNTHETIC_PAYLOAD = $(if ($SyntheticPayload) { '1' } else { '0' })
    ANIMEGONET_MIKAN_DOWNLOAD_TIMEOUT_MINUTES = $DownloadTimeoutMinutes.ToString(
        [Globalization.CultureInfo]::InvariantCulture)
    ANIMEGONET_MIKAN_ZERO_PROGRESS_SKIP_MINUTES = $ZeroProgressSkipMinutes.ToString(
        [Globalization.CultureInfo]::InvariantCulture)
    ANIMEGONET_MIKAN_AUDIT_START_ROW = $StartRow.ToString(
        [Globalization.CultureInfo]::InvariantCulture)
    ANIMEGONET_MIKAN_AUDIT_MAX_CASES = $MaxCases.ToString(
        [Globalization.CultureInfo]::InvariantCulture)
}

$saved = @{}
try {
    foreach ($entry in $values.GetEnumerator()) {
        $saved[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, 'Process')
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }

    dotnet test (Join-Path $RepositoryRoot 'tests\AnimeGoNet.LocalIntegration.Tests\AnimeGoNet.LocalIntegration.Tests.csproj') `
        --configuration Release `
        --filter 'FullyQualifiedName~MikanLiveChainAuditTests' `
        --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) {
        throw "Mikan live audit failed with exit code $LASTEXITCODE. Inspect the newest report below $auditOutput."
    }

    $payloadMode = if ($RealDownload) { 'real_download' } elseif ($SyntheticPayload) { 'synthetic_file' } else { 'metadata_only' }
    Write-Output "Mikan live audit passed. payload_mode=$payloadMode; reports: $auditOutput"
}
finally {
    foreach ($entry in $values.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $saved[$entry.Key], 'Process')
    }
}
