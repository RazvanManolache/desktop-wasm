$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '../..')
New-Item -ItemType Directory -Force .ci-results | Out-Null
& dotnet build DesktopWasm.slnx -c Release --nologo 2>&1 | Tee-Object .ci-results/build.log
if ($LASTEXITCODE) { throw 'Solution build failed.' }

# These complete suites need only tracked source and generated fixtures.
# Tests requiring separately staged app binaries are outside source-only CI.
foreach ($suite in @(
    'DesktopWasm.Catalog', 'DesktopWasm.Shell', 'WfWasm.Rendering.Gles',
    'WfWasm.Renderer', 'WfWasm.Platform', 'WfWasm.IsolatedStorage',
    'WfWasm.Skia', 'WfWasm.Shim', 'WfWasm.Rendering',
    'WfWasm.AppIntegration', 'WfWasm.ErrorLog'
)) {
    & dotnet test "tests/$suite.Tests" -c Release --no-build --nologo --logger trx --results-directory .ci-results
    if ($LASTEXITCODE) { throw "Tests failed: $suite" }
}
# The launcher/catalog slice is held to zero compiler warnings. Existing engine
# diagnostics remain visible in the full build log; see docs/GITHUB-BUILDS.md.
foreach ($suite in @('DesktopWasm.Catalog', 'DesktopWasm.Shell')) {
    & dotnet build "tests/$suite.Tests" -c Release -t:Rebuild -p:TreatWarningsAsErrors=true --nologo
    if ($LASTEXITCODE) { throw "Launcher warning gate failed: $suite" }
}
# Reflection and dynamic engine types require the untrimmed browser payload.
& dotnet publish src/DesktopWasm.BrowserHost -c Release -o .ci-dist/wpf -p:PublishTrimmed=false --nologo
if ($LASTEXITCODE) { throw 'Browser publish failed.' }
& python scripts/ci/package_demo.py
if ($LASTEXITCODE) { throw 'Browser packaging failed.' }
& python scripts/ci/smoke_browser.py .ci-dist/wpf/wwwroot
if ($LASTEXITCODE) { throw 'Packaged browser smoke check failed.' }
