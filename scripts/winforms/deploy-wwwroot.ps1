# Deploy wwwroot to the file the server actually serves, then byte-verify.
# Drift here is silent and user-visible (stale css/app.css shipped the old
# top bar for a day while src had the rail). Usage:
#   powershell -File scripts/winforms/deploy-wwwroot.ps1 [-RepoRoot <root>] [-ServerRoot <wwwroot>] [-VerifyOnly]
# RepoRoot defaults to the repository root derived from THIS script's location
# (scripts/winforms/ -> two levels up); pass it explicitly for fixture staging.
# ServerRoot may be absolute, or relative (resolved against RepoRoot).
# In deploy mode the script stages ONLY into ServerRoot (a fresh dir is fine)
# and never touches live serves; VerifyOnly performs read-only checks.
param(
    [string]$RepoRoot = '',
    [string]$ServerRoot = "src/WfWasm.Host.Server/bin/Debug/net9.0/wwwroot",
    [switch]$VerifyOnly
)
$ErrorActionPreference = 'Stop'
if (-not $RepoRoot) { $RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent }
if (![IO.Path]::IsPathRooted($RepoRoot)) { throw "RepoRoot must be absolute: $RepoRoot" }
if (![IO.Path]::IsPathRooted($ServerRoot)) { $ServerRoot = Join-Path $RepoRoot $ServerRoot }
$src = Join-Path $RepoRoot 'src/WfWasm.CanvasHost/wwwroot'
$bin = Join-Path $RepoRoot 'src/WfWasm.CanvasHost/bin/Debug/net9.0/wwwroot'
$scopedBundle = Join-Path $RepoRoot 'src/WfWasm.CanvasHost/obj/Debug/net9.0/scopedcss/bundle'

if (!(Test-Path $src)) { Write-Host "  MISSING src wwwroot: $src"; exit 1 }
$fw = Join-Path $bin '_framework'
if (!(Test-Path (Join-Path $fw 'blazor.boot.json'))) { Write-Host "  MISSING compiled _framework: $fw (build CanvasHost first)"; exit 1 }
# Scoped-blazor css is generated at build time, not kept in src. Source of
# truth is the obj scopedcss bundle; the bin wwwroot copy is accepted as a
# fallback for layouts that publish it there. Served output without the
# bundle css silently ships unstyled components, so absence is an error.
$scoped = @(Get-ChildItem $scopedBundle -Filter '*.styles.css' -ErrorAction SilentlyContinue)
if ($scoped.Count -eq 0) { $scoped = @(Get-ChildItem $bin -Filter '*.styles.css' -ErrorAction SilentlyContinue) }
if ($scoped.Count -eq 0) { Write-Host "  MISSING scoped css: no *.styles.css in $scopedBundle nor $bin (build CanvasHost first)"; exit 1 }

if (!$VerifyOnly) {
    if (!(Test-Path $ServerRoot)) { New-Item -ItemType Directory -Path $ServerRoot | Out-Null }
    robocopy $fw (Join-Path $ServerRoot '_framework') /MIR /NJH /NJS | Out-Null
    # Statics come from src (css/js/eval/groundtruth/lib/...). No /MIR: the
    # server dir keeps build-generated extras (scoped styles css).
    robocopy $src $ServerRoot /E /NJH /NJS /XD _framework | Out-Null
    foreach ($css in $scoped) {
        Copy-Item $css.FullName (Join-Path $ServerRoot $css.Name) -Force
    }
    # NB: host-bundle ReflectionOnly neutralize (paint MONO Fix 3) runs at
    # BUILD time (ensure-windowsbase-full.ps1 --refonly stage), not here:
    # the browser executes the WebCIL bundle (WindowsBase.*.wasm), which the
    # pipeline fingerprints from the staged DLL — post-hoc edits would break
    # boot-manifest integrity. check-bundle (below) gates the served bytes.
}

# Byte-verify: every src static must equal the served bytes.
$failed = $false
$srcPrefix = $src.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
Get-ChildItem $src -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($srcPrefix.Length)
    $dst = Join-Path $ServerRoot $rel
    if (!(Test-Path $dst)) { Write-Host "  MISSING served: $rel"; $failed = $true; return }
    $a = [IO.File]::ReadAllBytes($_.FullName)
    $b = [IO.File]::ReadAllBytes($dst)
    if ($a.Length -ne $b.Length) { Write-Host "  SIZE-DRIFT $rel src=$($a.Length) served=$($b.Length)"; $failed = $true; return }
    for ($i = 0; $i -lt $a.Length; $i += 65536) {
        $n = [Math]::Min(65536, $a.Length - $i)
        for ($j = 0; $j -lt $n; $j++) {
            if ($a[$i + $j] -ne $b[$i + $j]) { Write-Host "  BYTE-DRIFT $rel at $i"; $failed = $true; break }
        }
        if ($failed) { break }
    }
}
# Served scoped css must exist and match a staged source byte-for-byte.
foreach ($css in $scoped) {
    $dst = Join-Path $ServerRoot $css.Name
    if (!(Test-Path $dst)) { Write-Host "  MISSING served scoped css: $($css.Name)"; $failed = $true; continue }
    $a = [IO.File]::ReadAllBytes($css.FullName)
    $b = [IO.File]::ReadAllBytes($dst)
    if ($a.Length -ne $b.Length) { Write-Host "  SIZE-DRIFT scoped $($css.Name) staged=$($a.Length) served=$($b.Length)"; $failed = $true; continue }
    $same = $true
    for ($i = 0; $i -lt $a.Length; $i++) { if ($a[$i] -ne $b[$i]) { $same = $false; break } }
    if (-not $same) { Write-Host "  BYTE-DRIFT scoped $($css.Name)"; $failed = $true }
}
if ($failed) { exit 1 }
Write-Host "wwwroot in sync: $ServerRoot"
# Bundle gate wired into deploy (was manual-only): the SERVED _framework
# must satisfy the boot/bundle invariants (fingerprinted wasm present,
# full WindowsBase, stub wasm present).
& (Join-Path $PSScriptRoot 'check-bundle.ps1') -Root (Join-Path $ServerRoot '_framework')
# NB: a .ps1 call does not set $LASTEXITCODE (it still holds robocopy's 1 =
# "files copied"); $? is the real gate signal.
if (!$?) { exit 1 }
