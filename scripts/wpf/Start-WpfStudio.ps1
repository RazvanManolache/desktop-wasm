[CmdletBinding()]
param([switch]$RefreshApps, [switch]$PrepareOnly)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $repoRoot
try {
    & dotnet build DesktopWasm.slnx --nologo -v minimal --tl:off
    if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
    $inputs = @('src/DesktopWasm.Convert/bin/Debug/net9.0/DesktopWasm.Convert.dll',
        'src/DesktopWasm.Wpf/bin/Debug/net9.0/DesktopWasm.Wpf.dll',
        'src/DesktopWasm.Core/bin/Debug/net9.0/DesktopWasm.Core.dll',
        'src/DesktopWasm.Graphics/bin/Debug/net9.0/DesktopWasm.Graphics.dll',
        'scripts/Build-WpfApps.ps1', 'scripts/Prepare-WpfTargets.ps1',
        'src/DesktopWasm.BrowserHost/wwwroot/app-library.json')
    $stamp = ($inputs | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }) -join ':'
    $stampFile = 'artifacts/app-preparation/complete-stamp.txt'
    $cached = !$RefreshApps -and (Test-Path $stampFile) -and (Get-Content $stampFile -Raw).Trim() -ceq $stamp
    $apps = @(Get-Content src/DesktopWasm.BrowserHost/wwwroot/app-library.json -Raw | ConvertFrom-Json | Where-Object kind -eq external)
    $builds = @{}
    if (Test-Path artifacts/app-preparation/build-results.json) {
        Get-Content artifacts/app-preparation/build-results.json -Raw | ConvertFrom-Json | ForEach-Object { $builds[$_.slug] = $_ }
    }
    foreach ($app in $apps) {
        $root = Join-Path $repoRoot "src/DesktopWasm.BrowserHost/wwwroot/apps/$($app.slug)"
        try {
            if ($cached) {
                $source = Join-Path $repoRoot "targets/studio-inputs/$($app.slug)"
                $head = & git -C $source rev-parse HEAD 2>$null
                $changes = & git -C $source diff --name-only HEAD 2>$null
                if ($LASTEXITCODE -ne 0 -or $head -cne $app.commit -or $changes) { $cached = $false }
            }
            $manifest = Get-Content -LiteralPath (Join-Path $root 'app-manifest.json') -Raw | ConvertFrom-Json
            if ($manifest.slug -cne $app.slug -or $manifest.sourceCommit -cne $app.commit) { $cached = $false }
            if ($manifest.status -notin @('converted', 'conversion-failed')) { $cached = $false }
            foreach ($assembly in $manifest.assemblies) {
                if ($assembly.file -cne [IO.Path]::GetFileName($assembly.file) -or $assembly.file.Contains('\')) { throw 'Invalid package path.' }
                if ((Get-FileHash -LiteralPath (Join-Path $root $assembly.file)).Hash -ine $assembly.sha256) { $cached = $false }
                if ($assembly.name -cne [IO.Path]::GetFileName($assembly.name) -or $assembly.name.Contains('\')) { throw 'Invalid assembly identity.' }
                $input = if ($assembly.name -eq $manifest.entryAssembly) { $builds[$app.slug].entry } else { Join-Path (Split-Path $builds[$app.slug].entry -Parent) ($assembly.name + '.dll') }
                if (!$assembly.inputSha256 -or (Get-FileHash -LiteralPath $input).Hash -ine $assembly.inputSha256) { $cached = $false }
            }
        } catch { $cached = $false }
    }
    if (!$cached) {
        & (Join-Path $PSScriptRoot 'Build-WpfApps.ps1')
        # Compatibility gaps are recorded per app; they must not disable the studio.
        New-Item -ItemType Directory artifacts/app-preparation -Force | Out-Null
        Set-Content -LiteralPath $stampFile -Value $stamp -NoNewline
    } else { Write-Output 'App packages match the current engine, converter, source pins and image hashes.' }
    & dotnet build DesktopWasm.slnx --nologo -v minimal --tl:off
    if ($LASTEXITCODE -ne 0) { throw 'Staged app build failed.' }
    if ($PrepareOnly) { return }
    & dotnet run --project src/DesktopWasm.BrowserHost/DesktopWasm.BrowserHost.csproj --launch-profile http --no-restore
} finally { Pop-Location }
