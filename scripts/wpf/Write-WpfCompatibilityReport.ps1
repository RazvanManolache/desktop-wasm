$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appsJson = Get-Content (Join-Path $repoRoot 'src/DesktopWasm.BrowserHost/wwwroot/app-library.json') -Raw | ConvertFrom-Json
$catalog = @($appsJson | Where-Object kind -eq external)
$buildsJson = Get-Content (Join-Path $repoRoot 'artifacts/app-preparation/build-results.json') -Raw | ConvertFrom-Json
$builds = @($buildsJson)
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Actual upstream compatibility results')
$lines.Add('')
$lines.Add('Generated from pinned upstream builds and staged package reports. Build success and conversion success are separate from browser workflow acceptance. Gap counts are per assembly/type occurrence, not a count of equally sized implementation tasks.')
$lines.Add('')
$lines.Add('| Original app | Upstream build | Conversion | Packaged assemblies | Gaps |')
$lines.Add('|---|---|---|---:|---:|')
$allGaps = @()
foreach ($app in $catalog) {
    $manifest = Get-Content (Join-Path $repoRoot "src/DesktopWasm.BrowserHost/wwwroot/apps/$($app.slug)/app-manifest.json") -Raw | ConvertFrom-Json
    $build = $builds | Where-Object slug -eq $app.slug | Select-Object -Last 1
    $built = if ($build.entry -and (Test-Path -LiteralPath $build.entry)) { 'Built' } else { 'Failed' }
    $lines.Add("| $($app.label) | $built | $($manifest.status) | $(@($manifest.assemblies).Count) | $(@($manifest.gaps).Count) |")
    $allGaps += $manifest.gaps
}
$lines.Add('')
$lines.Add('## Most frequent unmapped types')
$lines.Add('')
$allGaps | Where-Object kind -eq unmapped-type | Group-Object detail | Sort-Object Count -Descending | Select-Object -First 16 | ForEach-Object {
    $lines.Add("- $($_.Name): $($_.Count) assembly occurrences")
}
$lines.Add('')
$lines.Add('## Work for the engine agent')
$lines.Add('')
$lines.Add('Finish current work before opening another rendering spike or recreating app screens. Use the existing shared command renderer and generic manifest loader. Preserve original application/model IL; never promote a vendor suite by swapping in a hand-built imitation, swallowing a setter, dropping a live type or fabricating a native result.')
$lines.Add('')
$lines.Add('1. Establish the real public ABI for foundational values, brushes, dependency-property metadata and callbacks. Match type identity, inheritance, constructor/member signatures and numeric enum values, with compiled original-IL fixtures.')
$lines.Add('2. Connect Control/Visual layout and paint overrides to the existing retained tree. Test real derived controls and their actual MeasureOverride/ArrangeOverride/OnRender paths; adding empty same-named classes is insufficient.')
$lines.Add('3. Implement the template/resource/trigger stack against the smallest original sample, then rerun the affected vendor assemblies. Preserve application resource and component assembly ownership.')
$lines.Add('4. Extend dispatcher, collection views, input, geometry and platform adapters using the concrete per-assembly reports. Reuse proven WinForms/platform boundaries through an explicit adapter; do not import Windows-native execution into the browser.')
$lines.Add('5. Add boot, business-action, visible-result, CPU/GPU, failed-load recovery and source/hash checks for each newly supported original. Keep all prior workflows. Native tests alone do not promote browser compatibility.')
$lines.Add('')
$lines.Add('Full reports: apps/<slug>/app-manifest.json in the preview, downloadable from app details. Local source/build logs: artifacts/app-preparation/. Regenerate with pwsh scripts/Write-WpfCompatibilityReport.ps1 after real preparation.')
$lines | Set-Content -LiteralPath (Join-Path $repoRoot 'docs/application-compatibility-status.md') -Encoding utf8
