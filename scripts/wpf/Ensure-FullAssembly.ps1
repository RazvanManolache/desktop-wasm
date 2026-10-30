# Stages a FULL implementation assembly for the browser bundle.
#
# Background (same trick as winforms-wasm PrepareFullWindowsBase): ResolveAssemblyReferences
# resolves framework references (e.g. WindowsBase) to the browser-wasm runtime-pack facade
# (~6KB WebCIL). Mono loads that facade into the default ALC first, so a real copy fetched
# later on demand can never displace it and bootstrap dies with TypeLoadException
# (in winforms-wasm: token 01000023, System.Windows.DependencyObject missing).
# Staging the full copy here lets the UseFullWindowsBase target swap it in before the
# bundle is computed, so WebCIL conversion, fingerprinting and boot.json integrity
# are all produced by the pipeline itself, never hand-edited.
#
# Differences from winforms-wasm (deliberate):
# - Generic: -AssemblyName / -SourceDir parametrize the assembly (WindowsBase is wired
#   in DesktopWasm.BrowserHost.csproj as the canonical example for the future merge).
# - No rewriter-tool dependency: optional -TransformPasses run "<exe> <args>" lines where
#   {file} is replaced with the working copy (winforms-wasm passes --stub-pinvokes,
#   --patch-probes, --refonly from its WfWasm.Rewrite.Tool here).
# - Fail-soft discovery: when -SourceDir is omitted and no full copy is installed,
#   staging is SKIPPED with a warning (exit 0). The DesktopWasm engine is facade-free
#   by design (clean-room types, no System.Windows references), so staging here is
#   merge preparation, not load-bearing. An explicit -SourceDir that is missing or
#   lacks the assembly is still a hard error, as in winforms-wasm.
#
# Usage: Ensure-FullAssembly.ps1 [-AssemblyName WindowsBase] [-SourceDir <dir>]
#        [-IntermediateDir <obj/Debug/net9.0>] [-TransformPasses <string[]>]
param(
    [string]$AssemblyName = "WindowsBase",
    [string]$SourceDir = "",
    [string]$IntermediateDir = "obj/Debug/net9.0",
    [string[]]$TransformPasses = @()
)
$ErrorActionPreference = 'Stop'
$IntermediateDir = $IntermediateDir.TrimEnd('\', '/', '"')
if (![IO.Path]::IsPathRooted($IntermediateDir)) { $IntermediateDir = Join-Path (Get-Location) $IntermediateDir }

$dllName = "$AssemblyName.dll"
if ($SourceDir -ne "") {
    if (!(Test-Path $SourceDir)) { throw "explicit source dir not found: $SourceDir" }
    $pristine = Join-Path $SourceDir $dllName
    if (!(Test-Path $pristine)) { throw "pristine $dllName not found in: $SourceDir" }
}
else {
    $pristine = $null
    $roots = @()
    if ($env:DOTNET_ROOT) { $roots += Join-Path $env:DOTNET_ROOT 'shared/Microsoft.WindowsDesktop.App' }
    $roots += 'C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App'
    foreach ($r in $roots) {
        if (!(Test-Path $r)) { continue }
        $found = Get-ChildItem $r -Directory | Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName $dllName } |
            Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($found) { $pristine = $found; break }
    }
    if ($null -eq $pristine) {
        Write-Host "WARNING: no full $dllName installed; skipping full-copy staging (facade-free engine, nothing to swap)."
        return
    }
}

$outDir = Join-Path $IntermediateDir 'full-assembly'
$outDll = Join-Path $outDir $dllName
$sidecar = $outDll + '.pristine.sha256'

$sha = [Security.Cryptography.SHA256]::Create()
try { $pristineHash = [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($pristine))).Replace('-', '') }
finally { $sha.Dispose() }
$stageRev = "$pristineHash|passes:$($TransformPasses.Count)"
if ((Test-Path $outDll) -and (Test-Path $sidecar) -and ((Get-Content $sidecar -Raw).Trim() -eq $stageRev)) {
    Write-Host "Full $AssemblyName copy up to date ($outDll)"
    return
}

$tmp = Join-Path ([IO.Path]::GetTempPath()) ('fullasm-' + [Guid]::NewGuid().ToString('N') + '.dll')
try {
    Copy-Item $pristine $tmp -Force
    foreach ($pass in $TransformPasses) {
        $cmd = $pass.Replace('{file}', $tmp)
        Write-Host "== transform pass: $cmd"
        Invoke-Expression $cmd
        if ($LASTEXITCODE -ne 0) { throw "transform pass failed: $cmd" }
    }
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    Copy-Item $tmp $outDll -Force
    Set-Content $sidecar $stageRev -NoNewline
    $len = (Get-Item $outDll).Length
    Write-Host "Full $AssemblyName copy staged: $outDll ($len bytes)"
}
finally {
    try { Remove-Item $tmp -Force } catch { }
}
