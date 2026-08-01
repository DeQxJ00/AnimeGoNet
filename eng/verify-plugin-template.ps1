[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-arm64')]
    [string] $RuntimeIdentifier,
    [switch] $SkipNativeAot
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$workingDirectory = [System.IO.Path]::GetFullPath((Join-Path $temporaryRoot (
    'AnimeGoPluginTemplate-' + [guid]::NewGuid().ToString('N'))))
if (-not $workingDirectory.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The template verification directory must be inside the system temporary directory.'
}

function Invoke-DotNet {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Resolve-CurrentRid {
    if ($IsWindows) {
        return if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') {
            'win-arm64'
        } else {
            'win-x64'
        }
    }
    if ($IsMacOS) {
        return 'osx-arm64'
    }
    return if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') {
        'linux-arm64'
    } else {
        'linux-x64'
    }
}

New-Item -ItemType Directory -Path $workingDirectory | Out-Null
try {
    $feed = Join-Path $workingDirectory 'feed'
    $hive = Join-Path $workingDirectory 'template-hive'
    $generated = Join-Path $workingDirectory 'generated'
    New-Item -ItemType Directory -Path $feed, $generated | Out-Null

    Push-Location $repositoryRoot
    try {
        Invoke-DotNet pack 'src/AnimeGo.Plugin.Abstractions/AnimeGo.Plugin.Abstractions.csproj' `
            '--configuration' 'Release' '--output' $feed
        Invoke-DotNet pack 'src/AnimeGo.Plugin.Sdk/AnimeGo.Plugin.Sdk.csproj' `
            '--configuration' 'Release' '--output' $feed
        Invoke-DotNet pack 'templates/AnimeGo.Plugin.Templates/AnimeGo.Plugin.Templates.csproj' `
            '--configuration' 'Release' '--output' $feed
    }
    finally {
        Pop-Location
    }

    $templatePackage = Get-Item (Join-Path $feed 'AnimeGo.Plugin.Templates.1.0.0.nupkg')
    Invoke-DotNet new '--debug:custom-hive' $hive 'install' $templatePackage.FullName

    $nugetConfig = Join-Path $workingDirectory 'NuGet.config'
    Copy-Item (Join-Path $repositoryRoot 'NuGet.config') $nugetConfig
    Invoke-DotNet nuget add source $feed '--name' 'animego-template-local' '--configfile' $nugetConfig

    $pluginTypes = @('source', 'feed', 'parser', 'filter', 'rename', 'schedule')
    foreach ($pluginType in $pluginTypes) {
        $output = Join-Path $generated $pluginType
        $projectName = "AnimeGo.Example.$pluginType"
        Invoke-DotNet new '--debug:custom-hive' $hive 'animego-plugin' `
            '--param:type' $pluginType `
            '--plugin-id' "com.example.$pluginType" `
            '--plugin-name' "Example $pluginType" `
            '--name' $projectName `
            '--output' $output

        $project = Join-Path $output "$projectName.csproj"
        Invoke-DotNet restore $project '--configfile' $nugetConfig
        Invoke-DotNet build $project '--configuration' 'Release' '--no-restore'

        $programs = @(Get-ChildItem $output -Filter 'Program.*.cs')
        $handlers = @(Get-ChildItem $output -Filter 'PluginHandler.*.cs')
        if ($programs.Count -ne 1 -or $handlers.Count -ne 1) {
            throw "Template '$pluginType' did not select exactly one Program and Handler."
        }
        $manifest = Get-Content (Join-Path $output 'plugin.json') -Raw | ConvertFrom-Json
        if ($manifest.type -ne $pluginType -or $manifest.id -ne "com.example.$pluginType") {
            throw "Template '$pluginType' did not replace its manifest identity."
        }
    }

    if (-not $SkipNativeAot) {
        $rid = if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
            Resolve-CurrentRid
        } else {
            $RuntimeIdentifier
        }
        $filterProject = Join-Path $generated 'filter/AnimeGo.Example.filter.csproj'
        $publishDirectory = Join-Path $workingDirectory "publish/$rid"
        Invoke-DotNet publish $filterProject `
            '--configuration' 'Release' `
            '--runtime' $rid `
            '--self-contained' 'true' `
            '--output' $publishDirectory `
            '-p:PublishAot=true'

        $entryPoint = if ($rid.StartsWith('win-', [StringComparison]::Ordinal)) {
            Join-Path $publishDirectory 'AnimeGo.Example.filter.exe'
        } else {
            Join-Path $publishDirectory 'AnimeGo.Example.filter'
        }
        if (-not (Test-Path $entryPoint -PathType Leaf)) {
            throw "NativeAOT entry point '$entryPoint' was not produced."
        }

        $dataPath = Join-Path $workingDirectory 'plugin-data'
        New-Item -ItemType Directory -Path $dataPath | Out-Null
        $previousId = $env:ANIMEGO_PLUGIN_ID
        $previousApi = $env:ANIMEGO_PLUGIN_API_VERSION
        $previousData = $env:ANIMEGO_PLUGIN_DATA_PATH
        try {
            $env:ANIMEGO_PLUGIN_ID = 'com.example.filter'
            $env:ANIMEGO_PLUGIN_API_VERSION = '1'
            $env:ANIMEGO_PLUGIN_DATA_PATH = $dataPath
            $requests = @(
                '{"apiVersion":1,"requestId":"00000000000000000000000000000001","method":"initialize","payload":{"hostVersion":"1.0.0","pluginId":"com.example.filter","pluginVersion":"1.0.0","apiVersion":1,"type":"filter","capabilities":[]}}',
                '{"apiVersion":1,"requestId":"00000000000000000000000000000002","method":"execute","operation":"filter.all","payload":{"sourceProfileId":"fixture","items":[],"arguments":{},"sourceProfileSnapshot":null},"config":{}}',
                '{"apiVersion":1,"requestId":"00000000000000000000000000000003","method":"health"}',
                '{"apiVersion":1,"requestId":"00000000000000000000000000000004","method":"shutdown","payload":{"reason":"verification"}}'
            )
            $responses = ($requests -join "`n") | & $entryPoint
            if ($LASTEXITCODE -ne 0) {
                throw "Native plugin exited with code $LASTEXITCODE."
            }
            if (@($responses).Count -ne 4) {
                throw 'Native plugin did not return exactly four lifecycle responses.'
            }
            foreach ($line in $responses) {
                $response = $line | ConvertFrom-Json
                if (-not $response.ok) {
                    throw "Native plugin lifecycle failed: $line"
                }
            }
        }
        finally {
            $env:ANIMEGO_PLUGIN_ID = $previousId
            $env:ANIMEGO_PLUGIN_API_VERSION = $previousApi
            $env:ANIMEGO_PLUGIN_DATA_PATH = $previousData
        }
    }

    Write-Host 'AnimeGo plugin SDK/template verification passed.'
}
finally {
    $resolvedWorkingDirectory = [System.IO.Path]::GetFullPath($workingDirectory)
    $safeToDelete = $resolvedWorkingDirectory.StartsWith(
        $temporaryRoot,
        [StringComparison]::OrdinalIgnoreCase)
    if ((Test-Path -LiteralPath $resolvedWorkingDirectory) -and $safeToDelete) {
        Remove-Item -LiteralPath $resolvedWorkingDirectory -Recurse -Force
    }
}
