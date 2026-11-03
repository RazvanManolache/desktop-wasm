# Resolve-LocalPython.ps1 — single shared resolver for the project-local
# standalone CPython. No fallback to global python, no PATH mutation.
# Usage: $LocalPython = & (Join-Path $PSScriptRoot 'Resolve-LocalPython.ps1')
# Throws (non-zero exit via caller $ErrorActionPreference) when unusable.

$ErrorActionPreference = 'Stop'

# Repo root = parent of scripts/, independent of CWD.
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\\..')).Path
$LocalPython = Join-Path $RepoRoot '.tools\\python314\\python.exe'

if (-not (Test-Path $LocalPython -PathType Leaf)) {
    throw "Project-local Python not found at '$LocalPython'. See .tools/python314/BUILD-RECEIPT.txt for bootstrap/recovery."
}

# Isolate from ambient interpreter configuration. Local tree carries its own
# stdlib + curated site-packages; never inherit global/user Python state.
$env:PYTHONNOUSERSITE = '1'
$env:PYTHONHOME = $null
$env:PYTHONPATH = $null
Remove-Item Env:\\PYTHONHOME -ErrorAction SilentlyContinue
Remove-Item Env:\\PYTHONPATH -ErrorAction SilentlyContinue

# Prove the binary answers as itself (version gate, honest failure otherwise).
$verOut = & $LocalPython --version 2>&1
if ($LASTEXITCODE -ne 0 -or -not ("$verOut" -match '^Python 3\.14\.')) {
    throw "Project-local Python failed version gate: '$verOut'."
}

$LocalPython
