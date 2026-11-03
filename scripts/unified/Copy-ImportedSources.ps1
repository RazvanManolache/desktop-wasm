# Copies first-party import trees from the frozen original roots into this
# unified workspace. Read-only on sources: robocopy copy (never move/delete),
# no hardlinks/junctions back to mutable originals.
#
# Layout (project/assembly names preserved, relative references intact):
#   src/                     <- WindowsForms/src + WPF-wasm/src (disjoint names)
#   tests/                   <- WindowsForms/tests + WPF-wasm/tests
#   scripts/winforms|wpf     <- partitioned per source (prevents overwrite)
#   docs/legacy/winforms|wpf <- partitioned per source
#   docs/migration/legacy-solutions/ <- original solution files (reference only)
#
# Excludes: bin, obj, .git, __pycache__, .vs, TestResults (disposable outputs).
# Intentional staged binaries under src/tests (ConvertedApps DLLs,
# wwwroot/apps *.bin) are NOT under bin/obj and are preserved.
# The 40 zero-byte Slice*.cs stubs at the WPF root are excluded (see COPY-REPORT).

param(
  [string]$WinformsRoot = '',
  [string]$WpfRoot = '',
  [string]$DestRoot = (Split-Path -Parent $PSScriptRoot | Split-Path -Parent)
)
# Portability: source roots have no portable default; pass both
# explicitly (e.g. -WinformsRoot <path> -WpfRoot <path>).
if ([string]::IsNullOrWhiteSpace($WinformsRoot) -or [string]::IsNullOrWhiteSpace($WpfRoot)) {
  throw 'Copy-ImportedSources requires explicit -WinformsRoot and -WpfRoot (no portable default).'
}

$ErrorActionPreference = 'Stop'
function Copy-Tree($Source, $Dest) {
  New-Item -ItemType Directory -Force -Path $Dest | Out-Null
  robocopy $Source $Dest /E /XD bin obj .git __pycache__ .vs TestResults `
    /XF *.user *.suo /R:2 /W:1 /NP /NDL /NJH /NJS /BYTES
  # robocopy exit 0-3 = success (0 none, 1 copied, 2 extra, 3 copied+extra);
  # >=8 = real failure.
  if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE): $Source -> $Dest" }
  Write-Output "robocopy exit=${LASTEXITCODE}: $Source -> $Dest"
}

# NOTE: never name a local function Cp/Copy/Del/Move - single-name aliases
# (cp = Copy-Item) shadow functions and silently do the wrong thing.

Copy-Tree (Join-Path $WinformsRoot 'src') (Join-Path $DestRoot 'src')
Copy-Tree (Join-Path $WpfRoot 'src') (Join-Path $DestRoot 'src')
Copy-Tree (Join-Path $WinformsRoot 'tests') (Join-Path $DestRoot 'tests')
Copy-Tree (Join-Path $WpfRoot 'tests') (Join-Path $DestRoot 'tests')
Copy-Tree (Join-Path $WinformsRoot 'scripts') (Join-Path $DestRoot 'scripts\winforms')
Copy-Tree (Join-Path $WpfRoot 'scripts') (Join-Path $DestRoot 'scripts\wpf')
Copy-Tree (Join-Path $WinformsRoot 'docs') (Join-Path $DestRoot 'docs\legacy\winforms')
Copy-Tree (Join-Path $WpfRoot 'docs') (Join-Path $DestRoot 'docs\legacy\wpf')

$solDir = Join-Path $DestRoot 'docs\migration\legacy-solutions'
New-Item -ItemType Directory -Force -Path $solDir | Out-Null
Copy-Item (Join-Path $WinformsRoot 'WfWasm.sln') (Join-Path $solDir 'WfWasm.sln') -Force
Copy-Item (Join-Path $WpfRoot 'DesktopWasm.slnx') (Join-Path $solDir 'DesktopWasm.slnx') -Force
Write-Output 'legacy solution copies done'
