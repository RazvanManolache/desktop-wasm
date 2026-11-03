#Requires -Version 5.1
# Stage an immutable controlled variant from a staged base generation.
# Copy-only: no source build, no live-server disturbance. Shell-owned.
param(
    [string]$Generation = "",
    [string]$BaseGeneration = "20261102T070000ZAGYshell5",
    [int]$BackendPort = 5240,
    [int]$GatewayPort = 5241,
    [string]$RewriterDll = "",
    [string]$ServerForkDll = "",
    [string]$RepoRoot = "E:\AI\Code\DesktopWasm"
)
$ErrorActionPreference = 'Stop'
if (-not $Generation) { throw "Generation required (UTC-stamped immutable name)" }
$base = Join-Path $RepoRoot (".artifacts\demo-deployments\" + $BaseGeneration)
$deploy = Join-Path $RepoRoot (".artifacts\demo-deployments\" + $Generation)
if (!(Test-Path $base)) { throw "Base generation missing: $base" }
if (Test-Path $deploy) { throw "Variant dir already exists (immutable): $deploy" }
Write-Host "Staging variant $Generation from $BaseGeneration"
robocopy $base $deploy /E /NJH /NJS /XD logs | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed: $LASTEXITCODE" }

$targetBackend = Join-Path $deploy "WfWasm.Host.Server"
$targetShell = Join-Path $deploy "DesktopWasm.Host.Server"
$targetWinforms = Join-Path $deploy "winforms"
$targetPortal = Join-Path $deploy "portal"

if ($RewriterDll -and (Test-Path $RewriterDll)) {
    Copy-Item $RewriterDll (Join-Path $targetBackend "WfWasm.Rewrite.dll") -Force
    Write-Host "Rewriter swapped: $RewriterDll"
}
if ($ServerForkDll -and (Test-Path $ServerForkDll)) {
    Copy-Item $ServerForkDll (Join-Path $targetBackend "WfWasm.WinForms.dll") -Force
    Write-Host "Server fork swapped: $ServerForkDll"
}

$rewriterHash = (Get-FileHash (Join-Path $targetBackend "WfWasm.Rewrite.dll") -Algorithm SHA256).Hash
$serverForkHash = (Get-FileHash (Join-Path $targetBackend "WfWasm.WinForms.dll") -Algorithm SHA256).Hash
$backendDllHash = (Get-FileHash (Join-Path $targetBackend "WfWasm.Host.Server.dll") -Algorithm SHA256).Hash
$shellDllHash = (Get-FileHash (Join-Path $targetShell "DesktopWasm.Host.Server.dll") -Algorithm SHA256).Hash
$canvasHostDllHash = (Get-FileHash (Join-Path $targetBackend "WfWasm.CanvasHost.dll") -Algorithm SHA256).Hash

$runBackendCmd = @"
@echo off
REM VARIANT $Generation backend :$BackendPort (base $BaseGeneration)
cd /d E:\AI\Code\DesktopWasm
set WFWASM_BENCHMARK_APPS_ROOT=E:\AI\Code\DesktopWasm\.artifacts\benchmark-apps
C:\Progra~1\dotnet\dotnet.exe $targetBackend\WfWasm.Host.Server.dll --urls http://127.0.0.1:$BackendPort --webroot $targetWinforms --Catalog:Path E:\AI\Code\DesktopWasm\catalog\apps.json >> $deploy\backend-$BackendPort.log 2>&1
"@
$runBackendCmd | Out-File -FilePath (Join-Path $deploy "run-backend-$BackendPort.cmd") -Encoding ascii

$runShellCmd = @"
@echo off
REM VARIANT $Generation gateway :$GatewayPort (backend :$BackendPort)
cd /d E:\AI\Code\DesktopWasm
set DESKTOPWASM_WPFROOT=E:\AI\Code\desktop-wasm-coordination\verification\dw-shell-route\generations\20261031T014103Z\wpfroot
set DESKTOPWASM_PACKAGES=E:\AI\Code\desktop-wasm-coordination\verification\dw-shell-route\generations\20261031T014103Z\packages
set DESKTOPWASM_WFBACKEND=http://127.0.0.1:$BackendPort
set DESKTOPWASM_WEBROOT=$targetPortal
C:\Progra~1\dotnet\dotnet.exe $targetShell\DesktopWasm.Host.Server.dll --urls http://127.0.0.1:$GatewayPort --Catalog:Path=E:/AI/Code/DesktopWasm/catalog/apps.json >> $deploy\shell-$GatewayPort.log 2>&1
"@
$runShellCmd | Out-File -FilePath (Join-Path $deploy "run-shell-$GatewayPort.cmd") -Encoding ascii

$deploymentJson = @{
    generation = $Generation
    base = $BaseGeneration
    createdUtc = [DateTime]::UtcNow.ToString("o")
    owner = "dw-shell"
    provenance = "RVW-8900 controlled variant (copy-only, no rebuild). See reports/RVW-8900-dw-shell-variants.md."
    deployment = $deploy
    ports = @{ shell = $GatewayPort; backend = $BackendPort }
    swaps = @{ rewriterDll = $RewriterDll; serverForkDll = $ServerForkDll }
    hashes = @{
        backendDll = $backendDllHash
        shellDll = $shellDllHash
        canvasHostDll = $canvasHostDllHash
        rewriterDll = $rewriterHash
        serverForkDll = $serverForkHash
    }
    rollback = "Variant uses isolated ports; public :5220/:5230 and frozen gen5 :5224/:5223 untouched. Rollback = stop variant PIDs."
} | ConvertTo-Json -Depth 6
$deploymentJson | Out-File -FilePath (Join-Path $deploy "deployment.json") -Encoding utf8
Write-Host "Variant staged: $deploy"
Write-Host "rewriter=$rewriterHash serverFork=$serverForkHash"
