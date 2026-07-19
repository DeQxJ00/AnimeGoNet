param(
    [Parameter(Mandatory = $true)]
    [string]$Executable,

    [int]$Port = 0
)

$ErrorActionPreference = 'Stop'
$Port = if ($Port -eq 0) { Get-Random -Minimum 20000 -Maximum 60000 } else { $Port }
$resolvedExecutable = (Resolve-Path -LiteralPath $Executable).Path
$smokeRoot = Join-Path ([IO.Path]::GetTempPath()) ("animegonet-smoke-" + [Guid]::NewGuid().ToString('N'))
$env:data_path = Join-Path $smokeRoot 'data'
$env:download_path = Join-Path $smokeRoot 'download/incomplete'
$env:save_path = Join-Path $smokeRoot 'download/anime'
$env:background_workers_enabled = 'false'
$baseUrl = "http://127.0.0.1:$Port"
$startParameters = @{
    FilePath = $resolvedExecutable
    ArgumentList = @('--urls', $baseUrl)
    PassThru = $true
}
if ($IsWindows) {
    $startParameters.WindowStyle = 'Hidden'
}
$process = Start-Process @startParameters

try {
    $ping = $null
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if ($process.HasExited) {
            throw "Published process exited before becoming ready (exit code $($process.ExitCode))."
        }

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
    $ingestPayload = '{"source":"mikan","data":[{"torrent":"https://tracker.invalid/passkey/smoke.torrent","info":{"title":"NativeAOT smoke","mikanid":3951,"bgmid":547888}}]}'
    $ingestParameters = @{
        Uri = "$baseUrl/api/v1/ingest"
        Method = 'Post'
        ContentType = 'application/json'
        Body = $ingestPayload
        TimeoutSec = 5
    }
    $ingest = Invoke-RestMethod @ingestParameters
    $index = Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/" -TimeoutSec 5
    if ($status.database_schema_version -ne 6) {
        throw "Unexpected schema version: $($status.database_schema_version)"
    }

    if (-not $status.native_aot) {
        throw 'Published process does not report NativeAOT.'
    }

    if (-not $status.capabilities.qbittorrent) {
        throw 'Published process does not report the qBittorrent capability.'
    }

    if (($ingest.accepted_count -ne 0) -or ($ingest.rejected_count -ne 1) -or (-not $ingest.items[0].errors[0].Contains('HostNotAllowed'))) {
        throw 'NativeAOT secure ingest rejection smoke failed.'
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
    if (Test-Path -LiteralPath $smokeRoot) {
        [IO.Directory]::Delete($smokeRoot, $true)
    }
}
