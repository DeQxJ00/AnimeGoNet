param(
    [Parameter(Mandatory = $true)]
    [string]$Executable,

    [int]$Port = 53271
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = (Resolve-Path -LiteralPath $Executable).Path
$smokeRoot = Join-Path $PWD ("artifacts/smoke-" + [Guid]::NewGuid().ToString('N'))
$env:data_path = Join-Path $smokeRoot 'data'
$env:download_path = Join-Path $smokeRoot 'download/incomplete'
$env:save_path = Join-Path $smokeRoot 'download/anime'
$baseUrl = "http://127.0.0.1:$Port"
$process = Start-Process -FilePath $resolvedExecutable -ArgumentList @('--urls', $baseUrl) -PassThru -WindowStyle Hidden

try {
    $ping = $null
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        try {
            $ping = Invoke-RestMethod -Uri "$baseUrl/ping" -TimeoutSec 2
            break
        }
        catch {
            Start-Sleep -Milliseconds 250
        }
    }

    if ($null -eq $ping -or $ping.code -ne 200 -or $ping.msg -ne 'pong') {
        throw 'Published binary /ping smoke failed.'
    }

    $status = Invoke-RestMethod -Uri "$baseUrl/api/v1/status" -TimeoutSec 5
    $index = Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/" -TimeoutSec 5
    if ($status.database_schema_version -ne 1) {
        throw "Unexpected schema version: $($status.database_schema_version)"
    }

    if (-not $status.native_aot) {
        throw 'Published process does not report NativeAOT.'
    }

    if ($index.StatusCode -ne 200 -or -not $index.Content.Contains('<title>AnimeGoNet</title>')) {
        throw 'Static WebUI smoke failed.'
    }

    if (-not (Test-Path -LiteralPath (Join-Path $env:data_path 'animegonet.db'))) {
        throw 'SQLite database was not initialized.'
    }

    Write-Output "Native smoke passed: $resolvedExecutable"
}
finally {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
}
