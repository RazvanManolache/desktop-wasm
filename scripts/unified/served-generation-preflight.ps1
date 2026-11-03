#Requires -Version 5.1
<#
.SYNOPSIS
  dw-shell owned served-generation preflight: fail visibly on missing static assets.

  File-level gate before any serve: checks the exact set the shell's
  /api/static-preflight endpoint enforces (catalog + portal index + WPF
  index/boot-manifest/entry). Exits 1 with the missing list when broken so a
  launch harness never silently starts a deployment that renders a black
  canvas or serves fallback HTML with framework MIME types.

  Labels match ShellApp.StaticPreflight so the script and the endpoint agree:
  catalog:apps.json, portal:index.html, wpf:index.html,
  wpf:_framework/blazor.boot.json, wpf:entry.mjs.
#>
param(
    [string]$RepoRoot = "E:/AI/Code/DesktopWasm",
    [string]$WpfRoot = "E:/AI/Code/desktop-wasm-coordination/verification/dw-shell-route/wpfroot",
    [string]$WebRoot = "E:/AI/Code/DesktopWasm/src/DesktopWasm.Web/wwwroot",
    [string]$CatalogPath = "E:/AI/Code/DesktopWasm/catalog/apps.json"
)

$ErrorActionPreference = "Stop"

function Test-One([string]$Label, [string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path $Path -PathType Leaf)) {
        return $Label
    }
    return $null
}

$missing = @()
$m = Test-One "catalog:apps.json" $CatalogPath; if ($m) { $missing += $m }
$m = Test-One "portal:index.html" (Join-Path $WebRoot "index.html"); if ($m) { $missing += $m }
$m = Test-One "portal:_framework/blazor.webassembly.js" (Join-Path $WebRoot "_framework/blazor.webassembly.js"); if ($m) { $missing += $m }
$m = Test-One "portal:_framework/blazor.boot.json" (Join-Path $WebRoot "_framework/blazor.boot.json"); if ($m) { $missing += $m }
if ([string]::IsNullOrWhiteSpace($WpfRoot)) {
    $missing += "wpf:index.html", "wpf:_framework/blazor.boot.json", "wpf:entry.mjs"
} else {
    $m = Test-One "wpf:index.html" (Join-Path $WpfRoot "index.html"); if ($m) { $missing += $m }
    $m = Test-One "wpf:_framework/blazor.boot.json" (Join-Path $WpfRoot "_framework/blazor.boot.json"); if ($m) { $missing += $m }
    $m = Test-One "wpf:entry.mjs" (Join-Path $WpfRoot "entry.mjs"); if ($m) { $missing += $m }
    $m = Test-One "wpf:_framework/blazor.webassembly.js" (Join-Path $WpfRoot "_framework/blazor.webassembly.js"); if ($m) { $missing += $m }
}

# The manifest can survive a partial copy while its referenced files do not.
function Find-MissingResources($Group, [string]$Directory, [string]$Label) {
    foreach ($item in $Group.PSObject.Properties) {
        if ($item.Value -is [System.Management.Automation.PSCustomObject]) {
            Find-MissingResources $item.Value (Join-Path $Directory $item.Name) ($Label + $item.Name + '/')
        } elseif ($item.Value -is [string] -and $item.Value.StartsWith('sha256-')) {
            Test-One ($Label + $item.Name) (Join-Path $Directory $item.Name)
        }
    }
}
foreach ($client in @(@{root=$WebRoot; label='portal'}, @{root=$WpfRoot; label='wpf'})) {
    if ([string]::IsNullOrWhiteSpace($client.root)) { continue }
    $framework = Join-Path $client.root '_framework'
    $manifestPath = Join-Path $framework 'blazor.boot.json'
    if (-not (Test-Path $manifestPath -PathType Leaf)) { continue }
    try {
        $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
        if (-not $manifest.resources) { throw 'Missing resources object' }
        foreach ($group in $manifest.resources.PSObject.Properties) {
            if ($group.Name -ne 'fingerprinting' -and $group.Value -is [System.Management.Automation.PSCustomObject]) {
                $missing += @(Find-MissingResources $group.Value $framework ($client.label + ':_framework/') | Where-Object { $_ })
            }
        }
    } catch { $missing += $client.label + ':_framework/blazor.boot.json (invalid or unreadable)' }
}

$result = @{
    ok = ($missing.Count -eq 0)
    missing = $missing
    wpfRoot = $WpfRoot
    webRoot = $WebRoot
    catalogPath = $CatalogPath
    utc = ([DateTime]::UtcNow.ToString("o"))
}
$result | ConvertTo-Json -Depth 4 | Write-Output
if ($missing.Count -gt 0) {
    [Console]::Error.WriteLine("served-generation preflight FAILED: missing " + ($missing -join ", "))
    exit 1
}
Write-Host "served-generation preflight OK"
exit 0
