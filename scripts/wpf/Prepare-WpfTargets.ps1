[CmdletBinding()]
param([string[]]$Id = @('all'), [switch]$FullSource)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$inputRoot = Join-Path $repoRoot 'targets\studio-inputs'
$catalog = Get-Content -LiteralPath (Join-Path $repoRoot 'src\DesktopWasm.BrowserHost\wwwroot\app-library.json') -Raw | ConvertFrom-Json
$targets = @()
if ($Id -contains 'all') { $Id = @($catalog | Where-Object { $_.kind -eq 'external' -and $_.recipe } | ForEach-Object slug) }
foreach ($name in $Id) {
    $target = @($catalog | Where-Object { $_.slug -ceq $name -and $_.kind -eq 'external' -and $_.recipe })
    if ($target.Count -ne 1) { throw "Unknown external target '$name'. Use a slug from app-library.json." }
    if ($target[0].repository -notmatch '^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or $target[0].commit -notmatch '^[0-9a-f]{40}$') { throw 'Invalid source pin.' }
    $targets += $target[0]
}
New-Item -ItemType Directory -Force -Path $inputRoot | Out-Null
foreach ($target in $targets) {
    $destination = Join-Path $inputRoot $target.slug
    $fresh = -not (Test-Path -LiteralPath $destination)
    if (-not (Test-Path -LiteralPath $destination)) {
        & git clone --filter=blob:none --no-checkout -- $target.repository $destination
        if ($LASTEXITCODE -ne 0) { throw "Clone failed: $($target.slug)" }
    }
    # Never overwrite an existing or dirty checkout, even for a pinned source input.
    $remote = & git -C $destination remote get-url origin
    if ($LASTEXITCODE -ne 0 -or $remote.TrimEnd('/') -ne $target.repository.TrimEnd('/')) { throw "Unexpected repository at $destination" }
    if (-not $fresh) {
        $dirty = & git -C $destination status --porcelain
        if ($LASTEXITCODE -ne 0 -or $dirty) { throw "Preserving modified checkout at $destination; use a clean input folder." }
    }
    & git -C $destination fetch --depth 1 origin $target.commit
    if ($LASTEXITCODE -ne 0) { throw "Could not fetch pin: $($target.slug)" }
    & git -C $destination sparse-checkout init --cone
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize sparse source checkout.' }
    $projectDirectory = (Split-Path -Parent $target.project).Replace('\', '/')
    if ($FullSource) { & git -C $destination sparse-checkout disable }
    else { & git -C $destination sparse-checkout set -- $projectDirectory }
    if ($LASTEXITCODE -ne 0) { throw 'Could not select project sources.' }
    & git -C $destination checkout --detach $target.commit
    if ($LASTEXITCODE -ne 0) { throw 'Could not check out pinned source.' }
    $actual = & git -C $destination rev-parse HEAD
    if ($actual -ne $target.commit -or -not (Test-Path -LiteralPath (Join-Path $destination $target.project))) { throw 'Source pin/project verification failed.' }
    [ordered]@{ slug=$target.slug; repository=$target.repository; commit=$actual; project=$target.project; license=$target.license; preparedAt=[DateTime]::UtcNow.ToString('o'); fullSource=[bool]$FullSource; status='source-fetched' } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $inputRoot "$($target.slug).source.json") -Encoding utf8
    Write-Output "Fetched $($target.label): $destination ($actual); full source: $([bool]$FullSource)."
}
