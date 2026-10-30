[CmdletBinding()]
param([string[]]$Id = @('all'))
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$catalog = @(Get-Content (Join-Path $repoRoot 'src/DesktopWasm.BrowserHost/wwwroot/app-library.json') -Raw | ConvertFrom-Json | ForEach-Object { $_ })
$apps = @($catalog | Where-Object { $_.kind -eq 'external' -and $_.recipe -and ($Id -contains 'all' -or $Id -contains $_.slug) })
foreach ($name in $Id) { if ($name -ne 'all' -and $name -notin $apps.slug) { throw "Unknown app '$name'." } }
$evidence = Join-Path $repoRoot 'artifacts/app-preparation'
New-Item -ItemType Directory -Force $evidence | Out-Null
$results = @()
Push-Location $repoRoot
try {
    foreach ($app in $apps) {
        $log = Join-Path $evidence "$($app.slug)-build.log"
        $phase = 'fetch'
        $entry = $null
        $failure = $null
        $started = [DateTime]::UtcNow
        $packageOutput = Join-Path $repoRoot "src/DesktopWasm.BrowserHost/wwwroot/apps/$($app.slug)"
        $source = Join-Path $repoRoot "targets/studio-inputs/$($app.slug)"
        $project = Join-Path $source $app.project
        try {
            & (Join-Path $PSScriptRoot 'Prepare-WpfTargets.ps1') -Id $app.slug -FullSource *> $log
            $phase = 'build'
            $output = Join-Path $repoRoot "artifacts/app-builds/$($app.slug)"
            # WPF's generated temporary projects do not reliably include the
            # TFM in SDK artifacts-path intermediates. Use upstream's ordinary
            # per-project bin/obj layout and serialize target frameworks.
            & dotnet build $project -c Release -m:1 --nologo -v minimal --tl:off *>> $log
            if ($LASTEXITCODE -ne 0) { throw "Upstream build exited $LASTEXITCODE. See $($app.slug)-build.log." }
            [xml]$projectXml = Get-Content -LiteralPath $project -Raw
            $assembly = @($projectXml.Project.PropertyGroup.AssemblyName | Where-Object { $_ -is [string] -and $_ }) | Select-Object -Last 1
            if (!$assembly) { $assembly = [IO.Path]::GetFileNameWithoutExtension($project) }
            $binaryRoots = @((Join-Path $output 'bin'), (Join-Path (Split-Path $project -Parent) 'bin')) | Where-Object { Test-Path -LiteralPath $_ }
            $entry = Get-ChildItem -LiteralPath $binaryRoots -Recurse -Filter "$assembly.dll" |
                Where-Object { $_.FullName -notmatch '[\\/](ref|refint)[\\/]' } |
                Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1 -ExpandProperty FullName
            if (!$entry) { throw "Build succeeded but no $assembly.dll was produced." }
            $phase = 'conversion'
            & dotnet (Join-Path $repoRoot 'src/DesktopWasm.Convert/bin/Debug/net9.0/DesktopWasm.Convert.dll') package $entry --source (Split-Path $project -Parent) --root $source --out $packageOutput --slug $app.slug --commit $app.commit *>> $log
            if ($LASTEXITCODE -ne 0) { throw "Conversion failed; exact assembly/API gaps are in apps/$($app.slug)/app-manifest.json and $($app.slug)-build.log." }
            $phase = 'converted'
        } catch {
            $failure = $_.Exception.Message
            $manifestPath = Join-Path $packageOutput 'app-manifest.json'
            if ($phase -ne 'conversion' -or !(Test-Path -LiteralPath $manifestPath) -or (Get-Item -LiteralPath $manifestPath).LastWriteTimeUtc -lt $started) {
                New-Item -ItemType Directory -Force $packageOutput | Out-Null
                $manifest = [ordered]@{ schemaVersion=1; slug=$app.slug; mapVersion='wpf-convert-v1'; status='build-failed'; entryAssembly=''; assemblies=@(); gaps=@(@{ assembly=$app.slug; kind=$phase; detail=$failure }); sourceCommit=$app.commit }
                $pending = Join-Path $packageOutput ('.manifest-' + [guid]::NewGuid())
                $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $pending -Encoding utf8
                Move-Item -LiteralPath $pending -Destination (Join-Path $packageOutput 'app-manifest.json') -Force
            }
        }
        $results += [ordered]@{ slug=$app.slug; phase=$phase; succeeded=($null -eq $failure); entry=$entry; source=$source; project=$project; commit=$app.commit; log=$log; error=$failure }
        $results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'build-results.json') -Encoding utf8
        Write-Output "$($app.slug): $(if ($failure) { "$phase failed: $failure" } else { "converted $entry" })"
    }
} finally { Pop-Location }
if (@($results | Where-Object { !$_.succeeded }).Count) { exit 1 }
