# Audit package manifest integrity and hash consistency across all 14 sample apps
param(
  [string]$DesktopWasmRoot = "E:\AI\Code\DesktopWasm"
)

$ErrorActionPreference = 'Stop'
$catalogPath = Join-Path $DesktopWasmRoot "catalog\apps.json"
$snapshotPath = Join-Path $DesktopWasmRoot "catalog\sources\wpf-manifests.snapshot.json"
$appsDir = Join-Path $DesktopWasmRoot "src\DesktopWasm.BrowserHost\wwwroot\apps"

if (-not (Test-Path $catalogPath)) { throw "Catalog not found at $catalogPath" }
if (-not (Test-Path $snapshotPath)) { throw "Snapshot not found at $snapshotPath" }

$catalog = Get-Content $catalogPath -Raw | ConvertFrom-Json
$snapshot = Get-Content $snapshotPath -Raw | ConvertFrom-Json

$slugs = @(
  "avalondock", "avalonedit", "geometry", "handycontrol", "hello-real",
  "ilspy", "mahapps", "material-design", "oxyplot", "pc-space-map",
  "screen-to-gif", "simple-binding", "wpf-calculator", "wpf-gallery"
)

$audit = @()
$totalFiles = 0
$matchedFiles = 0
$mismatches = 0
$missing = 0

foreach ($slug in $slugs) {
  $catApp = $catalog.apps | Where-Object { $_.id -eq $slug }
  $snapApp = $snapshot.data.entries | Where-Object { $_.slug -eq $slug }
  $appFolder = Join-Path $appsDir $slug
  $manifestPath = Join-Path $appFolder "app-manifest.json"

  if (-not (Test-Path $manifestPath)) {
    $audit += [PSCustomObject]@{
      Slug = $slug; Status = "MISSING_MANIFEST"; Files = 0; Matched = 0; Errors = 1
    }
    $missing++
    continue
  }

  $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
  $manifestSha = (Get-FileHash $manifestPath -Algorithm SHA256).Hash
  $snapShaMatches = ($snapApp -and ($snapApp.manifestSha256 -eq $manifestSha))

  $appFiles = 0
  $appMatched = 0
  $appErrors = @()

  # Check catalog package object vs manifest
  if (-not $catApp -or -not $catApp.package) {
    $appErrors += "Missing package definition in catalog"
  } elseif ($catApp.package.status -ne $manifest.status) {
    $appErrors += "Status mismatch (cat: $($catApp.package.status), man: $($manifest.status))"
  }

  # Verify each assembly file
  if ($manifest.assemblies) {
    foreach ($asm in $manifest.assemblies) {
      $appFiles++
      $totalFiles++
      $filePath = Join-Path $appFolder $asm.file
      if (-not (Test-Path $filePath)) {
        $appErrors += "Missing file: $($asm.file)"
        $missing++
      } else {
        $actualSha = (Get-FileHash $filePath -Algorithm SHA256).Hash
        if ($actualSha -eq $asm.sha256) {
          $appMatched++
          $matchedFiles++
        } else {
          $appErrors += "Hash mismatch for $($asm.file)"
          $mismatches++
        }
      }
    }
  }

  $audit += [PSCustomObject]@{
    Slug = $slug
    Status = $manifest.status
    Entry = $manifest.entryAssembly
    Files = $appFiles
    Matched = $appMatched
    SnapShaMatch = $snapShaMatches
    Errors = $appErrors.Count
  }
}

$audit | Format-Table -AutoSize
Write-Host "SUMMARY: $($slugs.Count) apps audited. Total assemblies: $totalFiles. Hashes matched: $matchedFiles. Mismatches: $mismatches. Missing files: $missing."

if ($mismatches -gt 0 -or $missing -gt 0) {
  exit 1
}
exit 0
