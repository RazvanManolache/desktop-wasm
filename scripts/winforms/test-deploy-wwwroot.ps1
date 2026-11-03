# Focused deploy-wwwroot check: valid complete fixture + missing-framework and
# missing-CSS negatives. Everything stages into fresh temp dirs (NEW outputs);
# live served roots are never touched. Fixture mimics the real build layout:
#   src/WfWasm.CanvasHost/wwwroot/{index.html,css/app.css}      (src statics)
#   src/WfWasm.CanvasHost/bin/Debug/net9.0/wwwroot/_framework/ (compiled fw)
#   src/WfWasm.CanvasHost/obj/Debug/net9.0/scopedcss/bundle/    (scoped css)
# Run: powershell -File scripts/winforms/test-deploy-wwwroot.ps1
param()
$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$deploy = Join-Path $scriptDir 'deploy-wwwroot.ps1'
$fail = 0
function Check($name, $cond) { if ($cond) { Write-Output ("PASS " + $name) } else { Write-Output ("FAIL " + $name); $script:fail++ } }
function Hash-File($p) { return ([Security.Cryptography.SHA256]::Create().ComputeHash([IO.File]::ReadAllBytes($p)) | ForEach-Object { $_.ToString('x2') }) -join '' }
function New-FixtureRoot {
  $t = Join-Path ([IO.Path]::GetTempPath()) ('dwdeploy-' + [Guid]::NewGuid().ToString('N'))
  foreach ($d in @('src/WfWasm.CanvasHost/wwwroot/css',
      'src/WfWasm.CanvasHost/bin/Debug/net9.0/wwwroot/_framework',
      'src/WfWasm.CanvasHost/obj/Debug/net9.0/scopedcss/bundle')) {
    New-Item -ItemType Directory -Path (Join-Path $t $d) | Out-Null
  }
  '<html>fixture</html>' | Set-Content -Path (Join-Path $t 'src/WfWasm.CanvasHost/wwwroot/index.html')
  '.top{color:red}' | Set-Content -Path (Join-Path $t 'src/WfWasm.CanvasHost/wwwroot/css/app.css')
  '.scoped{color:blue}' | Set-Content -Path (Join-Path $t 'src/WfWasm.CanvasHost/obj/Debug/net9.0/scopedcss/bundle/Fix.styles.css')
  # Compiled framework: boot manifest + fingerprinted wasm satisfying the
  # bundle gate (full-size WindowsBase/ConfigManager with integrity entries,
  # DependencyObject present, no ReflectionOnly guard, OleDb present).
  $fw = Join-Path $t 'src/WfWasm.CanvasHost/bin/Debug/net9.0/wwwroot/_framework'
  $rng = [Random]::new(42)
  $specs = @(
    @{name='WindowsBase.abc12345.wasm'; size=122880},
    @{name='System.Configuration.ConfigurationManager.def67890.wasm'; size=112640},
    @{name='System.Data.OleDb.ghi13579.wasm'; size=1024}
  )
  foreach ($spec in $specs) {
    $bytes = [byte[]]::new([int]$spec.size); $rng.NextBytes($bytes)
    $marker = [Text.Encoding]::ASCII.GetBytes('System.Windows.DependencyObject padding')
    [Array]::Copy($marker, 0, $bytes, 0, $marker.Length)
    [IO.File]::WriteAllBytes((Join-Path $fw $spec.name), $bytes)
  }
  $asm = @{}
  foreach ($f in Get-ChildItem $fw -Filter '*.wasm') {
    $h = [Convert]::ToBase64String([Security.Cryptography.SHA256]::Create().ComputeHash([IO.File]::ReadAllBytes($f.FullName)))
    $asm[$f.Name] = 'sha256-' + $h
  }
  (@{resources=@{assembly=$asm}} | ConvertTo-Json -Depth 4) | Set-Content -Path (Join-Path $fw 'blazor.boot.json')
  return $t
}
function Run-Deploy($repo, $stage, [switch]$VerifyOnly) {
  $a = @('-RepoRoot', $repo, '-ServerRoot', $stage)
  if ($VerifyOnly) { $a += @('-VerifyOnly') }
  $prev=$ErrorActionPreference; $ErrorActionPreference='Continue'
  & powershell -NoProfile -ExecutionPolicy Bypass -File $deploy @a | Out-Null
  $c=$LASTEXITCODE; $ErrorActionPreference=$prev; return $c
}

# F1 valid complete fixture deploys into a NEW stage, bytes identical.
$t = New-FixtureRoot
$stage = Join-Path $t 'stage-new'
$c = Run-Deploy $t $stage
$ok = ($c -eq 0)
$ok = $ok -and ((Hash-File (Join-Path $t 'src/WfWasm.CanvasHost/wwwroot/index.html')) -eq (Hash-File (Join-Path $stage 'index.html')))
$ok = $ok -and ((Hash-File (Join-Path $t 'src/WfWasm.CanvasHost/wwwroot/css/app.css')) -eq (Hash-File (Join-Path $stage 'css/app.css')))
$ok = $ok -and ((Hash-File (Join-Path $t 'src/WfWasm.CanvasHost/obj/Debug/net9.0/scopedcss/bundle/Fix.styles.css')) -eq (Hash-File (Join-Path $stage 'Fix.styles.css')))
$ok = $ok -and (Test-Path (Join-Path $stage '_framework/blazor.boot.json'))
$ok = $ok -and ((Hash-File (Join-Path $t 'src/WfWasm.CanvasHost/bin/Debug/net9.0/wwwroot/_framework/WindowsBase.abc12345.wasm')) -eq (Hash-File (Join-Path $stage '_framework/WindowsBase.abc12345.wasm')))
Check 'F1-complete-fixture-deploys' $ok

# F2 VerifyOnly on the staged output passes.
$c = Run-Deploy $t $stage -VerifyOnly
Check 'F2-verifyonly-clean' ($c -eq 0)

# F3 missing compiled _framework fails explicitly (no silent partial stage).
$t3 = New-FixtureRoot
Remove-Item (Join-Path $t3 'src/WfWasm.CanvasHost/bin/Debug/net9.0/wwwroot/_framework') -Recurse -Force
$c = Run-Deploy $t3 (Join-Path $t3 'stage-new')
Check 'F3-missing-framework-fails' ($c -ne 0)

# F4 missing scoped css fails explicitly (no unstyled silent ship).
$t4 = New-FixtureRoot
Remove-Item (Join-Path $t4 'src/WfWasm.CanvasHost/obj/Debug/net9.0/scopedcss/bundle/Fix.styles.css') -Force
$c = Run-Deploy $t4 (Join-Path $t4 'stage-new')
Check 'F4-missing-css-fails' ($c -ne 0)

# F5 byte drift in the stage is caught by VerifyOnly.
'drifted' | Set-Content -Path (Join-Path $stage 'css/app.css')
$c = Run-Deploy $t $stage -VerifyOnly
Check 'F5-drift-caught' ($c -ne 0)

Write-Output ("FAILURES=" + $fail)
exit $fail
