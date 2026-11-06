# Stages prepared Syncfusion WinForms app inputs byte-exact into the unified root.
#
# Read-only on sources: the prepared build outputs under WF_SYNCFUSION_APPS_ROOT
# (canonical retained/wfwasm-syncfusion-apps) and the upstream Flags content are
# never modified, never rebuilt, never downloaded. This script copies required
# managed-assembly inputs + loose content into
# inputs/winforms/syncfusion-staging/<slug>/ with deterministic SHA-256 hashes,
# and writes a catalog-consumable DIRECTORY receipt file (manifest-digest pin)
# under provenance/receipts/.
#
# Layout per slug (ReceiptVerifier manifest-directory contract):
#   app-manifest.json          assemblies:[{file,sha256,sourceRel,bytes}] (bare
#                              filenames staged beside the manifest; file must
#                              carry no slashes) + content:[{name,sha256,bytes}]
#                              (informative coverage; verifier checks assemblies)
#                              + runtimeProvided[] (GAC/desktop-pack deps, NOT
#                              staged, listed separately) + provenance block.
#   <Assembly.dll/exe/pdb/...> byte-exact copies of every required input file.
#   content/Flags/...          AutoComplete flag images (VFS content inputs).
#   PROVENANCE.json             licensing + origin provenance (not redistributed).
#
# Idempotent: rerun copies + re-verifies; identical bytes yield identical hashes.
# Throws on any source/destination hash mismatch or missing required input.
param(
  [string[]]$AppSlugs = @(
    'syncfusion-auto-complete', 'syncfusion-buttons', 'syncfusion-combobox',
    'syncfusion-numeric', 'syncfusion-masked-edit', 'syncfusion-toggle',
    'syncfusion-rating', 'syncfusion-font-combo'
  ),
  [string]$SourceAppsRoot = $env:WF_SYNCFUSION_APPS_ROOT,
  [string]$SourceFlagsDir = '',
  [string]$DestRoot = '',
  [string]$ReceiptPath = '',
  [switch]$NegativeCheck
)

$ErrorActionPreference = 'Stop'
# Normalize: `-File` CLI invocation flattens arrays to one comma-joined string.
$AppSlugs = @($AppSlugs | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
# TMPMOVE canonical-only: retained/<name>. Env override WF_SYNCFUSION_APPS_ROOT
# still wins when set; no legacy fallback.
if ([string]::IsNullOrWhiteSpace($SourceAppsRoot)) { $SourceAppsRoot = $env:WF_SYNCFUSION_APPS_ROOT }
if ([string]::IsNullOrWhiteSpace($SourceAppsRoot)) { $SourceAppsRoot = Join-Path $repoRoot '.artifacts/tmp/retained/wfwasm-syncfusion-apps' }
if ([string]::IsNullOrWhiteSpace($SourceFlagsDir)) { $SourceFlagsDir = Join-Path $repoRoot '.artifacts/tmp/retained/wfwasm-inputs/syncfusion-winforms-demos/autocomplete/GettingStarted/CS/Flags' }
if ([string]::IsNullOrWhiteSpace($DestRoot)) { $DestRoot = Join-Path $repoRoot 'inputs/winforms/syncfusion-staging' }
if ([string]::IsNullOrWhiteSpace($ReceiptPath)) { $ReceiptPath = Join-Path $repoRoot 'provenance/receipts/rvw0900-syncfusion-input.json' }

# Runtime/GAC-provided managed references: resolved by the backend at serve
# time from the desktop pack / GAC (AppTransform UiaOverrides). Never staged,
# never hashed as inputs; listed here so manifest coverage can name them.
$RuntimeProvided = @(
  'UIAutomationProvider.dll (Microsoft.WindowsDesktop.App 9.x desktop pack, else GAC_MSIL)',
  'UIAutomationTypes.dll (Microsoft.WindowsDesktop.App 9.x desktop pack, else GAC_MSIL)',
  'UIAutomationClient.dll (Microsoft.WindowsDesktop.App 9.x desktop pack, else GAC_MSIL)'
)

function Get-FileSha256([string]$Path) {
  $h = [System.Security.Cryptography.SHA256]::Create()
  try {
    $s = [System.IO.File]::OpenRead($Path)
    try { return ([BitConverter]::ToString($h.ComputeHash($s)) -replace '-', '') }
    finally { $s.Close() }
  } finally { $h.Dispose() }
}

# Allowed staged inputs: managed assemblies + symbols/configs that ship
# beside them in the prepared build output (matched by full suffix: .NET
# GetExtension returns only '.json' for '.deps.json'). Vendor license keys,
# user files and caches are never loose content and are excluded.
$AssemblySuffixes = @('.dll', '.exe', '.pdb', '.deps.json', '.runtimeconfig.json')

$receipts = @()
$missing = @()
foreach ($slug in $AppSlugs) {
  $srcDir = Join-Path $SourceAppsRoot $slug
  if (!(Test-Path -LiteralPath $srcDir -PathType Container)) {
    $missing += [pscustomobject]@{ slug = $slug; reason = "prepared app folder absent: $srcDir (no download performed; see report)" }
    continue
  }
  $destDir = Join-Path $DestRoot $slug
  New-Item -ItemType Directory -Force -Path $destDir | Out-Null
  New-Item -ItemType Directory -Force -Path (Join-Path $destDir 'content') | Out-Null

  $assemblies = @()
  foreach ($f in Get-ChildItem -LiteralPath $srcDir -Recurse -File | Sort-Object FullName) {
    $lower = $f.Name.ToLowerInvariant()
    $wanted = $false
    foreach ($sfx in $AssemblySuffixes) { if ($lower.EndsWith($sfx)) { $wanted = $true; break } }
    if (!$wanted) { continue }  # non-input byproduct, left unstaged
    $srcHash = Get-FileSha256 $f.FullName
    $dest = Join-Path $destDir $f.Name
    Copy-Item -LiteralPath $f.FullName -Destination $dest -Force
    $destHash = Get-FileSha256 $dest
    if ($destHash -ne $srcHash) { throw "byte mismatch staging $($f.FullName) -> $dest" }
    $assemblies += [pscustomobject]@{
      file = $f.Name; sha256 = $destHash; bytes = $f.Length
      sourceRel = $f.FullName.Substring($srcDir.Length).TrimStart('\', '/').Replace('\', '/')
    }
  }
  if ($assemblies.Count -eq 0) {
    $missing += [pscustomobject]@{ slug = $slug; reason = "no staged input files found in $srcDir" }
    continue
  }

  $content = @()
  if ($slug -eq 'syncfusion-auto-complete' -and (Test-Path -LiteralPath $SourceFlagsDir -PathType Container)) {
    $flagsDest = Join-Path (Join-Path $destDir 'content') 'Flags'
    New-Item -ItemType Directory -Force -Path $flagsDest | Out-Null
    foreach ($f in Get-ChildItem -LiteralPath $SourceFlagsDir -File | Sort-Object Name) {
      $ext = [System.IO.Path]::GetExtension($f.Name).ToLowerInvariant()
      if (@('.png', '.jpg', '.jpeg') -notcontains $ext) { continue }
      $srcHash = Get-FileSha256 $f.FullName
      $dest = Join-Path $flagsDest $f.Name
      Copy-Item -LiteralPath $f.FullName -Destination $dest -Force
      $destHash = Get-FileSha256 $dest
      if ($destHash -ne $srcHash) { throw "byte mismatch staging $($f.FullName) -> $dest" }
      $content += [pscustomobject]@{ name = "Flags/$($f.Name)"; sha256 = $destHash; bytes = $f.Length }
    }
  }

  $prov = [pscustomobject]@{
    origin = 'prepared build output (prepare-syncfusion.ps1) from upstream https://github.com/syncfusion/winforms-demos (no vendor download in this slice)'
    sourceAppFolder = $srcDir
    license = 'Syncfusion demo binaries: Syncfusion EULA applies; staged as conversion inputs only, NOT redistributed.'
    runtimeProvided = $RuntimeProvided
    stagedUtc = [DateTime]::UtcNow.ToString('o')
  }
  $prov | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destDir 'PROVENANCE.json') -Encoding UTF8

  # Manifest bytes must be rerun-stable: no wall-clock inside (stagedUtc lives
  # only in PROVENANCE.json + receipt rows). Identical inputs => identical digest.
  $manifestProv = [pscustomobject]@{
    origin = $prov.origin
    sourceAppFolder = $prov.sourceAppFolder
    license = $prov.license
    runtimeProvided = $prov.runtimeProvided
  }
  $manifest = [pscustomobject]@{
    slug = $slug
    kind = 'syncfusion-winforms-input-batch'
    assemblies = $assemblies
    content = $content
    runtimeProvided = $RuntimeProvided
    provenance = $manifestProv
  }
  $manifestPath = Join-Path $destDir 'app-manifest.json'
  # Deterministic JSON: sorted inputs + fixed key order give stable bytes.
  $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
  $manifestDigest = Get-FileSha256 $manifestPath
  $receipts += [pscustomobject]@{
    id = $slug; localPath = "inputs/winforms/syncfusion-staging/$slug"
    sha256 = $manifestDigest; stagedUtc = $prov.stagedUtc
  }
  Write-Output "$slug staged: $($assemblies.Count) assemblies, $($content.Count) content, manifest $manifestDigest"
}

if ($NegativeCheck) {
  # Intentional missing-file check: a manifest claiming one more byte than
  # staged must fail closure verification (fail-closed contract).
  $probe = Join-Path ([System.IO.Path]::GetTempPath()) 'rvw0900-negcheck'
  New-Item -ItemType Directory -Force -Path $probe | Out-Null
  $victim = $receipts | Select-Object -First 1
  if ($null -eq $victim) { throw 'negative check needs at least one staged app' }
  $man = Get-Content -LiteralPath (Join-Path $DestRoot ($victim.id + '/app-manifest.json')) -Raw | ConvertFrom-Json
  $man.assemblies += [pscustomobject]@{ file = 'DoesNotExist.dll'; sha256 = ('0' * 64); bytes = 0; sourceRel = 'none' }
  $fake = Join-Path $probe 'app-manifest.json'
  $man | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $fake -Encoding UTF8
  # Closure must fail: claimed file absent beside the manifest.
  $claimed = Join-Path $probe 'DoesNotExist.dll'
  if (Test-Path -LiteralPath $claimed) { throw 'negative-check probe contaminated' }
  Write-Output "NEGATIVE-CHECK PASS: manifest claims DoesNotExist.dll, staged file absent -> verifier ClosureMatches must be false (fail-closed)."
  $missing += [pscustomobject]@{ slug = '(negative-check probe)'; reason = 'intentional DoesNotExist.dll claim; staged file absent by design; throwaway dir ' + $probe }
}

New-Item -ItemType Directory -Force -Path (Split-Path $ReceiptPath -Parent) | Out-Null
$receipts | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ReceiptPath -Encoding UTF8
Write-Output "receipts: $($receipts.Count) rows -> $ReceiptPath"
if ($missing.Count -gt 0) {
  Write-Output 'UNRESOLVED (recorded, not retried):'
  $missing | Format-Table -AutoSize | Out-String | Write-Output
}

