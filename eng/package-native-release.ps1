[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-arm64')]
    [string]$RuntimeIdentifier,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._+-]{0,127}$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-SafeRelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $relative = [IO.Path]::GetRelativePath($Root, $Path).Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($relative) -or
        $relative.StartsWith('../', [StringComparison]::Ordinal) -or
        $relative -eq '..' -or
        [IO.Path]::IsPathRooted($relative) -or
        $relative.Contains('//', [StringComparison]::Ordinal) -or
        $relative.Split('/').Where({ $_ -in @('', '.', '..') }).Count -ne 0) {
        throw "Release path is outside the publish directory or is not canonical."
    }
    return $relative
}

function Assert-NoReparsePoints {
    param([Parameter(Mandatory = $true)][string]$Root)

    $items = @(
        Get-ChildItem -LiteralPath $Root -Force -Recurse -ErrorAction Stop
    )
    foreach ($item in $items) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Release directory contains a symbolic link or reparse point."
        }
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    $stream = [IO.File]::OpenRead($Path)
    try {
        return [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream))
    }
    finally {
        $stream.Dispose()
    }
}

$publishRoot = [IO.Path]::GetFullPath($PublishDirectory)
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (-not [IO.Directory]::Exists($publishRoot)) {
    throw "Publish directory does not exist."
}
if ($outputRoot -eq $publishRoot -or
    $outputRoot.StartsWith($publishRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Output directory must be outside the publish directory."
}

Assert-NoReparsePoints -Root $publishRoot
$checksumPath = [IO.Path]::Combine($publishRoot, 'SHA256SUMS')
if (-not [IO.File]::Exists($checksumPath)) {
    throw "SHA256SUMS is missing from the publish directory."
}
foreach ($required in @('sbom.cdx.json', 'THIRD-PARTY-LICENSES.txt')) {
    if (-not [IO.File]::Exists([IO.Path]::Combine($publishRoot, $required))) {
        throw "Required release metadata is missing."
    }
}

$applicationName = if ($RuntimeIdentifier.StartsWith('win-', [StringComparison]::Ordinal)) {
    'AnimeGoNet.App.exe'
}
else {
    'AnimeGoNet.App'
}
$importerName = if ($RuntimeIdentifier.StartsWith('win-', [StringComparison]::Ordinal)) {
    'AnimeGoNet.LegacyCacheImporter.exe'
}
else {
    'AnimeGoNet.LegacyCacheImporter'
}
foreach ($required in @($applicationName, $importerName)) {
    if (-not [IO.File]::Exists([IO.Path]::Combine($publishRoot, $required))) {
        throw "Required NativeAOT executable is missing."
    }
}

$expected = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
$checksumLines = [IO.File]::ReadAllLines($checksumPath, [Text.UTF8Encoding]::new($false, $true))
if ($checksumLines.Count -eq 0) {
    throw "SHA256SUMS is empty."
}
foreach ($line in $checksumLines) {
    if ($line -notmatch '^([0-9a-f]{64})  ([^\\]+)$') {
        throw "SHA256SUMS contains an invalid line."
    }
    $relative = $Matches[2]
    if ($relative -eq 'SHA256SUMS' -or
        [IO.Path]::IsPathRooted($relative) -or
        $relative.Contains('//', [StringComparison]::Ordinal) -or
        $relative.Split('/').Where({ $_ -in @('', '.', '..') }).Count -ne 0 -or
        -not $expected.TryAdd($relative, $Matches[1])) {
        throw "SHA256SUMS contains an unsafe or duplicate path."
    }
    $fullPath = [IO.Path]::GetFullPath(
        [IO.Path]::Combine($publishRoot, $relative.Replace('/', [IO.Path]::DirectorySeparatorChar)))
    if (-not $fullPath.StartsWith(
            $publishRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not [IO.File]::Exists($fullPath) -or
        (Get-Sha256 -Path $fullPath) -ne $Matches[1]) {
        throw "Release file is missing or does not match SHA256SUMS."
    }
}

$fileMap = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
$relativePaths = [Collections.Generic.List[string]]::new()
foreach ($file in Get-ChildItem -LiteralPath $publishRoot -File -Force -Recurse) {
    $relative = Get-SafeRelativePath -Root $publishRoot -Path $file.FullName
    if (-not $fileMap.TryAdd($relative, $file.FullName)) {
        throw "Release directory contains duplicate canonical paths."
    }
    $relativePaths.Add($relative)
}
$relativePaths.Sort([StringComparer]::Ordinal)
$files = @($relativePaths | ForEach-Object {
    [pscustomobject]@{
        FullPath = $fileMap[$_]
        RelativePath = $_
    }
})
$actualChecksummed = @($files.RelativePath | Where-Object { $_ -ne 'SHA256SUMS' })
$expectedPaths = [Collections.Generic.List[string]]::new()
foreach ($path in $expected.Keys) {
    $expectedPaths.Add($path)
}
$expectedPaths.Sort([StringComparer]::Ordinal)
if ([string]::Join("`n", $actualChecksummed) -ne [string]::Join("`n", $expectedPaths)) {
    throw "SHA256SUMS does not cover the exact release file set."
}

[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$safeVersion = $Version.ToLowerInvariant()
$archiveName = "animegonet-$safeVersion-$RuntimeIdentifier.zip"
$archivePath = [IO.Path]::Combine($outputRoot, $archiveName)
$archiveChecksumPath = "$archivePath.sha256"
if ([IO.File]::Exists($archivePath) -or [IO.File]::Exists($archiveChecksumPath)) {
    throw "Release package output already exists."
}

$stream = [IO.File]::Open($archivePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    $archive = [IO.Compression.ZipArchive]::new(
        $stream,
        [IO.Compression.ZipArchiveMode]::Create,
        $true,
        [Text.UTF8Encoding]::new($false))
    try {
        foreach ($file in $files) {
            $entry = $archive.CreateEntry(
                $file.RelativePath,
                [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            if (-not $RuntimeIdentifier.StartsWith('win-', [StringComparison]::Ordinal)) {
                $unixMode = if ($file.RelativePath -in @($applicationName, $importerName)) {
                    0x81ed # regular file, 0755
                }
                else {
                    0x81a4 # regular file, 0644
                }
                $external = [int64]$unixMode -shl 16
                if ($external -gt [int]::MaxValue) {
                    $external -= 0x100000000
                }
                $entry.ExternalAttributes = [int]$external
            }
            else {
                $entry.ExternalAttributes = [int][IO.FileAttributes]::Archive
            }
            $input = [IO.File]::OpenRead($file.FullPath)
            $output = $entry.Open()
            try {
                $input.CopyTo($output)
            }
            finally {
                $output.Dispose()
                $input.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}
catch {
    $stream.Dispose()
    [IO.File]::Delete($archivePath)
    throw
}
finally {
    $stream.Dispose()
}

$archiveHash = Get-Sha256 -Path $archivePath
[IO.File]::WriteAllText(
    $archiveChecksumPath,
    "$archiveHash  $archiveName`n",
    [Text.UTF8Encoding]::new($false))

Write-Output $archivePath
Write-Output $archiveChecksumPath
