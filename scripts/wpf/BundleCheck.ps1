# Bundle consistency gate for the facade-shadow invariant (adapted from
# winforms-wasm check-bundle.ps1, DesktopWasm scope):
# every fingerprinted .wasm the boot manifest references must exist on disk,
# and IF <AssemblyName>.*.wasm is in the bundle it must be the FULL assembly
# (>= MinFullBytes, integrity matches blazor.boot.json, DependencyObject present).
# Absence is PASS with a note: the DesktopWasm engine is facade-free by design
# (clean-room types, no System.Windows references), so there is nothing to swap.
# Use -StagedDll to also fail when a staged full copy was not bundled (desync).
# Usage: Check-BundleWindowsBase.ps1 [-Root <wwwroot/_framework>] [-AssemblyName WindowsBase]
#        [-StagedDll <path>] [-MinFullBytes 102400]
param(
    [string]$Root = "src/DesktopWasm.BrowserHost/bin/Debug/net9.0/wwwroot/_framework",
    [string]$AssemblyName = "WindowsBase",
    [string]$StagedDll = "",
    [long]$MinFullBytes = 102400
)
$ErrorActionPreference = 'Stop'
if (![IO.Path]::IsPathRooted($Root)) { $Root = Join-Path (Get-Location) $Root }
$failed = $false

$bootPath = Join-Path $Root 'blazor.boot.json'
if (!(Test-Path $bootPath)) { Write-Host "  MISSING blazor.boot.json in $Root"; exit 1 }
$boot = Get-Content $bootPath -Raw
$names = [regex]::Matches($boot, '"([A-Za-z0-9_.-]+\.[a-z0-9]{8,}\.wasm)"') |
    ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
$missing = @($names | Where-Object { !(Test-Path (Join-Path $Root $_)) })
Write-Host "referenced=$($names.Count) missing=$($missing.Count)"
$missing | ForEach-Object { Write-Host "  MISSING $_"; $failed = $true }

$manifest = $boot | ConvertFrom-Json
$bundled = @(Get-ChildItem (Join-Path $Root "$AssemblyName.*.wasm") -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notlike '*.gz' })
if ($bundled.Count -eq 0) {
    if ($StagedDll -ne "" -and (Test-Path $StagedDll)) {
        Write-Host "  FAIL: staged full $AssemblyName was not bundled (desync)"; $failed = $true
    }
    else { Write-Host "$AssemblyName absent from bundle (facade-free engine OK)" }
}
foreach ($wb in $bundled) {
    Write-Host "$AssemblyName`: $($wb.Name) $($wb.Length) bytes"
    if ($wb.Length -lt $MinFullBytes) { Write-Host '  FAIL: facade-sized - full copy missing'; $failed = $true }

    $entry = $manifest.resources.assembly.PSObject.Properties[$wb.Name]
    if ($null -eq $entry) { Write-Host '  FAIL: no blazor.boot.json integrity entry'; $failed = $true }
    else {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $actual = 'sha256-' + [Convert]::ToBase64String($sha.ComputeHash([IO.File]::ReadAllBytes($wb.FullName))) }
        finally { $sha.Dispose() }
        if ($actual -ne $entry.Value) { Write-Host "  FAIL: integrity mismatch manifest=$($entry.Value) actual=$actual"; $failed = $true }
        else { Write-Host '  integrity OK' }
    }

    $text = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($wb.FullName))
    if ($text -like '*DependencyObject*') { Write-Host '  DependencyObject present' }
    else { Write-Host '  FAIL: System.Windows.DependencyObject absent (facade shadowed the full copy)'; $failed = $true }
}

if ($failed) { exit 1 }
Write-Host 'bundle gate PASS'
