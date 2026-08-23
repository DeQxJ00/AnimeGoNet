param(
    [string]$Application = "src/AnimeGoNet.App/bin/Release/net10.0/AnimeGoNet.App.dll",
    [string]$Importer = "tools/AnimeGoNet.LegacyCacheImporter/bin/Release/net10.0/AnimeGoNet.LegacyCacheImporter.dll",
    [int]$ExpectedSchemaVersion = 56,
    [int]$Port = 0
)

$ErrorActionPreference = 'Stop'
$resolvedApplication = (Resolve-Path -LiteralPath $Application).Path
$resolvedImporter = (Resolve-Path -LiteralPath $Importer).Path
$cacheFixture = (Resolve-Path -LiteralPath 'eng/fixtures/legacy-cache-export-v1.json').Path
$libraryFixture = (Resolve-Path -LiteralPath 'eng/fixtures/legacy-library').Path
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar)
$smokeRoot = Join-Path $temporaryRoot ("animegonet-legacy-migration-smoke-" + [Guid]::NewGuid().ToString('N'))
$dataPath = Join-Path $smokeRoot 'data'
$downloadPath = Join-Path $smokeRoot 'download/incomplete'
$savePath = Join-Path $smokeRoot 'download/anime'
$Port = if ($Port -eq 0) { Get-Random -Minimum 20000 -Maximum 60000 } else { $Port }
$baseUrl = "http://127.0.0.1:$Port"
$process = $null

function Get-LaunchParameters {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [object[]]$Arguments
    )

    if ([IO.Path]::GetExtension($Path) -eq '.dll') {
        return @{
            FilePath = 'dotnet'
            ArgumentList = @($Path) + $Arguments
        }
    }
    return @{
        FilePath = $Path
        ArgumentList = $Arguments
    }
}

function Start-SmokeApplication {
    $env:data_path = $dataPath
    $env:download_path = $downloadPath
    $env:save_path = $savePath
    $env:background_workers_enabled = 'false'
    $parameters = Get-LaunchParameters `
        -Path $resolvedApplication `
        -Arguments @('--urls', $baseUrl)
    $parameters.PassThru = $true
    if ($IsWindows) {
        $parameters.WindowStyle = 'Hidden'
    }
    $script:process = Start-Process @parameters
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if ($script:process.HasExited) {
            throw "Application exited before migration smoke became ready (exit $($script:process.ExitCode))."
        }
        try {
            $ping = Invoke-RestMethod -Uri "$baseUrl/ping" -TimeoutSec 2
            if ($ping.code -eq 200 -and $ping.msg -eq 'pong') {
                return
            }
        }
        catch {
            Start-Sleep -Milliseconds 250
        }
    }
    throw 'Application did not become ready for migration smoke.'
}

function Invoke-SmokeImporter {
    $arguments = @('--data-path', $dataPath, '--input', $cacheFixture)
    if ([IO.Path]::GetExtension($resolvedImporter) -eq '.dll') {
        return ((& dotnet $resolvedImporter @arguments) -join "`n")
    }
    return ((& $resolvedImporter @arguments) -join "`n")
}

function Stop-SmokeApplication {
    if ($null -ne $script:process -and -not $script:process.HasExited) {
        Stop-Process -Id $script:process.Id
        $script:process.WaitForExit(5000) | Out-Null
    }
    $script:process = $null
}

try {
    New-Item -ItemType Directory -Path $savePath -Force | Out-Null
    Copy-Item -LiteralPath $libraryFixture -Destination $savePath -Recurse

    Start-SmokeApplication
    $initialStatus = Invoke-RestMethod -Uri "$baseUrl/api/v1/status" -TimeoutSec 5
    $initialDirectory = Invoke-RestMethod `
        -Uri "$baseUrl/api/v1/library/directory-database" `
        -TimeoutSec 5
    if ($initialStatus.database_schema_version -ne $ExpectedSchemaVersion `
        -or $initialDirectory.entry_count -ne 3 `
        -or $initialDirectory.last_rejected_count -ne 0) {
        throw 'Initial schema or legacy directory sidecar scan did not match the fixture.'
    }
    Stop-SmokeApplication

    $firstText = Invoke-SmokeImporter
    if ($LASTEXITCODE -ne 0) {
        throw "First legacy cache import failed with exit code $LASTEXITCODE."
    }
    $first = $firstText | ConvertFrom-Json
    if ($first.status -ne 'imported' `
        -or $first.bucket_count -ne 6 `
        -or $first.entry_count -ne 3 `
        -or $first.imported_entry_count -ne 2 `
        -or $first.skipped_expired_entry_count -ne 1 `
        -or $first.repeat_count -ne 0) {
        throw 'First legacy cache import report did not match the fixture.'
    }

    $secondText = Invoke-SmokeImporter
    if ($LASTEXITCODE -ne 0) {
        throw "Repeated legacy cache import failed with exit code $LASTEXITCODE."
    }
    $second = $secondText | ConvertFrom-Json
    if ($second.status -ne 'already_imported' `
        -or $second.package_sha256 -ne $first.package_sha256 `
        -or $second.repeat_count -ne 1) {
        throw 'Repeated legacy cache import was not idempotent.'
    }

    Start-SmokeApplication
    $finalStatus = Invoke-RestMethod -Uri "$baseUrl/api/v1/status" -TimeoutSec 5
    $finalDirectory = Invoke-RestMethod `
        -Uri "$baseUrl/api/v1/library/directory-database" `
        -TimeoutSec 5
    $mainBuckets = Invoke-RestMethod `
        -Uri "$baseUrl/api/v1/cache/buckets?database=bolt" `
        -TimeoutSec 5
    $archiveBuckets = Invoke-RestMethod `
        -Uri "$baseUrl/api/v1/cache/buckets?database=bolt_sub" `
        -TimeoutSec 5
    if ($finalStatus.database_schema_version -ne $ExpectedSchemaVersion `
        -or $finalDirectory.entry_count -ne 3 `
        -or @($mainBuckets.items).Count -ne 5 `
        -or @($archiveBuckets.items).Count -ne 1) {
        throw 'Restart verification did not preserve imported cache and directory indexes.'
    }
}
finally {
    Stop-SmokeApplication
    $resolvedSmokeRoot = [IO.Path]::GetFullPath($smokeRoot)
    if (-not $resolvedSmokeRoot.StartsWith(
            $temporaryRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to clean a migration smoke path outside the system temp directory.'
    }
    if (Test-Path -LiteralPath $resolvedSmokeRoot) {
        Remove-Item -LiteralPath $resolvedSmokeRoot -Recurse -Force
    }
}

Write-Host 'Legacy cache plus directory-sidecar migration smoke passed.'
