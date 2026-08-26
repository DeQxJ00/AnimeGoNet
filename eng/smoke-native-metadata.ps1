param(
    [Parameter(Mandatory = $true)]
    [string]$Executable,

    [string]$FixtureProject = '',

    [int]$ExpectedSchemaVersion = 66
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = (Resolve-Path -LiteralPath $Executable).Path
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($FixtureProject)) {
    $FixtureProject = Join-Path $repositoryRoot `
        'tests/AnimeGoNet.NativeMetadataSmokeFixture/AnimeGoNet.NativeMetadataSmokeFixture.csproj'
}
$resolvedFixtureProject = (Resolve-Path -LiteralPath $FixtureProject).Path
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$smokeRoot = Join-Path $temporaryRoot `
    ('animegonet-native-metadata-smoke-' + [Guid]::NewGuid().ToString('N'))
$smokeRoot = [IO.Path]::GetFullPath($smokeRoot)
if (
    -not $smokeRoot.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
    -not [IO.Path]::GetFileName($smokeRoot).StartsWith(
        'animegonet-native-metadata-smoke-',
        [StringComparison]::Ordinal)
) {
    throw 'Native metadata smoke temporary path escaped the system temporary directory.'
}

$dataPath = Join-Path $smokeRoot 'data'
$downloadPath = Join-Path $smokeRoot 'download/incomplete'
$savePath = Join-Path $smokeRoot 'download/anime'
$fixtureOutput = Join-Path $smokeRoot 'fixture'
$databasePath = Join-Path $dataPath 'animegonet.db'
$nativeFirstProcess = $null
$nativeWorkerProcess = $null
$fixtureProcess = $null

function Get-FreeLoopbackPort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Start-OwnedProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList,
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $parameters = @{
        FilePath = $FilePath
        ArgumentList = $ArgumentList
        PassThru = $true
        RedirectStandardOutput = Join-Path $smokeRoot "$Name.stdout.log"
        RedirectStandardError = Join-Path $smokeRoot "$Name.stderr.log"
    }
    if ($IsWindows) {
        $parameters.WindowStyle = 'Hidden'
    }
    return Start-Process @parameters
}

function Stop-OwnedProcess {
    param([Diagnostics.Process]$Process)

    if ($null -eq $Process) {
        return
    }
    $Process.Refresh()
    if (-not $Process.HasExited) {
        Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
        [void]$Process.WaitForExit(5000)
    }
    $Process.Dispose()
}

function Wait-Ready {
    param(
        [Parameter(Mandatory = $true)]
        [Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)]
        [string]$Uri,
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    for ($attempt = 0; $attempt -lt 80; $attempt++) {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "$Name exited before becoming ready (exit code $($Process.ExitCode))."
        }
        try {
            return Invoke-RestMethod -Uri $Uri -TimeoutSec 2
        }
        catch {
            Start-Sleep -Milliseconds 250
        }
    }
    throw "$Name did not become ready."
}

try {
    New-Item -ItemType Directory -Path $dataPath -Force | Out-Null
    New-Item -ItemType Directory -Path $downloadPath -Force | Out-Null
    New-Item -ItemType Directory -Path $savePath -Force | Out-Null

    $env:data_path = $dataPath
    $env:download_path = $downloadPath
    $env:save_path = $savePath
    $env:background_workers_enabled = 'false'
    $env:downloaders__bt__enabled = 'false'
    $firstPort = Get-FreeLoopbackPort
    $firstBaseUrl = "http://127.0.0.1:$firstPort"
    $nativeFirstProcess = Start-OwnedProcess `
        -FilePath $resolvedExecutable `
        -ArgumentList @('--urls', $firstBaseUrl) `
        -Name 'native-first-start'
    $ping = Wait-Ready `
        -Process $nativeFirstProcess `
        -Uri "$firstBaseUrl/ping" `
        -Name 'Native first-start process'
    if ($ping.code -ne 200 -or $ping.msg -ne 'pong') {
        throw 'Native metadata smoke first-start /ping failed.'
    }
    Stop-OwnedProcess $nativeFirstProcess
    $nativeFirstProcess = $null
    if (-not (Test-Path -LiteralPath $databasePath -PathType Leaf)) {
        throw 'Native metadata smoke database was not initialized by the published binary.'
    }

    & dotnet build $resolvedFixtureProject `
        --configuration Release `
        --nologo `
        --output $fixtureOutput
    if ($LASTEXITCODE -ne 0) {
        throw 'Native metadata smoke fixture build failed.'
    }
    $fixtureAssembly = Join-Path $fixtureOutput `
        'AnimeGoNet.NativeMetadataSmokeFixture.dll'
    if (-not (Test-Path -LiteralPath $fixtureAssembly -PathType Leaf)) {
        throw 'Native metadata smoke fixture assembly was not produced.'
    }

    $seedOutput = @(& dotnet $fixtureAssembly seed `
        --database $databasePath `
        --download-path $downloadPath `
        --save-path $savePath)
    if ($LASTEXITCODE -ne 0) {
        throw 'Native metadata smoke task seed failed.'
    }
    $seed = ($seedOutput -join [Environment]::NewLine) | ConvertFrom-Json
    if (
        [string]::IsNullOrWhiteSpace($seed.task_id) -or
        $seed.file_name -ne 'Native.AI.S02E07.mkv'
    ) {
        throw 'Native metadata smoke task seed returned an invalid identity.'
    }

    $fixturePort = Get-FreeLoopbackPort
    $fixtureBaseUrl = "http://127.0.0.1:$fixturePort"
    $fixtureProcess = Start-OwnedProcess `
        -FilePath 'dotnet' `
        -ArgumentList @($fixtureAssembly, 'serve', '--urls', $fixtureBaseUrl) `
        -Name 'metadata-fixture'
    [void](Wait-Ready `
        -Process $fixtureProcess `
        -Uri "$fixtureBaseUrl/ready" `
        -Name 'Native metadata loopback fixture')

    $env:background_workers_enabled = 'true'
    $env:tmdb_base_url = "$fixtureBaseUrl/tmdb/"
    $env:tmdb_api_key = 'native-smoke-tmdb-key'
    $env:tmdb_retry_count = '0'
    $env:tmdb_retry_wait_second = '0'
    $env:bangumi_base_url = "$fixtureBaseUrl/bangumi/"
    $env:bangumi_retry_count = '0'
    $env:bangumi_retry_wait_second = '0'
    $env:ai_base_url = "$fixtureBaseUrl/ai/"
    $env:ai_api_key = 'native-smoke-ai-key'
    $env:ai_model = 'native-smoke-model'
    $env:ai_use_metadata_match = 'true'
    $env:ai_timeout_second = '30'
    $env:ai_retry_count = '0'
    $env:ai_use_bangumi_pubdate_first = 'false'
    $env:ai_tmdb_mcp_url = "$fixtureBaseUrl/mcp"
    $env:ai_bangumi_mcp_url = "$fixtureBaseUrl/mcp"
    $workerPort = Get-FreeLoopbackPort
    $workerBaseUrl = "http://127.0.0.1:$workerPort"
    $nativeWorkerProcess = Start-OwnedProcess `
        -FilePath $resolvedExecutable `
        -ArgumentList @('--urls', $workerBaseUrl) `
        -Name 'native-metadata-workers'
    [void](Wait-Ready `
        -Process $nativeWorkerProcess `
        -Uri "$workerBaseUrl/ping" `
        -Name 'Native metadata worker process')

    $detail = $null
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        $nativeWorkerProcess.Refresh()
        if ($nativeWorkerProcess.HasExited) {
            throw "Native metadata worker process exited early (exit code $($nativeWorkerProcess.ExitCode))."
        }
        try {
            $detail = Invoke-RestMethod `
                -Uri "$workerBaseUrl/api/v1/metadata/tasks/$($seed.task_id)" `
                -TimeoutSec 2
            if ($detail.summary.status -in @('metadata_resolved', 'metadata_failed')) {
                break
            }
        }
        catch {
            # The public task projection may briefly race the worker transaction.
        }
        Start-Sleep -Milliseconds 500
    }

    if ($null -eq $detail -or $detail.summary.status -ne 'metadata_resolved') {
        $lastStatus = if ($null -eq $detail) { '<missing>' } else { $detail.summary.status }
        $detailJson = if ($null -eq $detail) {
            '<missing>'
        }
        else {
            $detail | ConvertTo-Json -Depth 8 -Compress
        }
        $fixtureStateJson = try {
            (Invoke-RestMethod -Uri "$fixtureBaseUrl/__state" -TimeoutSec 2) |
                ConvertTo-Json -Depth 4 -Compress
        }
        catch {
            '<unavailable>'
        }
        throw "Published AI metadata pipeline did not resolve the task (last status: $lastStatus; detail: $detailJson; fixture: $fixtureStateJson)."
    }
    $files = @($detail.files)
    if (
        $detail.summary.tmdb_series_id -ne 72517 -or
        $detail.summary.tmdb_season_number -ne 2 -or
        $detail.summary.season_strategy -ne 'ai_metadata' -or
        $detail.summary.episode_strategy -ne 'ai_metadata' -or
        $detail.ai.status -ne 'matched' -or
        $detail.ai.confidence_basis -ne 'tmdb_verified' -or
        $files.Count -ne 1 -or
        $files[0].source_name -ne 'Native.AI.S02E07.mkv' -or
        $files[0].disposition -ne 'episode' -or
        $files[0].tmdb_series_id -ne 72517 -or
        $files[0].tmdb_season_number -ne 2 -or
        $files[0].tmdb_episode_number -ne 7
    ) {
        throw 'Published AI metadata pipeline returned an unexpected authoritative projection.'
    }

    $attempts = Invoke-RestMethod `
        -Uri "$workerBaseUrl/api/v1/metadata/tasks/$($seed.task_id)/attempts" `
        -TimeoutSec 5
    $attemptItems = @($attempts.items)
    if (
        -not ($attemptItems | Where-Object {
            $_.stage -eq 'season' -and
            $_.strategy -eq 'ai_metadata' -and
            $_.result -eq 'matched'
        }) -or
        -not ($attemptItems | Where-Object {
            $_.stage -eq 'episode' -and
            $_.strategy -eq 'ai_metadata' -and
            $_.result -eq 'matched'
        })
    ) {
        throw 'Published AI metadata pipeline did not persist both AI season and Episode evidence.'
    }

    $status = Invoke-RestMethod -Uri "$workerBaseUrl/api/v1/status" -TimeoutSec 5
    $configuration = Invoke-RestMethod -Uri "$workerBaseUrl/api/v1/config" -TimeoutSec 5
    if (
        -not $status.native_aot -or
        $status.database_schema_version -ne $ExpectedSchemaVersion -or
        -not $configuration.deployment.background_workers_enabled -or
        -not $configuration.editable.ai_use_metadata_match
    ) {
        throw 'Native metadata smoke did not execute in the expected published runtime mode.'
    }

    $fixtureState = Invoke-RestMethod -Uri "$fixtureBaseUrl/__state" -TimeoutSec 5
    if (
        $fixtureState.ai_calls -ne 2 -or
        $fixtureState.ai_authorization_failures -ne 0 -or
        $fixtureState.unsafe_absolute_paths -ne 0 -or
        $fixtureState.mcp_initialize_calls -ne 1 -or
        $fixtureState.mcp_notification_calls -ne 1 -or
        $fixtureState.mcp_tools_list_calls -ne 1 -or
        $fixtureState.mcp_tool_calls -ne 1 -or
        $fixtureState.tmdb_discover_calls -lt 1 -or
        $fixtureState.tmdb_series_calls -lt 1 -or
        $fixtureState.tmdb_season_calls -lt 1 -or
        $fixtureState.tmdb_episode_calls -lt 1 -or
        $fixtureState.tmdb_credential_failures -ne 0
    ) {
        throw 'Published AI metadata pipeline did not complete the expected fake AI/MCP/TMDB request graph.'
    }

    Write-Output `
        "Native AI metadata smoke passed: $resolvedExecutable (task $($seed.task_id))."
}
catch {
    foreach ($log in Get-ChildItem -LiteralPath $smokeRoot -Filter '*.log' -File -ErrorAction SilentlyContinue) {
        $tail = @(Get-Content -LiteralPath $log.FullName -Tail 40 -ErrorAction SilentlyContinue)
        if ($tail.Count -gt 0) {
            Write-Warning ("$($log.Name):`n" + ($tail -join [Environment]::NewLine))
        }
    }
    throw
}
finally {
    Stop-OwnedProcess $nativeWorkerProcess
    Stop-OwnedProcess $fixtureProcess
    Stop-OwnedProcess $nativeFirstProcess
    if (Test-Path -LiteralPath $smokeRoot) {
        Remove-Item -LiteralPath $smokeRoot -Recurse -Force
    }
}
