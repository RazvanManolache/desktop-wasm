# Regenerates the migrated fork (rewritten-small/WfWasm.WinForms.dll) from
# in-repo sources. Publication portability: the migrated copy is a build
# output, never a committed binary; this step rebuilds it when stale.
# The rewrite tool may exit nonzero while gaps remain but still writes
# --out, so freshness is judged by the output file + input stamp, not the
# exit code (same as scripts/winforms/rewrite-small.ps1).
param([string]$RepoRoot = '', [string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent }

$forkProj = Join-Path $RepoRoot 'src/WfWasm.WinForms/WfWasm.WinForms.csproj'
$drawingProj = Join-Path $RepoRoot 'src/WfWasm.Drawing/WfWasm.Drawing.csproj'
$toolProj = Join-Path $RepoRoot 'src/WfWasm.Rewrite.Tool/WfWasm.Rewrite.Tool.csproj'
foreach ($p in @($forkProj, $drawingProj)) {
  & dotnet build $p -c $Configuration --nologo -v q
  if ($LASTEXITCODE -ne 0) { throw "prerequisite build failed: $p" }
}
$forkDll = Join-Path $RepoRoot "src/WfWasm.WinForms/bin/$Configuration/net9.0/WfWasm.WinForms.dll"
$drawingDll = Join-Path $RepoRoot "src/WfWasm.Drawing/bin/$Configuration/net9.0/WfWasm.Drawing.dll"
foreach ($f in @($forkDll, $drawingDll)) { if (!(Test-Path $f)) { throw "missing prerequisite output: $f" } }

$outDir = Join-Path $RepoRoot 'artifacts-local/rewritten-small'
$out = Join-Path $outDir 'WfWasm.WinForms.dll'
$sidecar = $out + '.inputs.sha256'
$sha = [Security.Cryptography.SHA256]::Create()
try {
  $stamp = [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($forkDll))).Replace('-', '') + '|' + `
    [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($drawingDll))).Replace('-', '')
}
finally { $sha.Dispose() }
if ((Test-Path $out) -and (Test-Path $sidecar) -and ((Get-Content $sidecar -Raw).Trim() -eq $stamp)) {
  Write-Host "Migrated fork up to date ($out)"
  return
}
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
& dotnet run --project $toolProj -c $Configuration -- --app $forkDll --forms $forkDll --drawing $drawingDll --out $out 2>&1 | ForEach-Object { Write-Host "  $_" }
if (!(Test-Path $out)) { throw "fork migration produced no output: $out" }
Set-Content $sidecar $stamp -NoNewline
Write-Host "Migrated fork staged: $out"
