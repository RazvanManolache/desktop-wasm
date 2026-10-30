# Emits a tab-separated manifest: relative_path, bytes, sha256.
# Used to prove source stability before/after copy (diff manifests).
param(
  [string]$Root = (Split-Path -Parent $PSScriptRoot | Split-Path -Parent),
  [string]$OutFile = '',
  [string[]]$ExcludeDir = @('bin', 'obj', '.git', '__pycache__', '.vs', 'TestResults')
)
$ErrorActionPreference = 'Stop'
$rootFull = (Resolve-Path $Root).Path
$files = Get-ChildItem $rootFull -Recurse -File -Force | Where-Object {
  $full = $_.FullName
  foreach ($d in $ExcludeDir) { if ($full -like "*\$d\*") { return $false } }
  if ($_.Name -like '*.user' -or $_.Name -like '*.suo') { return $false }
  $true
} | Sort-Object FullName
$lines = foreach ($f in $files) {
  $rel = $f.FullName.Substring($rootFull.Length + 1)
  $h = (Get-FileHash -Algorithm SHA256 -Path $f.FullName).Hash
  "$rel`t$($f.Length)`t$h"
}
if ($OutFile -ne '') { $lines | Out-File -FilePath $OutFile -Encoding utf8 }
else { $lines }
Write-Output ("MANIFEST-COUNT=" + $lines.Count)
