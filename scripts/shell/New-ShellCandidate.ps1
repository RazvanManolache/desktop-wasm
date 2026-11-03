#Requires -Version 5.1
<#
.SYNOPSIS
  dw-shell owned candidate harness: sequenced build + immutable hashes.

  Build order is load-bearing: WfWasm.CanvasHost FIRST, then the WinForms
  backend (WfWasm.Host.Server), then the unified shell + portal. A CanvasHost
  rebuild after the backend leaves the running backend's static-asset manifest
  stale (served-slice 404s, 2026-10-30). This script encodes that order and
  pins the resulting deployment/asset hashes into a JSON receipt so the
  verifier can pin one generation.

  Owns no product source outside the shell lane: it only BUILDS
  importer-owned projects (CanvasHost, backend) and READS their outputs.
  Run builds under the shared lease:
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/with-build-lock.ps1 -Repo DesktopWasm -Command <wrapper>
  from E:/AI/Code/desktop-wasm-coordination. -SkipBuild is read-only
  (hash only, no lease needed) for frozen acceptance windows.
#>
param(
    [string]$RepoRoot = "E:/AI/Code/DesktopWasm",
    [string]$OutDir = "E:/AI/Code/desktop-wasm-coordination/verification/dw-shell-route",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$started = [DateTime]::UtcNow
$generation = $started.ToString("yyyyMMddTHHmmssZ")
$builds = @()

function Invoke-Step {
    param([string]$Name, [string]$Project)
    $t0 = [DateTime]::UtcNow
    dotnet build (Join-Path $RepoRoot $Project) --nologo -v q 2>&1 | Out-String | Write-Host
    $exit = $LASTEXITCODE
    $t1 = [DateTime]::UtcNow
    $script:builds += @{
        project = $Project; exit = $exit
        startedUtc = $t0.ToString("o"); endedUtc = $t1.ToString("o")
        durationMs = [int](($t1 - $t0).TotalMilliseconds)
    }
    if ($exit -ne 0) { throw "build failed: $Project (exit $exit)" }
}

if (-not $SkipBuild) {
    # ORDER: CanvasHost before backend before shell. Do not reorder.
    Invoke-Step "canvas-host" "src/WfWasm.CanvasHost/WfWasm.CanvasHost.csproj"
    Invoke-Step "winforms-backend" "src/WfWasm.Host.Server/WfWasm.Host.Server.csproj"
    Invoke-Step "shell" "src/DesktopWasm.Host.Server/DesktopWasm.Host.Server.csproj"
    Invoke-Step "portal" "src/DesktopWasm.Web/DesktopWasm.Web.csproj"
}

function Get-RelHash {
    param([string]$Relative)
    $full = Join-Path $RepoRoot $Relative
    if (-not (Test-Path $full -PathType Leaf)) {
        return @{ path = $Relative; state = "absent" }
    }
    $h = (Get-FileHash $full -Algorithm SHA256).Hash
    $info = Get-Item $full
    return @{ path = $Relative; state = "present"; sha256 = $h; bytes = $info.Length; mtimeUtc = $info.LastWriteTimeUtc.ToString("o") }
}

# Newest backend boot manifest wins: it is the fingerprint that staled mid-slice.
$bootCandidates = @(@(
    "src/WfWasm.Host.Server/bin/Debug/net9.0/wwwroot/_framework/blazor.boot.json",
    "src/WfWasm.CanvasHost/bin/Debug/net9.0/wwwroot/_framework/blazor.boot.json",
    "src/WfWasm.CanvasHost/bin/Release/net9.0/wwwroot/_framework/blazor.boot.json"
) | Where-Object { Test-Path (Join-Path $RepoRoot $_) -PathType Leaf })
$bootManifest = if ($bootCandidates.Count -gt 0) { $bootCandidates[0] } else { $null }

$hashes = @(
    (Get-RelHash "src/DesktopWasm.BrowserHost/wwwroot/entry.mjs"),
    (Get-RelHash "src/DesktopWasm.Web/wwwroot/index.html"),
    (Get-RelHash "catalog/apps.json")
)
if ($bootManifest) { $hashes += Get-RelHash $bootManifest }

$orderOk = $true
if ($builds.Count -ge 2) {
    $orderOk = ([DateTime]$builds[1].startedUtc) -ge ([DateTime]$builds[0].endedUtc)
}

$receipt = @{
    generation = $generation
    owner = "dw-shell"
    harness = "scripts/shell/New-ShellCandidate.ps1"
    utc = $started.ToString("o")
    skipBuild = [bool]$SkipBuild
    buildOrder = @("WfWasm.CanvasHost", "WfWasm.Host.Server", "DesktopWasm.Host.Server", "DesktopWasm.Web")
    builds = $builds
    backendAfterCanvasHost = $orderOk
    backendBootManifest = $bootManifest
    hashes = $hashes
} | ConvertTo-Json -Depth 6

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$receiptPath = Join-Path $OutDir ("candidate-" + $generation + ".json")
$receipt | Out-File -Encoding utf8 -FilePath $receiptPath
Write-Host ("receipt=" + $receiptPath)
Write-Host ("backendAfterCanvasHost=" + $orderOk)
