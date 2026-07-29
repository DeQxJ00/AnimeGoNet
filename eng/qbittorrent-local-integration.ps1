param(
    [string]$SandboxRoot = 'E:\WorkSpaceAI\AnimeGoNet\TestSpace',
    [string]$BaseUrl = $(if ($env:ANIMEGONET_QBIT_BASE_URL) { $env:ANIMEGONET_QBIT_BASE_URL } else { 'http://127.0.0.1:8080/' }),
    [switch]$DispatchFixture
)

$ErrorActionPreference = 'Stop'
$sandbox = [IO.Path]::GetFullPath($SandboxRoot)
$executable = Join-Path $sandbox 'qbittorrent\qbittorrent.exe'
$profile = Join-Path $sandbox 'qbittorrent\profile'
$downloadPath = Join-Path $sandbox 'download_temp'
$savePath = Join-Path $sandbox 'jellyfin_data'
$dataPath = Join-Path $sandbox 'animegonet_data'
$fixturePath = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..\tests\fixtures\animegonet-ci.torrent.b64'))

foreach ($requiredPath in @($executable, $profile, $downloadPath, $savePath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required integration path is missing: $requiredPath"
    }
}
if ($DispatchFixture -and -not (Test-Path -LiteralPath $fixturePath -PathType Leaf)) {
    throw "Safe integration Torrent fixture is missing: $fixturePath"
}

$file = Get-Item -LiteralPath $executable
if ($file.VersionInfo.ProductName -ne 'qBittorrent') {
    throw "The sandbox executable is not identified as qBittorrent: $executable"
}

if ([string]::IsNullOrWhiteSpace($env:ANIMEGONET_QBIT_USERNAME) -or
    [string]::IsNullOrWhiteSpace($env:ANIMEGONET_QBIT_PASSWORD)) {
    throw 'Set ANIMEGONET_QBIT_USERNAME and ANIMEGONET_QBIT_PASSWORD in the current process. Never commit them.'
}

$uri = [Uri]$BaseUrl
if ($uri.Scheme -ne 'http' -and $uri.Scheme -ne 'https') {
    throw "Unsupported qBittorrent WebUI scheme: $($uri.Scheme)"
}
$normalizedBaseUrl = $uri.AbsoluteUri.TrimEnd('/') + '/'

$processes = @(Get-CimInstance Win32_Process -Filter "Name = 'qbittorrent.exe'" |
    Where-Object { $_.ExecutablePath -eq $executable })
if ($processes.Count -eq 0) {
    throw 'The sandbox qBittorrent executable is not running.'
}

$listeners = @(Get-NetTCPConnection -LocalPort $uri.Port -State Listen -ErrorAction Stop)
$owners = @($listeners | Select-Object -ExpandProperty OwningProcess -Unique)
if ($owners.Count -ne 1) {
    throw "Expected one WebUI listener owner on port $($uri.Port), found $($owners.Count)."
}
$processInfo = @($processes | Where-Object { $_.ProcessId -eq $owners[0] })
if ($processInfo.Count -ne 1) {
    throw "WebUI port $($uri.Port) is not owned by the sandbox qBittorrent executable."
}

$profileLock = Join-Path $profile 'qBittorrent\config\lockfile'
if (-not (Test-Path -LiteralPath $profileLock)) {
    throw "The running process does not expose the expected portable profile lock: $profileLock"
}

[IO.Directory]::CreateDirectory($dataPath) | Out-Null

$env:ANIMEGONET_QBIT_INTEGRATION = '1'
$env:ANIMEGONET_QBIT_SANDBOX = $sandbox
$env:ANIMEGONET_QBIT_EXE = $executable
$env:ANIMEGONET_QBIT_BASE_URL = $normalizedBaseUrl
$env:ANIMEGONET_QBIT_PROFILE = $profile
$env:ANIMEGONET_QBIT_DOWNLOAD_PATH = $downloadPath
$env:ANIMEGONET_QBIT_SAVE_PATH = $savePath
$env:ANIMEGONET_QBIT_DATA_PATH = $dataPath
$env:ANIMEGONET_QBIT_DISPATCH_FIXTURE = $(if ($DispatchFixture) { '1' } else { '0' })
$env:ANIMEGONET_QBIT_TORRENT_FIXTURE = $fixturePath

try {
    $testFilter = if ($DispatchFixture) {
        'FullyQualifiedName~QbittorrentSandboxTests|FullyQualifiedName~QbittorrentDispatchFixtureTests'
    }
    else {
        'FullyQualifiedName~QbittorrentSandboxTests'
    }
    dotnet test tests/AnimeGoNet.LocalIntegration.Tests/AnimeGoNet.LocalIntegration.Tests.csproj `
        --configuration Release `
        --no-restore `
        --filter $testFilter `
        --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) {
        throw "Local qBittorrent integration tests failed with exit code $LASTEXITCODE."
    }

    Write-Output "qBittorrent local integration smoke passed: $($file.VersionInfo.ProductVersion)"
    Write-Output 'startup=existing sandbox executable with sibling portable profile'
    Write-Output "download_path=$downloadPath"
    Write-Output "save_path=$savePath"
    Write-Output "data_path=$dataPath"
    Write-Output "dispatch_fixture=$($DispatchFixture.IsPresent)"
}
finally {
    foreach ($name in @(
        'ANIMEGONET_QBIT_INTEGRATION',
        'ANIMEGONET_QBIT_SANDBOX',
        'ANIMEGONET_QBIT_EXE',
        'ANIMEGONET_QBIT_BASE_URL',
        'ANIMEGONET_QBIT_PROFILE',
        'ANIMEGONET_QBIT_DOWNLOAD_PATH',
        'ANIMEGONET_QBIT_SAVE_PATH',
        'ANIMEGONET_QBIT_DATA_PATH',
        'ANIMEGONET_QBIT_DISPATCH_FIXTURE',
        'ANIMEGONET_QBIT_TORRENT_FIXTURE')) {
        [Environment]::SetEnvironmentVariable($name, $null, 'Process')
    }
}
