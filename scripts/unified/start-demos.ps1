#Requires -Version 5.1
<#
.SYNOPSIS
Prepare complete clients and start both demo servers on loopback :5220/:5230.
Uses a pinned WPF generation and copies build payloads into a NEW deployment.
Never stops a listener. -PrepareOnly validates/stages without starting servers.
#>
param(
    [string]$WpfRoot,
    [string]$PackagesRoot,
    [string]$CoordinationRoot = 'E:/AI/Code/desktop-wasm-coordination',
    [switch]$NoBuild,
    [switch]$PrepareOnly,
    [switch]$ShellOnly
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $WpfRoot) {
    $generations = Join-Path $CoordinationRoot 'verification/dw-shell-route/generations'
    $generation = Get-ChildItem -LiteralPath $generations -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName 'wpfroot/_framework/blazor.boot.json') } |
        Sort-Object Name -Descending | Select-Object -First 1
    if (-not $generation) { throw 'No staged WPF generation. Pass -WpfRoot explicitly.' }
    $WpfRoot = Join-Path $generation.FullName 'wpfroot'
}
$WpfRoot = (Resolve-Path -LiteralPath $WpfRoot).Path
if (-not $PackagesRoot) { $PackagesRoot = Join-Path (Split-Path $WpfRoot -Parent) 'packages' }
if (-not $PrepareOnly) {
    $ports = if ($ShellOnly) { @(5220) } else { @(5220,5230) }
    $occupied = @(Get-NetTCPConnection -State Listen -LocalPort $ports -ErrorAction SilentlyContinue)
    if ($occupied.Count) { throw 'Demo ports are already in use. Existing servers were left running.' }
    if ($ShellOnly) {
        $existingBackend = Invoke-RestMethod 'http://127.0.0.1:5230/api/preview-config' -TimeoutSec 5
        if (-not $existingBackend.apps) { throw 'Shell-only start requires the existing WinForms backend on :5230.' }
    }
}
if (-not $NoBuild) {
    # The existing coordinator lease remains the single shared-output writer gate.
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $CoordinationRoot 'tools/with-build-lock.ps1') -Repo DesktopWasm -Command (Join-Path $PSScriptRoot 'build-demos.ps1')
    if ($LASTEXITCODE -ne 0) { throw "Demo build failed (exit $LASTEXITCODE)." }
}
$deploy = Join-Path $repoRoot ('.artifacts/demo-deployments/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
New-Item -ItemType Directory -Path $deploy -Force | Out-Null

function Stage-Client([string]$Project, [string]$Destination) {
    $source = Join-Path $repoRoot "src/$Project"
    $built = Join-Path $source 'bin/Debug/net9.0/wwwroot'
    if (-not (Test-Path "$built/_framework/blazor.boot.json")) { throw "Build payload missing for $Project" }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item "$source/wwwroot/*" $Destination -Recurse -Force
    Copy-Item "$built/*" $Destination -Recurse -Force
    $css = Join-Path $source "obj/Debug/net9.0/scopedcss/bundle/$Project.styles.css"
    if (Test-Path -LiteralPath $css) { Copy-Item -LiteralPath $css -Destination $Destination -Force }
}
$portal = Join-Path $deploy 'portal'
$winforms = Join-Path $deploy 'winforms'
Stage-Client 'DesktopWasm.Web' $portal
Stage-Client 'WfWasm.CanvasHost' $winforms

function Check-Boot([string]$Root) {
    foreach ($file in @('index.html', '_framework/blazor.webassembly.js', '_framework/blazor.boot.json')) {
        if (-not (Test-Path (Join-Path $Root $file) -PathType Leaf)) { throw "Missing $Root/$file" }
    }
    $manifest = Get-Content (Join-Path $Root '_framework/blazor.boot.json') -Raw | ConvertFrom-Json
    function Check-Group($Group, [string]$Directory) {
        foreach ($item in $Group.PSObject.Properties) {
            if ($item.Value -is [System.Management.Automation.PSCustomObject]) {
                Check-Group $item.Value (Join-Path $Directory $item.Name)
            } elseif ($item.Value -is [string] -and $item.Value.StartsWith('sha256-')) {
                $path = Join-Path $Directory $item.Name
                if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing boot payload $path" }
                $hasher = [Security.Cryptography.SHA256]::Create()
                try { $digest = [Convert]::ToBase64String($hasher.ComputeHash([IO.File]::ReadAllBytes($path))) }
                finally { $hasher.Dispose() }
                if (('sha256-' + $digest) -cne $item.Value) { throw "Boot integrity mismatch: $path" }
            }
        }
    }
    foreach ($group in $manifest.resources.PSObject.Properties) {
        if ($group.Name -ne 'fingerprinting' -and $group.Value -is [System.Management.Automation.PSCustomObject]) {
            Check-Group $group.Value (Join-Path $Root '_framework')
        }
    }
}
Check-Boot $portal
Check-Boot $winforms
Check-Boot $WpfRoot
if (-not (Test-Path "$winforms/WfWasm.CanvasHost.styles.css")) { throw 'WinForms CSS bundle missing.' }

# Copy server outputs as well: a later build must not replace live assemblies.
foreach ($project in @('DesktopWasm.Host.Server', 'WfWasm.Host.Server')) {
    $target = Join-Path $deploy $project
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Copy-Item (Join-Path $repoRoot "src/$project/bin/Debug/net9.0/*") $target -Recurse -Force
}
$state = [ordered]@{ deployment=$deploy; wpfRoot=$WpfRoot; packagesRoot=$PackagesRoot; portal=$portal; winforms=$winforms; createdUtc=[DateTime]::UtcNow.ToString('o') }
$state | ConvertTo-Json | Set-Content (Join-Path $deploy 'deployment.json') -Encoding UTF8
Write-Host "Complete deployment validated: $deploy"
if ($PrepareOnly) { return }

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
function Start-Host([string]$Project, [int]$Port, [string]$Root) {
    $arguments = @((Join-Path $deploy "$Project/$Project.dll"), '--urls', "http://127.0.0.1:$Port", '--webroot', $Root, '--Catalog:Path', (Join-Path $repoRoot 'catalog/apps.json'))
    $quoted = $arguments | ForEach-Object { '"' + $_ + '"' }
    Start-Process -FilePath $dotnet -ArgumentList $quoted -WorkingDirectory $repoRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $deploy "$Port.log") -RedirectStandardError (Join-Path $deploy "$Port.err.log") -PassThru
}
$saved = @{}
$backend = $null
$shell = $null
function Stop-StartedHosts {
    # These are the exact Process objects returned by this invocation. Never
    # find/stop by port or process name, and keep all failure logs intact.
    foreach ($started in @($backend, $shell)) {
        if ($started -and -not $started.HasExited) { $started.Kill() }
    }
}
foreach ($name in @('DESKTOPWASM_WPFROOT','DESKTOPWASM_PACKAGES','DESKTOPWASM_WFBACKEND','DESKTOPWASM_WEBROOT','WFWASM_BENCHMARK_APPS_ROOT')) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $env:DESKTOPWASM_WPFROOT = $WpfRoot
    $env:DESKTOPWASM_PACKAGES = $PackagesRoot
    $env:DESKTOPWASM_WFBACKEND = 'http://127.0.0.1:5230'
    $env:DESKTOPWASM_WEBROOT = $portal
    if (-not $env:WFWASM_BENCHMARK_APPS_ROOT) {
        $env:WFWASM_BENCHMARK_APPS_ROOT = Join-Path $repoRoot '.artifacts/benchmark-apps'
    }
    if (-not $ShellOnly) {
        $backend = Start-Host 'WfWasm.Host.Server' 5230 $winforms
        $state['backendPid'] = $backend.Id
    } else {
        $state['backendPid'] = (Get-NetTCPConnection -State Listen -LocalPort 5230 | Select-Object -First 1).OwningProcess
        $state['backendReused'] = $true
    }
    $shell = Start-Host 'DesktopWasm.Host.Server' 5220 $portal
    $state['shellPid'] = $shell.Id
    $state | ConvertTo-Json | Set-Content (Join-Path $deploy 'deployment.json') -Encoding UTF8
} catch {
    Stop-StartedHosts
    throw
} finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
}
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    if (($backend -and $backend.HasExited) -or $shell.HasExited) {
        Stop-StartedHosts
        throw "Demo process exited during startup. Inspect $deploy/*.err.log; exact owned PIDs are in deployment.json."
    }
    try {
        $ready = Invoke-RestMethod 'http://127.0.0.1:5220/api/static-preflight' -TimeoutSec 2
        $loader = Invoke-WebRequest 'http://127.0.0.1:5230/_framework/blazor.webassembly.js' -UseBasicParsing -TimeoutSec 2
        if ($ready.ok -and $loader.StatusCode -eq 200) {
            Write-Host 'Demos running: http://127.0.0.1:5220/ (WinForms /app/smoke, WPF /demo/controls)'
            return
        }
    } catch { }
    Start-Sleep -Milliseconds 500
}
Stop-StartedHosts
throw "Deployment did not become ready. Inspect $deploy/*.log; exact owned PIDs are in deployment.json."
