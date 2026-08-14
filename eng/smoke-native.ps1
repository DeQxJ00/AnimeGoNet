param(
    [Parameter(Mandatory = $true)]
    [string]$Executable,

    [int]$Port = 0,

    [int]$ExpectedSchemaVersion = 51,

    [switch]$LegacyYamlUpgrade
)

$ErrorActionPreference = 'Stop'
$Port = if ($Port -eq 0) { Get-Random -Minimum 20000 -Maximum 60000 } else { $Port }
$resolvedExecutable = (Resolve-Path -LiteralPath $Executable).Path
$smokeRoot = Join-Path ([IO.Path]::GetTempPath()) ("animegonet-smoke-" + [Guid]::NewGuid().ToString('N'))
$env:data_path = Join-Path $smokeRoot 'data'
$env:download_path = Join-Path $smokeRoot 'download/incomplete'
$env:save_path = Join-Path $smokeRoot 'download/anime'
$env:background_workers_enabled = 'false'
$env:mikan_base_url = 'http://127.0.0.1:1/'
$nativeCredential = 'native-aot-private-cookie'
$env:ANIMEGO_MIKAN_COOKIE = $nativeCredential
$legacyYamlHash = $null
if ($LegacyYamlUpgrade) {
    New-Item -ItemType Directory -Path $env:data_path -Force | Out-Null
    $yamlDataPath = $env:data_path.Replace("'", "''")
    $yamlDownloadPath = $env:download_path.Replace("'", "''")
    $yamlSavePath = $env:save_path.Replace("'", "''")
    $legacyYaml = @"
version: 1.6.1
setting:
  client:
    qbittorrent:
      url: http://127.0.0.1:18080/
      username: smoke-user
      password: smoke-password
      download_path: ''
  data_path: '$yamlDataPath'
  download_path: '$yamlDownloadPath'
  save_path: '$yamlSavePath'
  category: NativeSmoke
  tag: '{year}-legacy-template'
advanced:
  request:
    timeout_second: 31
  download:
    rename: move
    seeding_time_minute: 0
  default:
    tmdb_fail_skip: false
    tmdb_fail_use_title_season: true
    tmdb_fail_use_first_season: false
"@
    $legacyYamlBytes = [Text.UTF8Encoding]::new($false).GetBytes($legacyYaml)
    $legacyYamlHash = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($legacyYamlBytes))
    [IO.File]::WriteAllBytes(
        (Join-Path $env:data_path 'animego.yaml'),
        $legacyYamlBytes)
}
$pluginOs = if ($IsWindows) { 'win' } elseif ($IsLinux) { 'linux' } else { 'osx' }
$pluginArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
$pluginRid = "$pluginOs-$pluginArchitecture"
$pluginPackage = Join-Path $env:data_path 'plugins/native-smoke'
New-Item -ItemType Directory -Path $pluginPackage -Force | Out-Null
$pluginEntryName = if ($IsWindows) { 'NativeSmokePlugin.exe' } else { 'NativeSmokePlugin' }
$pluginEntry = Join-Path $pluginPackage $pluginEntryName
[IO.File]::WriteAllBytes($pluginEntry, [byte[]](0))
if (-not $IsWindows) {
    $pluginMode = [IO.UnixFileMode]::UserRead -bor `
        [IO.UnixFileMode]::UserWrite -bor `
        [IO.UnixFileMode]::UserExecute
    [IO.File]::SetUnixFileMode($pluginEntry, $pluginMode)
}
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$pluginManifest = @"
{"id":"com.animegonet.native-smoke","name":"Native smoke","version":"1.0.0","apiVersion":1,"type":"filter","rid":"$pluginRid","entryPoint":"$pluginEntryName","configSchema":"config.schema.json","capabilities":[]}
"@
[IO.File]::WriteAllText(
    (Join-Path $pluginPackage 'plugin.json'),
    $pluginManifest,
    $utf8NoBom)
[IO.File]::WriteAllText(
    (Join-Path $pluginPackage 'config.schema.json'),
    '{"type":"object","additionalProperties":false,"properties":{"token":{"type":"string","writeOnly":true}}}',
    $utf8NoBom)
$pluginConfigurationDirectory = Join-Path $env:data_path 'config'
New-Item -ItemType Directory -Path $pluginConfigurationDirectory -Force | Out-Null
$pluginWriteOnlyValue = 'native-smoke-write-only-value'
$pluginConfiguration = @"
{"format_version":1,"revision":1,"plugins":{"com.animegonet.native-smoke":{"enabled":false,"args":{},"vars":{"token":"$pluginWriteOnlyValue"},"revision":1,"updated_at_utc":"2026-08-01T00:00:00+00:00"}}}
"@
$pluginConfigurationPath = Join-Path $pluginConfigurationDirectory 'external-plugins.private.json'
[IO.File]::WriteAllText(
    $pluginConfigurationPath,
    $pluginConfiguration,
    $utf8NoBom)
if (-not $IsWindows) {
    [IO.File]::SetUnixFileMode(
        $pluginConfigurationPath,
        [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite)
}
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

    $sha256 = Invoke-RestMethod `
        -Uri "$baseUrl/sha256?access_key=NativeAOT%20smoke" `
        -TimeoutSec 5
    $expectedSha256 = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            [Text.Encoding]::UTF8.GetBytes('NativeAOT smoke'))).ToLowerInvariant()
    if ($sha256.code -ne 200 `
        -or $sha256.msg -ne 'Access-Key' `
        -or $sha256.data -ne $expectedSha256) {
        throw 'Published binary legacy /sha256?access_key= smoke failed.'
    }

    $status = Invoke-RestMethod -Uri "$baseUrl/api/v1/status" -TimeoutSec 5
    $sourceResponse =
        Invoke-RestMethod -Uri "$baseUrl/api/v1/sources" -TimeoutSec 5
    $sources = @($sourceResponse.items)
    $cacheBuckets = Invoke-RestMethod -Uri "$baseUrl/api/bolt?type=bucket" -TimeoutSec 5
    $cacheBrowser = Invoke-RestMethod -Uri "$baseUrl/api/v1/cache/buckets?database=bolt" -TimeoutSec 5
    $legacyConfig = Invoke-RestMethod -Uri "$baseUrl/api/config?key=all" -TimeoutSec 5
    $legacyRaw = Invoke-RestMethod -Uri "$baseUrl/api/config?key=raw" -TimeoutSec 5
    $legacyPutPayload = @{
        key = 'raw'
        backup = $false
        config_raw = $legacyRaw.data
    } | ConvertTo-Json -Compress
    $legacyPut = Invoke-RestMethod `
        -Uri "$baseUrl/api/config" `
        -Method Put `
        -ContentType 'application/json' `
        -Body $legacyPutPayload `
        -TimeoutSec 5
    $ingestPayload = '{"source":"mikan","data":[{"torrent":"https://127.0.0.1/passkey/smoke.torrent","info":{"title":"NativeAOT smoke","mikanid":3951,"bgmid":547888}}]}'
    $ingestParameters = @{
        Uri = "$baseUrl/api/v1/ingest"
        Method = 'Post'
        ContentType = 'application/json'
        Body = $ingestPayload
        TimeoutSec = 30
    }
    $ingest = Invoke-RestMethod @ingestParameters
    $openApiResponse = Invoke-WebRequest `
        -UseBasicParsing `
        -Uri "$baseUrl/openapi/v1.json" `
        -TimeoutSec 10
    $openApi = $openApiResponse.Content | ConvertFrom-Json
    $index = Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/" -TimeoutSec 5
    $appScript = Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/app.js" -TimeoutSec 5
    $apiClientScript = Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/api-client.js" -TimeoutSec 5
    if ($status.database_schema_version -ne $ExpectedSchemaVersion) {
        throw "Unexpected schema version: $($status.database_schema_version)"
    }

    if ($cacheBuckets.code -ne 200 -or $cacheBuckets.data.type -ne 'bucket') {
        throw 'NativeAOT SQLite cache compatibility API smoke failed.'
    }

    if ($cacheBrowser.database -ne 'bolt' `
        -or $cacheBrowser.read_only `
        -or $null -eq $cacheBrowser.items) {
        throw 'NativeAOT safe cache browser API smoke failed.'
    }

    if ($legacyConfig.code -ne 200 -or $legacyConfig.data.version -ne '1.7.1' `
        -or $legacyRaw.code -ne 200 -or [string]::IsNullOrWhiteSpace($legacyRaw.data) `
        -or $legacyPut.code -ne 200) {
        throw 'NativeAOT legacy configuration API smoke failed.'
    }

    if (-not $status.native_aot) {
        throw 'Published process does not report NativeAOT.'
    }

    if ($openApiResponse.StatusCode -ne 200 `
        -or $openApi.info.title -ne 'AnimeGoNet API' `
        -or $null -eq $openApi.paths.'/api/v1/status' `
        -or $null -eq $openApi.paths.'/api/download/manager' `
        -or $openApiResponse.Content.Contains($nativeCredential) `
        -or $openApiResponse.Content.Contains($smokeRoot) `
        -or $openApiResponse.Content.Contains($baseUrl)) {
        throw 'NativeAOT deterministic OpenAPI document smoke failed.'
    }

    $externalPackages = @($status.external_plugins.packages)
    $externalErrors = @($status.external_plugins.errors)
    $externalRuntimes = @($status.external_plugins.runtimes)
    if ($externalPackages.Count -ne 1 `
        -or $externalPackages[0].id -ne 'com.animegonet.native-smoke' `
        -or $externalPackages[0].rid -ne $pluginRid `
        -or $externalErrors.Count -ne 0 `
        -or $externalRuntimes.Count -ne 1 `
        -or $externalRuntimes[0].id -ne 'com.animegonet.native-smoke' `
        -or $externalRuntimes[0].state -ne 'stopped' `
        -or $externalRuntimes[0].consecutive_failures -ne 0 `
        -or -not (Test-Path -LiteralPath (Join-Path $env:data_path 'plugin-data') -PathType Container)) {
        throw 'NativeAOT external plugin manifest discovery smoke failed.'
    }

    $pluginConfigurations = Invoke-RestMethod `
        -Uri "$baseUrl/api/v1/plugins" `
        -TimeoutSec 5
    $pluginConfigurationItems = @($pluginConfigurations.items)
    if ($pluginConfigurations.revision -ne 1 `
        -or $pluginConfigurationItems.Count -ne 1 `
        -or $pluginConfigurationItems[0].enabled `
        -or @($pluginConfigurationItems[0].configured_write_only_paths).Count -ne 1 `
        -or $pluginConfigurationItems[0].configured_write_only_paths[0] -ne '/token' `
        -or $null -ne $pluginConfigurationItems[0].vars.token `
        -or ($pluginConfigurations | ConvertTo-Json -Depth 12 -Compress).Contains($pluginWriteOnlyValue)) {
        throw 'NativeAOT external plugin configuration redaction smoke failed.'
    }
    $pluginPutPayload = '{"expected_revision":1,"enabled":true,"args":{"smoke":true},"vars":{},"clear_write_only_paths":[]}'
    $pluginPut = Invoke-RestMethod `
        -Uri "$baseUrl/api/v1/plugins/com.animegonet.native-smoke/configuration" `
        -Method Put `
        -ContentType 'application/json' `
        -Body $pluginPutPayload `
        -TimeoutSec 5
    if ($pluginPut.revision -ne 2 `
        -or -not $pluginPut.item.enabled `
        -or $pluginPut.item.configured_write_only_paths[0] -ne '/token' `
        -or ($pluginPut | ConvertTo-Json -Depth 12 -Compress).Contains($pluginWriteOnlyValue)) {
        throw 'NativeAOT external plugin configuration update smoke failed.'
    }
    $pluginClearPayload = '{"expected_revision":2,"enabled":true,"args":{"smoke":true},"vars":{},"clear_write_only_paths":["/token"]}'
    $pluginClear = Invoke-RestMethod `
        -Uri "$baseUrl/api/v1/plugins/com.animegonet.native-smoke/configuration" `
        -Method Put `
        -ContentType 'application/json' `
        -Body $pluginClearPayload `
        -TimeoutSec 5
    $pluginConfigurationFileText = [IO.File]::ReadAllText($pluginConfigurationPath)
    if ($pluginClear.revision -ne 3 `
        -or @($pluginClear.item.configured_write_only_paths).Count -ne 0 `
        -or $pluginConfigurationFileText.Contains($pluginWriteOnlyValue)) {
        throw 'NativeAOT external plugin write-only clear smoke failed.'
    }
    $updatedPluginStatus = Invoke-RestMethod -Uri "$baseUrl/api/v1/status" -TimeoutSec 5
    if (-not $updatedPluginStatus.external_plugins.packages[0].enabled `
        -or $updatedPluginStatus.external_plugins.packages[0].entry_revision -ne 3) {
        throw 'NativeAOT external plugin enabled status smoke failed.'
    }

    $externalPluginReset = Invoke-RestMethod `
        -Uri "$baseUrl/api/v1/plugins/com.animegonet.native-smoke/reset" `
        -Method Post `
        -TimeoutSec 5
    if ($externalPluginReset.id -ne 'com.animegonet.native-smoke' `
        -or $externalPluginReset.state -ne 'stopped' `
        -or $externalPluginReset.consecutive_failures -ne 0) {
        throw 'NativeAOT external plugin reset API smoke failed.'
    }

    if (-not $status.capabilities.qbittorrent) {
        throw 'Published process does not report the qBittorrent capability.'
    }

    $mikanSource = @($sources | Where-Object { $_.id -eq 'mikan' })
    $sourcesJson = $sources | ConvertTo-Json -Depth 8 -Compress
    if (
        $mikanSource.Count -ne 1 -or
        -not $mikanSource[0].mikan_identity_cookie_configured -or
        $sourcesJson.Contains($nativeCredential)
    ) {
        throw 'NativeAOT source credential redaction smoke failed.'
    }

    if (($ingest.accepted_count -ne 0) -or ($ingest.rejected_count -ne 1) -or (-not $ingest.items[0].errors[0].Contains('NetworkFailure'))) {
        $safeIngestError = if ($null -eq $ingest.items[0].errors[0]) {
            '<missing>'
        } else {
            [string]$ingest.items[0].errors[0]
        }
        throw "NativeAOT secure ingest rejection smoke failed: accepted=$($ingest.accepted_count), rejected=$($ingest.rejected_count), error=$safeIngestError"
    }

    if ($index.StatusCode -ne 200 `
        -or $appScript.StatusCode -ne 200 `
        -or $apiClientScript.StatusCode -ne 200 `
        -or -not $index.Content.Contains('<title>AnimeGoNet</title>') `
        -or -not $index.Content.Contains('external-plugin-list') `
        -or -not $index.Content.Contains('cache-browser') `
        -or -not $appScript.Content.Contains('/api/v1/plugins/') `
        -or -not $appScript.Content.Contains('/api/v1/cache/buckets') `
        -or -not $appScript.Content.Contains('from "./api-client.js"') `
        -or -not $apiClientScript.Content.Contains('invalid_api_path')) {
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

    $deploymentYaml = Join-Path $env:data_path 'animego.yaml'
    if (-not (Test-Path -LiteralPath $deploymentYaml -PathType Leaf)) {
        throw 'NativeAOT first start did not create deployment YAML.'
    }
    $deploymentYamlText = Get-Content -Raw -LiteralPath $deploymentYaml
    if (
        -not $deploymentYamlText.Contains('version: 1.7.1') -or
        -not $deploymentYamlText.Contains($env:data_path) -or
        -not $deploymentYamlText.Contains($env:download_path) -or
        -not $deploymentYamlText.Contains($env:save_path) -or
        -not $deploymentYamlText.Contains('use_metadata_match: false')
    ) {
        throw 'NativeAOT deployment YAML does not contain the effective safe defaults.'
    }
    if ($LegacyYamlUpgrade) {
        if ($deploymentYamlText.Contains("`nsetting:")) {
            throw 'NativeAOT legacy deployment YAML was not rewritten to the canonical layout.'
        }
        if (-not $deploymentYamlText.Contains("dynamic_tag_template: '{year}-legacy-template'")) {
            throw 'NativeAOT legacy dynamic tag template was not migrated to the dedicated source field.'
        }
        if (-not $deploymentYamlText.Contains('tags: []')) {
            throw 'NativeAOT legacy dynamic tag template was incorrectly migrated as a static tag.'
        }
        $backups = @(
            Get-ChildItem -LiteralPath $env:data_path `
                -Filter 'animego-1.6.1-*.yaml' `
                -File)
        if ($backups.Count -ne 1) {
            throw "NativeAOT legacy YAML expected one backup, found $($backups.Count)."
        }
        $backupHash = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData(
                [IO.File]::ReadAllBytes($backups[0].FullName)))
        if ($backupHash -ne $legacyYamlHash) {
            throw 'NativeAOT legacy YAML backup does not exactly match the original bytes.'
        }
    }

    $logFile = Join-Path $env:data_path 'logs/animego.log'
    if (
        -not (Test-Path -LiteralPath $logFile) -or
        (Get-Item -LiteralPath $logFile).Length -le 0
    ) {
        throw 'Rolling file log was not initialized under data_path.'
    }

    $mode = if ($LegacyYamlUpgrade) { 'legacy-yaml-upgrade' } else { 'first-start' }
    Write-Output "Native smoke passed ($mode): $resolvedExecutable"
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
    Remove-Item Env:ANIMEGO_MIKAN_COOKIE -ErrorAction SilentlyContinue
    if ($null -ne $shutdownFailure) {
        throw $shutdownFailure
    }
}
