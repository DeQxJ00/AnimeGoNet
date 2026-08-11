param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$RemoteHost = 'root@192.168.1.164',
    [string]$PlinkPath = 'C:\Program Files\PuTTY\plink.exe',
    [string]$PscpPath = 'C:\Program Files\PuTTY\pscp.exe',
    [string]$QbittorrentImage = 'lscr.io/linuxserver/qbittorrent:5.1.4',
    [string]$ReportRoot = 'E:\WorkSpaceAI\AnimeGoNet\TestSpace\animegonet_data\docker-ubuntu-ct',
    [switch]$FullChainWebUi
)

$ErrorActionPreference = 'Stop'
foreach ($path in @($PlinkPath, $PscpPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required PuTTY executable is missing: $path" }
}
$repository = [IO.Path]::GetFullPath($RepositoryRoot)
$runId = [Guid]::NewGuid().ToString('N')
$remoteRoot = "/var/tmp/animegonet-docker-audit-$runId"
$image = "animegonet:ct-$runId"
$archive = Join-Path ([IO.Path]::GetTempPath()) "animegonet-$runId.tar"
$started = [DateTimeOffset]::UtcNow
$stages = [Collections.Generic.List[object]]::new()
$fullChainWebUiValue = if ($FullChainWebUi) { 1 } else { 0 }

function Invoke-Remote([string]$Name, [string]$Command) {
    $stageStart = [DateTimeOffset]::UtcNow
    & $PlinkPath -batch -ssh $RemoteHost $Command
    $exitCode = $LASTEXITCODE
    $stages.Add([ordered]@{ name = $Name; exit_code = $exitCode; duration_seconds = [Math]::Round(([DateTimeOffset]::UtcNow - $stageStart).TotalSeconds, 3) })
    if ($exitCode -ne 0) { throw "Remote stage '$Name' failed with exit code $exitCode." }
}

try {
    $preflight = 'set -eu; test "$(. /etc/os-release; printf ''%s'' "$ID")" = ubuntu; test "$(. /etc/os-release; printf ''%s'' "$VERSION_ID")" = 24.04; test "$(uname -m)" = x86_64; docker version >/dev/null; docker compose version >/dev/null; mkdir -m 700 ''{0}''' -f $remoteRoot
    Invoke-Remote 'preflight' $preflight
    & git -C $repository archive --format=tar --output=$archive HEAD
    if ($LASTEXITCODE -ne 0) { throw 'git archive failed.' }
    & $PscpPath -batch $archive "${RemoteHost}:$remoteRoot/source.tar"
    if ($LASTEXITCODE -ne 0) { throw 'pscp source upload failed.' }
    Invoke-Remote 'extract' "set -eu; tar -xf '$remoteRoot/source.tar' -C '$remoteRoot'; rm -f '$remoteRoot/source.tar'"
    Invoke-Remote 'build-native-aot-image' "set -eu; cd '$remoteRoot'; docker build --pull --build-arg TARGETARCH=amd64 -f Dockerfile.animegonet -t '$image' ."
    Invoke-Remote 'container-api-sqlite-paths' "set -eu; cd '$remoteRoot'; GITHUB_RUN_ID='$runId' GITHUB_RUN_ATTEMPT=1 bash ./eng/smoke-container.sh '$image'"
    Invoke-Remote 'compose-qbittorrent-chain' "set -eu; cd '$remoteRoot'; GITHUB_RUN_ID='$runId' GITHUB_RUN_ATTEMPT=1 QBITTORRENT_IMAGE='$QbittorrentImage' ANIMEGONET_FULL_CHAIN_WEBUI=$fullChainWebUiValue bash ./eng/smoke-qbittorrent-compose.sh '$image'"
}
finally {
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
    & $PlinkPath -batch -ssh $RemoteHost "set -eu; docker image rm -f '$image' >/dev/null 2>&1 || true; case '$remoteRoot' in /var/tmp/animegonet-docker-audit-*) rm -rf -- '$remoteRoot' ;; *) exit 64 ;; esac"
    $cleanupExitCode = $LASTEXITCODE
    [IO.Directory]::CreateDirectory($ReportRoot) | Out-Null
    $report = [ordered]@{
        schema_version = 1
        executed_at_utc = $started
        completed_at_utc = [DateTimeOffset]::UtcNow
        remote_host = $RemoteHost
        expected_environment = 'Ubuntu 24.04 x86_64'
        qbittorrent_image = $QbittorrentImage
        full_chain_webui = [bool]$FullChainWebUi
        source_commit = (& git -C $repository rev-parse HEAD).Trim()
        stages = $stages
        cleanup_exit_code = $cleanupExitCode
    }
    $reportPath = Join-Path $ReportRoot "docker-ct-audit-$runId.json"
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8
    Write-Output "Docker Ubuntu CT audit report: $reportPath"
}
