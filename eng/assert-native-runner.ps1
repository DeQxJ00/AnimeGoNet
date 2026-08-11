[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-arm64')]
    [string]$RuntimeIdentifier
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$parts = $RuntimeIdentifier.Split('-', 2)
$expectedOs = $parts[0]
$expectedArchitecture = $parts[1]

$actualOs = if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Windows)) {
    'win'
}
elseif ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Linux)) {
    'linux'
}
elseif ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::OSX)) {
    'osx'
}
else {
    'unknown'
}

$actualArchitecture = switch (
    [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
    ([System.Runtime.InteropServices.Architecture]::X64) { 'x64' }
    ([System.Runtime.InteropServices.Architecture]::Arm64) { 'arm64' }
    default { $_.ToString().ToLowerInvariant() }
}

if ($actualOs -ne $expectedOs -or $actualArchitecture -ne $expectedArchitecture) {
    throw "Native runner mismatch: expected $RuntimeIdentifier, observed $actualOs-$actualArchitecture."
}

Write-Output "Native runner verified: $actualOs-$actualArchitecture"
