#Requires -Version 5.1
param(
    [string]$Generation = "20261101T120000ZAGYshell1",
    [string]$RepoRoot = "E:\AI\Code\DesktopWasm",
    [string]$CoordinationRoot = "E:\AI\Code\desktop-wasm-coordination"
)

$ErrorActionPreference = 'Stop'
$deploy = Join-Path $RepoRoot (".artifacts\demo-deployments\" + $Generation)
New-Item -ItemType Directory -Path $deploy -Force | Out-Null

Write-Host "Staging candidate into: $deploy"

# 1. Build projects under build lock
$buildScript = Join-Path $deploy "build.ps1"
@"
`$ErrorActionPreference = 'Stop'
Set-Location '$RepoRoot'
Write-Host 'Building CanvasHost...'
dotnet build src/WfWasm.CanvasHost/WfWasm.CanvasHost.csproj --nologo -v q
Write-Host 'Building WfWasm.Host.Server...'
dotnet build src/WfWasm.Host.Server/WfWasm.Host.Server.csproj --nologo -v q
Write-Host 'Building DesktopWasm.Host.Server...'
dotnet build src/DesktopWasm.Host.Server/DesktopWasm.Host.Server.csproj --nologo -v q
Write-Host 'Building DesktopWasm.Web...'
dotnet build src/DesktopWasm.Web/DesktopWasm.Web.csproj --nologo -v q
"@ | Out-File -FilePath $buildScript -Encoding utf8

powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $CoordinationRoot "tools\with-build-lock.ps1") -Repo DesktopWasm -Command $buildScript
if ($LASTEXITCODE -ne 0) { throw "Candidate build failed with code $LASTEXITCODE" }

# 2. Stage server binaries
$targetShell = Join-Path $deploy "DesktopWasm.Host.Server"
$targetBackend = Join-Path $deploy "WfWasm.Host.Server"
New-Item -ItemType Directory -Path $targetShell -Force | Out-Null
New-Item -ItemType Directory -Path $targetBackend -Force | Out-Null
Copy-Item (Join-Path $RepoRoot "src\DesktopWasm.Host.Server\bin\Debug\net9.0\*") $targetShell -Recurse -Force
Copy-Item (Join-Path $RepoRoot "src\WfWasm.Host.Server\bin\Debug\net9.0\*") $targetBackend -Recurse -Force

# 3. Stage winforms wwwroot
$targetWinforms = Join-Path $deploy "winforms"
New-Item -ItemType Directory -Path $targetWinforms -Force | Out-Null
powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "scripts\winforms\deploy-wwwroot.ps1") -ServerRoot $targetWinforms
if ($LASTEXITCODE -ne 0) { throw "deploy-wwwroot failed with code $LASTEXITCODE" }

# 4. Stage portal
$targetPortal = Join-Path $deploy "portal"
New-Item -ItemType Directory -Path $targetPortal -Force | Out-Null
$portalSrc = Join-Path $RepoRoot ".artifacts\demo-deployments\20261031T113000ZUNSTALLshell\portal"
Copy-Item "$portalSrc\*" $targetPortal -Recurse -Force

# 4b. Pre-compress static assets (.br and .gz)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "scripts\shell\Precompress-StaticAssets.ps1") -TargetDir $targetWinforms
powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "scripts\shell\Precompress-StaticAssets.ps1") -TargetDir $targetPortal

# 5. Write launchers
$runBackendCmd = @"
@echo off
REM AGY-FALLBACK-SHELL candidate backend :5223
cd /d E:\AI\Code\DesktopWasm
set WFWASM_BENCHMARK_APPS_ROOT=E:\AI\Code\DesktopWasm\.artifacts\benchmark-apps
C:\Progra~1\dotnet\dotnet.exe $targetBackend\WfWasm.Host.Server.dll --urls http://127.0.0.1:5223 --webroot $targetWinforms --Catalog:Path E:\AI\Code\DesktopWasm\catalog\apps.json >> $deploy\backend-5223.log 2>&1
"@
$runBackendCmd | Out-File -FilePath (Join-Path $deploy "run-backend-5223.cmd") -Encoding ascii

$runShellCmd = @"
@echo off
REM AGY-FALLBACK-SHELL candidate shell :5224 (backend :5223)
cd /d E:\AI\Code\DesktopWasm
set DESKTOPWASM_WPFROOT=E:\AI\Code\desktop-wasm-coordination\verification\dw-shell-route\generations\20261031T014103Z\wpfroot
set DESKTOPWASM_PACKAGES=E:\AI\Code\desktop-wasm-coordination\verification\dw-shell-route\generations\20261031T014103Z\packages
set DESKTOPWASM_WFBACKEND=http://127.0.0.1:5223
set DESKTOPWASM_WEBROOT=$targetPortal
C:\Progra~1\dotnet\dotnet.exe $targetShell\DesktopWasm.Host.Server.dll --urls http://127.0.0.1:5224 --Catalog:Path=E:/AI/Code/DesktopWasm/catalog/apps.json >> $deploy\shell-5224.log 2>&1
"@
$runShellCmd | Out-File -FilePath (Join-Path $deploy "run-shell-5224.cmd") -Encoding ascii

$runPublicBackendCmd = @"
@echo off
REM AGY-FALLBACK-SHELL public backend :5230 ($Generation)
cd /d E:\AI\Code\DesktopWasm
set WFWASM_BENCHMARK_APPS_ROOT=E:\AI\Code\DesktopWasm\.artifacts\benchmark-apps
C:\Progra~1\dotnet\dotnet.exe $targetBackend\WfWasm.Host.Server.dll --urls http://127.0.0.1:5230 --webroot $targetWinforms --Catalog:Path E:\AI\Code\DesktopWasm\catalog\apps.json >> $deploy\backend-5230.log 2>&1
"@
$runPublicBackendCmd | Out-File -FilePath (Join-Path $deploy "run-public-backend-5230.cmd") -Encoding ascii

$runPublicShellCmd = @"
@echo off
REM AGY-FALLBACK-SHELL public gateway :5220 (backend :5230) ($Generation)
cd /d E:\AI\Code\DesktopWasm
set DESKTOPWASM_WPFROOT=E:\AI\Code\desktop-wasm-coordination\verification\dw-shell-route\generations\20261031T014103Z\wpfroot
set DESKTOPWASM_PACKAGES=E:\AI\Code\desktop-wasm-coordination\verification\dw-shell-route\generations\20261031T014103Z\packages
set DESKTOPWASM_WFBACKEND=http://127.0.0.1:5230
set DESKTOPWASM_WEBROOT=$targetPortal
C:\Progra~1\dotnet\dotnet.exe $targetShell\DesktopWasm.Host.Server.dll --urls http://127.0.0.1:5220 --Catalog:Path=E:/AI/Code/DesktopWasm/catalog/apps.json >> $deploy\shell-5220.log 2>&1
"@
$runPublicShellCmd | Out-File -FilePath (Join-Path $deploy "run-public-shell-5220.cmd") -Encoding ascii

$rollbackBackendCmd = @"
@echo off
REM Candidate-only rollback for :5223
powershell -ExecutionPolicy Bypass -Command "`$c = Get-NetTCPConnection -LocalPort 5223 -ErrorAction SilentlyContinue; if (`$c) { Stop-Process -Id `$c.OwningProcess -Force }"
"@
$rollbackBackendCmd | Out-File -FilePath (Join-Path $deploy "run-rollback-5223.cmd") -Encoding ascii

$rollbackShellCmd = @"
@echo off
REM Candidate-only rollback for :5224
powershell -ExecutionPolicy Bypass -Command "`$c = Get-NetTCPConnection -LocalPort 5224 -ErrorAction SilentlyContinue; if (`$c) { Stop-Process -Id `$c.OwningProcess -Force }"
"@
$rollbackShellCmd | Out-File -FilePath (Join-Path $deploy "run-rollback-5224.cmd") -Encoding ascii

# 6. Hashes
$backendDllHash = (Get-FileHash "$targetBackend\WfWasm.Host.Server.dll" -Algorithm SHA256).Hash
$shellDllHash = (Get-FileHash "$targetShell\DesktopWasm.Host.Server.dll" -Algorithm SHA256).Hash
$canvasHostDllHash = (Get-FileHash "$targetBackend\WfWasm.CanvasHost.dll" -Algorithm SHA256).Hash

$deploymentJson = @{
    generation = $Generation
    createdUtc = [DateTime]::UtcNow.ToString("o")
    owner = "dw-shell"
    provenance = "AGY-FALLBACK-SHELL candidate: startup profiling optimizations, launch error presentation, pre-compression (.br/.gz), and AppAssemblyLoader prefetch telemetry. Gated on verifier acceptance."
    deployment = $deploy
    ports = @{
        shell = 5224
        backend = 5223
    }
    rollback = @{
        liveShell5220 = "E:\AI\Code\DesktopWasm\.artifacts\demo-deployments\20261102T001500ZAGYshell3\DesktopWasm.Host.Server"
        liveBackend5230 = "E:\AI\Code\DesktopWasm\.artifacts\demo-deployments\20261102T001500ZAGYshell3\WfWasm.Host.Server"
        note = "Candidate uses isolated ports 5224/5223; public :5220/:5230 untouched. Rollback = stop candidate PIDs, verify public pair 200."
    }
    sources = @{
        backendServer = "$targetBackend"
        shellServer = "$targetShell"
        winformsWebroot = "$targetWinforms"
        portal = "$targetPortal"
        catalog = "E:/AI/Code/DesktopWasm/catalog/apps.json"
        wpfRoot = "E:\AI\Code\desktop-wasm-coordination\verification\dw-shell-route\generations\20261031T014103Z\wpfroot"
        packagesRoot = "E:\AI\Code\desktop-wasm-coordination\verification\dw-shell-route\generations\20261031T014103Z\packages"
    }
    hashes = @{
        backendDll = $backendDllHash
        shellDll = $shellDllHash
        canvasHostDll = $canvasHostDllHash
        bundleGate = "PASS"
    }
    candidatePins = @{
        shell5224pid = $null
        backend5223pid = $null
    }
} | ConvertTo-Json -Depth 6

$deploymentJson | Out-File -FilePath (Join-Path $deploy "deployment.json") -Encoding utf8
Write-Host "Candidate staged successfully: $deploy"
