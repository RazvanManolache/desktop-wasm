$ErrorActionPreference = 'Stop'
Push-Location (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent)
try {
    foreach ($project in @('DesktopWasm.Host.Server', 'DesktopWasm.Web', 'WfWasm.Host.Server')) {
        & dotnet build "src/$project/$project.csproj" --no-restore --verbosity minimal
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
} finally { Pop-Location }
