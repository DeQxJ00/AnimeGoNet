[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [Parameter(Mandatory = $true)]
    [string]$AssetsFile,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-z0-9][a-z0-9.-]{0,63}$')]
    [string]$RuntimeIdentifier,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._+-]{0,127}$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-AtomicUtf8File {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Content
    )

    $temporaryPath = "$Path.partial-$([Guid]::NewGuid().ToString('N'))"
    try {
        [IO.File]::WriteAllText(
            $temporaryPath,
            ($Content -replace "`r`n", "`n"),
            [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporaryPath, $Path, $true)
    }
    finally {
        if ([IO.File]::Exists($temporaryPath)) {
            [IO.File]::Delete($temporaryPath)
        }
    }
}

function Read-NuspecMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create($Path, $settings)
    try {
        $document = [Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
    }
    finally {
        $reader.Dispose()
    }

    $metadata = $document.SelectSingleNode(
        "/*[local-name()='package']/*[local-name()='metadata']")
    if ($null -eq $metadata) {
        throw "NuGet package metadata is missing."
    }

    $license = $metadata.SelectSingleNode("*[local-name()='license']")
    $projectUrl = $metadata.SelectSingleNode("*[local-name()='projectUrl']")
    $licenseType = if ($null -eq $license) { 'unknown' } else { $license.GetAttribute('type') }
    $licenseValue = if ($null -eq $license) { 'NOASSERTION' } else { $license.InnerText.Trim() }
    if ([string]::IsNullOrWhiteSpace($licenseValue)) {
        $licenseValue = 'NOASSERTION'
    }
    if ($licenseValue.Length -gt 256 -or $licenseValue.IndexOfAny([char[]]"`r`n`t") -ge 0) {
        throw "NuGet package license metadata is invalid."
    }

    $projectUrlValue = if ($null -eq $projectUrl) { $null } else { $projectUrl.InnerText.Trim() }
    if (-not [string]::IsNullOrWhiteSpace($projectUrlValue)) {
        $parsedProjectUrl = $null
        if ((-not [Uri]::TryCreate($projectUrlValue, [UriKind]::Absolute, [ref]$parsedProjectUrl)) -or
            ($parsedProjectUrl.Scheme -notin @('http', 'https')) -or
            (-not [string]::IsNullOrEmpty($parsedProjectUrl.UserInfo))) {
            throw "NuGet package project URL is invalid."
        }
        $projectUrlValue = $parsedProjectUrl.AbsoluteUri
    }

    return [ordered]@{
        LicenseType = $licenseType
        LicenseValue = $licenseValue
        ProjectUrl = $projectUrlValue
    }
}

$publishRoot = [IO.Path]::GetFullPath($PublishDirectory)
$assetsPath = [IO.Path]::GetFullPath($AssetsFile)
if (-not [IO.Directory]::Exists($publishRoot)) {
    throw "Publish directory does not exist."
}
if (-not [IO.File]::Exists($assetsPath)) {
    throw "NuGet project.assets.json does not exist."
}

$assets = Get-Content -LiteralPath $assetsPath -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable
if ($assets.version -notin @(3, 4) -or $null -eq $assets.libraries -or $null -eq $assets.packageFolders) {
    throw "NuGet project.assets.json has an unsupported shape."
}

$packageFolders = [Collections.Generic.List[string]]::new()
foreach ($packageFolderKey in $assets.packageFolders.Keys) {
    $packageFolders.Add($packageFolderKey)
}
$packageFolders.Sort([StringComparer]::Ordinal)
$libraryKeys = [Collections.Generic.List[string]]::new()
foreach ($libraryKeyValue in $assets.libraries.Keys) {
    $libraryKeys.Add($libraryKeyValue)
}
$libraryKeys.Sort([StringComparer]::Ordinal)
$packages = [Collections.Generic.List[object]]::new()
foreach ($libraryKey in $libraryKeys) {
    $library = $assets.libraries[$libraryKey]
    if ($library.type -ne 'package') {
        continue
    }

    $identity = $libraryKey -split '/', 2
    if ($identity.Count -ne 2) {
        throw "NuGet package identity is invalid."
    }
    $name = $identity[0]
    $packageVersion = $identity[1]
    $relativePackagePath = $library.path.Replace('/', [IO.Path]::DirectorySeparatorChar)
    $packageRoot = $null
    foreach ($packageFolder in $packageFolders) {
        $candidate = [IO.Path]::GetFullPath([IO.Path]::Combine($packageFolder, $relativePackagePath))
        if ([IO.Directory]::Exists($candidate)) {
            $packageRoot = $candidate
            break
        }
    }
    if ($null -eq $packageRoot) {
        throw "NuGet package '$name/$packageVersion' is missing from the restored package folders."
    }

    $nuspecRelativePath = @($library.files | Where-Object {
            $_ -notmatch '[/\\]' -and $_.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase)
        })
    if ($nuspecRelativePath.Count -ne 1) {
        throw "NuGet package '$name/$packageVersion' must contain exactly one root nuspec."
    }
    $nuspecPath = [IO.Path]::GetFullPath(
        [IO.Path]::Combine($packageRoot, $nuspecRelativePath[0]))
    $packagePrefix = $packageRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ((-not $nuspecPath.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase)) -or
        (-not [IO.File]::Exists($nuspecPath))) {
        throw "NuGet package nuspec path is invalid."
    }

    $metadata = Read-NuspecMetadata -Path $nuspecPath
    $licenseText = $null
    if ($metadata.LicenseType -eq 'file') {
        $licenseRelativePath = $metadata.LicenseValue.Replace('/', [IO.Path]::DirectorySeparatorChar)
        $licensePath = [IO.Path]::GetFullPath([IO.Path]::Combine($packageRoot, $licenseRelativePath))
        if ((-not $licensePath.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase)) -or
            (-not [IO.File]::Exists($licensePath))) {
            throw "NuGet package license file path is invalid."
        }
        $licenseInfo = [IO.FileInfo]::new($licensePath)
        if ($licenseInfo.Length -gt 1MB) {
            throw "NuGet package license file is too large."
        }
        $licenseText = [IO.File]::ReadAllText(
            $licensePath,
            [Text.UTF8Encoding]::new($false, $true)).Replace("`r`n", "`n")
    }
    elseif ($metadata.LicenseType -ne 'expression') {
        throw "NuGet package must declare an SPDX expression or license file."
    }
    $sha512Bytes = [Convert]::FromBase64String($library.sha512)
    $packages.Add([ordered]@{
            Name = $name
            Version = $packageVersion
            Sha512 = [Convert]::ToHexString($sha512Bytes).ToLowerInvariant()
            LicenseType = $metadata.LicenseType
            LicenseValue = $metadata.LicenseValue
            LicenseText = $licenseText
            ProjectUrl = $metadata.ProjectUrl
        })
}
if ($packages.Count -eq 0) {
    throw "The restored graph contains no NuGet packages."
}
$packages.Sort([Comparison[object]]{
        param($left, $right)

        $nameOrder = [StringComparer]::Ordinal.Compare($left.Name, $right.Name)
        if ($nameOrder -ne 0) {
            return $nameOrder
        }
        return [StringComparer]::Ordinal.Compare($left.Version, $right.Version)
    })

$components = @($packages | ForEach-Object {
        $license = if ($_.LicenseType -eq 'expression' -and $_.LicenseValue -ne 'NOASSERTION') {
            [ordered]@{ expression = $_.LicenseValue }
        }
        else {
            [ordered]@{ license = [ordered]@{ name = $_.LicenseValue } }
        }
        $component = [ordered]@{
            type = 'library'
            name = $_.Name
            version = $_.Version
            hashes = @([ordered]@{ alg = 'SHA-512'; content = $_.Sha512 })
            licenses = @($license)
            purl = "pkg:nuget/$([Uri]::EscapeDataString($_.Name))@$([Uri]::EscapeDataString($_.Version))"
        }
        if (-not [string]::IsNullOrWhiteSpace($_.ProjectUrl)) {
            $component.externalReferences = @(
                [ordered]@{ type = 'website'; url = $_.ProjectUrl })
        }
        $component
    })

$bom = [ordered]@{
    bomFormat = 'CycloneDX'
    specVersion = '1.5'
    version = 1
    metadata = [ordered]@{
        component = [ordered]@{
            type = 'application'
            name = 'AnimeGoNet'
            version = $Version
            properties = @(
                [ordered]@{ name = 'animegonet:runtime-identifier'; value = $RuntimeIdentifier })
        }
    }
    components = $components
}
$bomJson = ($bom | ConvertTo-Json -Depth 12) + "`n"
Write-AtomicUtf8File -Path ([IO.Path]::Combine($publishRoot, 'sbom.cdx.json')) -Content $bomJson

$notices = [Text.StringBuilder]::new()
[void]$notices.AppendLine('AnimeGoNet third-party licenses')
[void]$notices.AppendLine('')
[void]$notices.AppendLine("Runtime identifier: $RuntimeIdentifier")
[void]$notices.AppendLine("Application version: $Version")
[void]$notices.AppendLine('Source: exact NuGet restore graph used for this artifact.')
foreach ($package in $packages) {
    [void]$notices.AppendLine('')
    [void]$notices.AppendLine("[$($package.Name) $($package.Version)]")
    [void]$notices.AppendLine("License: $($package.LicenseValue)")
    if (-not [string]::IsNullOrWhiteSpace($package.ProjectUrl)) {
        [void]$notices.AppendLine("Project: $($package.ProjectUrl)")
    }
    [void]$notices.AppendLine("NuGet SHA-512: $($package.Sha512)")
    if ($null -ne $package.LicenseText) {
        [void]$notices.AppendLine('--- license file begins ---')
        [void]$notices.AppendLine($package.LicenseText.TrimEnd())
        [void]$notices.AppendLine('--- license file ends ---')
    }
}
Write-AtomicUtf8File `
    -Path ([IO.Path]::Combine($publishRoot, 'THIRD-PARTY-LICENSES.txt')) `
    -Content $notices.ToString()

$checksumLines = [Collections.Generic.List[string]]::new()
$releaseFiles = Get-ChildItem -LiteralPath $publishRoot -File -Recurse | Where-Object {
    $_.Name -ne 'SHA256SUMS' -and $_.Name -notlike '*.partial-*'
}
$releaseFilesByPath = [Collections.Generic.Dictionary[string, IO.FileInfo]]::new(
    [StringComparer]::Ordinal)
foreach ($releaseFile in $releaseFiles) {
    if (($releaseFile.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Release artifact contains a symbolic link or reparse point."
    }
    $relativePath = [IO.Path]::GetRelativePath($publishRoot, $releaseFile.FullName).Replace('\', '/')
    if (-not $releaseFilesByPath.TryAdd($relativePath, $releaseFile)) {
        throw "Release artifact contains duplicate normalized paths."
    }
}
$releasePaths = [Collections.Generic.List[string]]::new()
foreach ($releasePath in $releaseFilesByPath.Keys) {
    $releasePaths.Add($releasePath)
}
$releasePaths.Sort([StringComparer]::Ordinal)
foreach ($relativePath in $releasePaths) {
    $releaseFile = $releaseFilesByPath[$relativePath]
    $sha256 = (Get-FileHash -LiteralPath $releaseFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumLines.Add("$sha256  $relativePath")
}
if ($checksumLines.Count -eq 0) {
    throw "Publish directory contains no release files."
}
Write-AtomicUtf8File `
    -Path ([IO.Path]::Combine($publishRoot, 'SHA256SUMS')) `
    -Content (($checksumLines -join "`n") + "`n")

Write-Output "Generated release metadata for $RuntimeIdentifier with $($packages.Count) NuGet packages and $($checksumLines.Count) checksums."
