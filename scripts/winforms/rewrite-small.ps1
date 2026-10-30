# Small-app migration: rebuild current sources, migrate the fork onto
# WfWasm.Drawing, and IL-rewrite the 4 pristine small apps onto it.
# Outputs go to a staging dir (regenerable). targets/ is only read.
# The rewrite tool exits 1 while gaps remain, but still writes --out, so
# this script judges each step by its output file, not the exit code.
# Publication portability: all roots are parameters (repo-relative
# defaults); pass explicit paths for a foreign checkout. Requires the
# small-app target sources staged under $Ws/targets (not shipped here).
param(
  [string]$Ws = '',
  [string]$Out = '',
  [string]$Dotnet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Ws)) { $Ws = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent }
if ([string]::IsNullOrWhiteSpace($Out)) { $Out = Join-Path $Ws 'artifacts-local/rewritten-small' }
$tool = Join-Path $ws "src\WfWasm.Rewrite.Tool\bin\Debug\net9.0\WfWasm.Rewrite.Tool.dll"
$forms = Join-Path $ws "src\WfWasm.WinForms\bin\Debug\net9.0\WfWasm.WinForms.dll"
$drawing = Join-Path $ws "src\WfWasm.Drawing\bin\Debug\net9.0\WfWasm.Drawing.dll"

function Invoke-Rewrite([string]$app, [string]$dest) {
    Write-Host "== rewrite $app -> $dest"
    $log = dotnet $tool --app $app --forms (Join-Path $out "WfWasm.WinForms.dll") --drawing $drawing --out $dest 2>&1
    $log | ForEach-Object { Write-Host "   $_" }
    if (!(Test-Path $dest)) { throw "rewrite produced no output: $dest" }
}

Write-Host "== build current sources"
dotnet build (Join-Path $ws "src\WfWasm.WinForms\WfWasm.WinForms.csproj") --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "fork build failed" }
dotnet build (Join-Path $ws "src\WfWasm.Drawing\WfWasm.Drawing.csproj") --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "drawing build failed" }
dotnet build (Join-Path $ws "src\WfWasm.Rewrite.Tool\WfWasm.Rewrite.Tool.csproj") --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "rewrite tool build failed" }

if (!(Test-Path $out)) { New-Item -ItemType Directory $out | Out-Null }

Write-Host "== migrate fork onto WfWasm.Drawing"
$forkLog = dotnet $tool --app $forms --forms $forms --drawing $drawing --out (Join-Path $out "WfWasm.WinForms.dll") 2>&1
$forkLog | Select-Object -First 8 | ForEach-Object { Write-Host "   $_" }
if (!(Test-Path (Join-Path $out "WfWasm.WinForms.dll"))) { throw "fork migration produced no output" }

Write-Host "== pristine calculator: copy sources read-only, build with SDK 10"
$calcStage = Join-Path $Ws 'artifacts-local/build/calc'
robocopy (Join-Path $ws "targets\csharp-win-calculator") $calcStage /MIR /NFL /NDL /NJH /NJS /XD bin obj | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy calc failed: $LASTEXITCODE" }
& $Dotnet build (Join-Path $calcStage "Calculator.csproj") -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "calculator pristine build failed" }
$calcPristine = Join-Path $calcStage "bin\Debug\net10.0-windows\Calculator.dll"
if (!(Test-Path $calcPristine)) { throw "calculator pristine not found: $calcPristine" }

Invoke-Rewrite $calcPristine (Join-Path $out "Calculator.dll")
Invoke-Rewrite (Join-Path $ws "targets\tic-tac-toe\bin\Debug\Tic Tac Toe.exe") (Join-Path $out "TicTacToe.dll")
Invoke-Rewrite (Join-Path $ws "targets\simple-csharp-todolist\SimpleDailyToDoList\SimpleDailyToDoList\bin\Debug\SimpleDailyToDoList.exe") (Join-Path $out "SimpleDailyToDoList.dll")
Invoke-Rewrite (Join-Path $ws "targets\functional-calendar\bin\Debug\net8.0-windows\grp2Calendar.dll") (Join-Path $out "Calendar.dll")

Write-Host "== refresh WfWasm.Drawing copy"
Copy-Item $drawing (Join-Path $out "WfWasm.Drawing.dll") -Force

Write-Host "MIGRATION DONE"
Get-ChildItem $out/*.dll | ForEach-Object { Write-Host ("  {0}  {1} bytes  {2}" -f $_.Name, $_.Length, $_.LastWriteTime) }
