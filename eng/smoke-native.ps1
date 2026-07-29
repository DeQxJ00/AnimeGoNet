param(
    [Parameter(Mandatory = $true)]
    [string]$Executable,

    [int]$Port = 0,

    [int]$ExpectedSchemaVersion = 30
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
$smokePassed = $false
$shutdownFailure = $null

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
    $cacheBuckets = Invoke-RestMethod -Uri "$baseUrl/api/bolt?type=bucket" -TimeoutSec 5
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
    if ($status.database_schema_version -ne $ExpectedSchemaVersion) {
        throw "Unexpected schema version: $($status.database_schema_version)"
    }

    if ($cacheBuckets.code -ne 200 -or $cacheBuckets.data.type -ne 'bucket') {
        throw 'NativeAOT SQLite cache compatibility API smoke failed.'
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

    $webSocket = [Net.WebSockets.ClientWebSocket]::new()
    try {
        $webSocketUri = [Uri]("ws://127.0.0.1:$Port/websocket/log")
        [void]$webSocket.ConnectAsync(
            $webSocketUri,
            [Threading.CancellationToken]::None
        ).GetAwaiter().GetResult()

        $pauseBytes = [Text.Encoding]::UTF8.GetBytes('{"action":"pause"}')
        $pauseSegment = [ArraySegment[byte]]::new($pauseBytes)
        [void]$webSocket.SendAsync(
            $pauseSegment,
            [Net.WebSockets.WebSocketMessageType]::Text,
            $true,
            [Threading.CancellationToken]::None
        ).GetAwaiter().GetResult()

        $receiveBytes = [byte[]]::new(4096)
        $receiveSegment = [ArraySegment[byte]]::new($receiveBytes)
        $receive = $webSocket.ReceiveAsync(
            $receiveSegment,
            [Threading.CancellationToken]::None
        ).GetAwaiter().GetResult()
        $control = [Text.Encoding]::UTF8.GetString(
            $receiveBytes,
            0,
            $receive.Count
        )
        if (
            $receive.MessageType -ne [Net.WebSockets.WebSocketMessageType]::Text -or
            -not $receive.EndOfMessage -or
            -not $control.Contains('"type":"control"') -or
            -not $control.Contains('"action":"pause"') -or
            -not $control.Contains('"status":"ok"')
        ) {
            throw 'NativeAOT WebSocket pause control smoke failed.'
        }

        $terminateBytes = [Text.Encoding]::UTF8.GetBytes('{"action":"terminate"}')
        $terminateSegment = [ArraySegment[byte]]::new($terminateBytes)
        [void]$webSocket.SendAsync(
            $terminateSegment,
            [Net.WebSockets.WebSocketMessageType]::Text,
            $true,
            [Threading.CancellationToken]::None
        ).GetAwaiter().GetResult()
    }
    finally {
        $webSocket.Dispose()
    }

    if (-not (Test-Path -LiteralPath (Join-Path $env:data_path 'animegonet.db'))) {
        throw 'SQLite database was not initialized.'
    }

    $logFile = Join-Path $env:data_path 'logs/animego.log'
    if (
        -not (Test-Path -LiteralPath $logFile) -or
        (Get-Item -LiteralPath $logFile).Length -le 0
    ) {
        throw 'Rolling file log was not initialized under data_path.'
    }

    Write-Output "Native smoke passed: $resolvedExecutable"
    $smokePassed = $true
}
finally {
    if ($smokePassed -and -not $IsWindows -and $process.HasExited) {
        $shutdownFailure = 'Published process exited before the SIGTERM shutdown check.'
    }
    if (-not $process.HasExited) {
        if ($smokePassed -and -not $IsWindows) {
            & /bin/kill -TERM $process.Id
            if ($LASTEXITCODE -ne 0) {
                $shutdownFailure = "Could not send SIGTERM to published process $($process.Id)."
            }
            elseif (-not $process.WaitForExit(7000)) {
                $shutdownFailure = 'Published process did not exit within seven seconds after SIGTERM.'
            }
            elseif ($process.ExitCode -ne 0) {
                $shutdownFailure = "Published process returned exit code $($process.ExitCode) after SIGTERM."
            }
            else {
                Write-Output 'Published process SIGTERM shutdown passed.'
            }
        }

        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            [void]$process.WaitForExit(5000)
        }
    }
    if (Test-Path -LiteralPath $smokeRoot) {
        [IO.Directory]::Delete($smokeRoot, $true)
    }
    if ($null -ne $shutdownFailure) {
        throw $shutdownFailure
    }
}
