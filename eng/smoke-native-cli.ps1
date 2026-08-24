param(
    [Parameter(Mandatory = $true)]
    [string]$Executable
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = (Resolve-Path -LiteralPath $Executable).Path
$smokeRoot = Join-Path `
    ([IO.Path]::GetTempPath()) `
    ('animegonet-native-cli-smoke-' + [Guid]::NewGuid().ToString('N'))
$stdoutPath = Join-Path $smokeRoot 'stdout.log'
$stderrPath = Join-Path $smokeRoot 'stderr.log'
$process = $null
$shutdownFailure = $null

function Invoke-BoundedProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutMilliseconds
    )

    Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
    $child = Start-Process `
        -FilePath $resolvedExecutable `
        -ArgumentList $ArgumentList `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -NoNewWindow `
        -PassThru
    if (-not $child.WaitForExit($TimeoutMilliseconds)) {
        Stop-Process -Id $child.Id -Force -ErrorAction SilentlyContinue
        [void]$child.WaitForExit(5000)
        throw "Published CLI did not exit within $TimeoutMilliseconds ms."
    }
    if ($child.ExitCode -ne 0) {
        throw "Published CLI returned exit code $($child.ExitCode)."
    }

    return [IO.File]::ReadAllText($stdoutPath) + [IO.File]::ReadAllText($stderrPath)
}

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

New-Item -ItemType Directory -Path $smokeRoot -Force | Out-Null
$savedEnvironment = @{}
foreach ($name in @(
    'ANIMEGO_CONFIG',
    'ANIMEGO_DATA_PATH',
    'ANIMEGO_DOWNLOAD_PATH',
    'ANIMEGO_SAVE_PATH',
    'ANIMEGO_WEB',
    'background_workers_enabled')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}

try {
    $help = Invoke-BoundedProcess -ArgumentList @('--help') -TimeoutMilliseconds 10000
    foreach ($required in @('--config', '--debug', '--web', '--backup')) {
        if (-not $help.Contains($required, [StringComparison]::Ordinal)) {
            throw "Published CLI help is missing $required."
        }
    }

    [Environment]::SetEnvironmentVariable('ANIMEGO_CONFIG', $null)
    $env:ANIMEGO_DATA_PATH = Join-Path $smokeRoot 'data'
    $env:ANIMEGO_DOWNLOAD_PATH = Join-Path $smokeRoot 'download/incomplete'
    $env:ANIMEGO_SAVE_PATH = Join-Path $smokeRoot 'download/anime'
    $env:ANIMEGO_WEB = 'true'
    $env:background_workers_enabled = 'false'
    $port = Get-FreeLoopbackPort
    $process = Start-Process `
        -FilePath $resolvedExecutable `
        -ArgumentList @('-web=false', "--urls=http://127.0.0.1:$port") `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -NoNewWindow `
        -PassThru

    $databasePath = Join-Path $env:ANIMEGO_DATA_PATH 'animegonet.db'
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while (-not (Test-Path -LiteralPath $databasePath)) {
        if ($process.HasExited) {
            throw "Published headless host exited early with code $($process.ExitCode)."
        }
        if ([DateTime]::UtcNow -ge $deadline) {
            throw 'Published headless host did not initialize its database within 20 seconds.'
        }
        Start-Sleep -Milliseconds 100
    }

    $client = [Net.Sockets.TcpClient]::new()
    try {
        $connect = $client.ConnectAsync([Net.IPAddress]::Loopback, $port)
        if ($connect.Wait(1000) -and $client.Connected) {
            throw 'Published --web=false host unexpectedly opened a TCP listener.'
        }
    }
    catch [AggregateException] {
        # Connection refusal is the expected result for the no-listener server.
    }
    catch [Net.Sockets.SocketException] {
        # Connection refusal is the expected result for the no-listener server.
    }
    finally {
        $client.Dispose()
    }

    Write-Output "Native CLI smoke passed: $resolvedExecutable"
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        if (-not $IsWindows) {
            & /bin/kill -TERM $process.Id
            if ($LASTEXITCODE -ne 0) {
                $shutdownFailure = "Could not send SIGTERM to headless process $($process.Id)."
            }
            elseif (-not $process.WaitForExit(7000)) {
                $shutdownFailure = 'Published headless process did not exit within seven seconds after SIGTERM.'
            }
            elseif ($process.ExitCode -notin @(0, 143)) {
                $shutdownFailure = "Published headless process returned exit code $($process.ExitCode) after SIGTERM."
            }
        }
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            [void]$process.WaitForExit(5000)
        }
    }

    foreach ($entry in $savedEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
    }
    if (Test-Path -LiteralPath $smokeRoot) {
        [IO.Directory]::Delete($smokeRoot, $true)
    }
    if ($null -ne $shutdownFailure) {
        throw $shutdownFailure
    }
}
