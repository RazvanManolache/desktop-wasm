#Requires -Version 5.1
<#
.SYNOPSIS
  dw-shell owned served-generation freeze: pin one self-contained candidate.

  Read-only (no builds, no lease needed): runs the served-generation
  preflight first and refuses to freeze a broken deployment, then writes an
  immutable generation directory with closure/static manifests for the full
  candidate surface after alias/proxy changes:

    backend + CanvasHost + WPF assets + manifests + app inputs + catalog hashes

  Large wasm payloads are pinned by SHA256/path/bytes/mtime (closure
  manifest), not recopied per generation; small manifests (boot JSONs,
  entry.mjs, indexes, catalog) are copied byte-for-byte so the verifier can
  pin one generation and serve exactly those bytes. Nothing is rewritten in
  place: each generation is a new timestamped directory.

  Bridge slice: the assembled servable root ($genDir/wpfroot) is a full copy
  of the staged $WpfRoot overlaid with the FRESH BrowserHost build
  _framework (bridge registration + Interop/fork payload + fresh boot
  manifest), because the staged root predates the bridge build. Bridge pins
  cover the bin DLLs and the fingerprinted bundle files in both the build
  output and the assembled root.
#>
param(
    [string]$RepoRoot = "E:/AI/Code/DesktopWasm",
    [string]$OutDir = "E:/AI/Code/desktop-wasm-coordination/verification/dw-shell-route/generations",
    [string]$WpfRoot = "E:/AI/Code/desktop-wasm-coordination/verification/dw-shell-route/wpfroot",
    [string]$WebRoot = "E:/AI/Code/DesktopWasm/src/DesktopWasm.Web/wwwroot",
    [string]$CatalogPath = "E:/AI/Code/DesktopWasm/catalog/apps.json",
    [string]$BrowserHostBin = "E:/AI/Code/DesktopWasm/src/DesktopWasm.BrowserHost/bin/Debug/net9.0",
    [string]$BrowserHostWwwRoot = "E:/AI/Code/DesktopWasm/src/DesktopWasm.BrowserHost/wwwroot",
    [string]$PortalFrameworkRoot = "E:/AI/Code/DesktopWasm/src/DesktopWasm.Web/bin/Debug/net9.0/wwwroot",
    [string]$CatalogStagingRoot = "E:/AI/Code/DesktopWasm/inputs/wpf/catalog-staging",
    [string[]]$StageSlugs = @("material-design"),
    [string]$JointPackageRoot = "E:/AI/Code/DesktopWasm/inputs/mixed/testapp-package",
    [string]$JointSlug = "testapp-joint",
    [string]$Generation = "",
    [switch]$NoAssemble
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($Generation)) {
    $Generation = ([DateTime]::UtcNow).ToString("yyyyMMddTHHmmssZ")
}
$genDir = Join-Path $OutDir $Generation

function Invoke-Preflight([string]$Wpf, [string]$Web) {
    $script = Join-Path $RepoRoot "scripts/unified/served-generation-preflight.ps1"
    $preOut = & $script -RepoRoot $RepoRoot -WpfRoot $Wpf -WebRoot $Web -CatalogPath $CatalogPath 2>&1
    $preExit = $LASTEXITCODE
    $preOut | Out-String | Write-Host
    if ($preExit -ne 0) { throw "preflight failed: refusing to freeze a broken candidate" }
}

function Get-Pin([string]$FullPath, [string]$Label) {
    if (-not (Test-Path $FullPath -PathType Leaf)) {
        return @{ label = $Label; path = $FullPath; state = "absent" }
    }
    $h = (Get-FileHash $FullPath -Algorithm SHA256).Hash
    $info = Get-Item $FullPath
    return @{ label = $Label; path = $FullPath; state = "present"; sha256 = $h; bytes = $info.Length; mtimeUtc = $info.LastWriteTimeUtc.ToString("o") }
}

New-Item -ItemType Directory -Force -Path $genDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $genDir "manifests") | Out-Null

# Self-contained portal root (per DIRECT-DEMO-REPAIR: the source-only
# portal cannot pass — index.html references a compiled _framework that
# lives only in the Web build output). Assemble genDir/portal from the
# source index.html + the built _framework, and gate/serve exactly that.
$portalRoot = Join-Path $genDir "portal"
New-Item -ItemType Directory -Force -Path $portalRoot | Out-Null
Copy-Item (Join-Path $WebRoot "index.html") $portalRoot -Force
$portalFwSrc = Join-Path $PortalFrameworkRoot "_framework"
if (-not (Test-Path (Join-Path $portalFwSrc "blazor.boot.json") -PathType Leaf)) {
    throw "portal _framework missing blazor.boot.json: build DesktopWasm.Web first, refusing to freeze"
}
Copy-Item $portalFwSrc (Join-Path $portalRoot "_framework") -Recurse -Force
$portalPins = @(
    (Get-Pin (Join-Path $portalRoot "index.html") "portal:index.html")
    (Get-Pin (Join-Path $portalRoot "_framework/blazor.boot.json") "portal:_framework/blazor.boot.json")
    (Get-Pin (Join-Path $portalRoot "_framework/blazor.webassembly.js") "portal:_framework/blazor.webassembly.js")
)
Invoke-Preflight $WpfRoot $portalRoot

$assembledRoot = $null
if (-not $NoAssemble) {
    # Immutable servable root: full copy of the staged root, then overlay
    # the fresh build _framework (new fingerprints + fresh boot manifest).
    # Stale fingerprinted extras are left in place (nothing references them).
    $assembledRoot = Join-Path $genDir "wpfroot"
    New-Item -ItemType Directory -Force -Path $assembledRoot | Out-Null
    Copy-Item (Join-Path $WpfRoot "*") $assembledRoot -Recurse -Force
    $freshFw = Join-Path $BrowserHostBin "wwwroot/_framework"
    if (-not (Test-Path (Join-Path $freshFw "blazor.boot.json") -PathType Leaf)) {
        throw "fresh BrowserHost _framework missing blazor.boot.json: refusing to assemble"
    }
    Copy-Item (Join-Path $freshFw "*") (Join-Path $assembledRoot "_framework") -Recurse -Force
    # Refresh root-level static assets from the BrowserHost SOURCE wwwroot
    # (Blazor ships wwwroot verbatim; only _framework is a build output —
    # compiled Home.razor arrives via the _framework overlay above).
    # studio.mjs/entry.mjs/css/js/fonts refresh from source; apps/ and
    # app-library.json are NEVER overlaid: staged package bytes stay
    # byte-identical, and the library gets the catalog-derived joint merge
    # below instead.
    foreach ($item in (Get-ChildItem $BrowserHostWwwRoot -Force)) {
        if ($item.Name -in @("apps", "app-library.json")) { continue }
        Copy-Item $item.FullName $assembledRoot -Recurse -Force
    }
    # Gate the ASSEMBLED roots too: the verifier serves exactly these bytes.
    $ascript = Join-Path $RepoRoot "scripts/unified/served-generation-preflight.ps1"
    $aOut = & $ascript -RepoRoot $RepoRoot -WpfRoot $assembledRoot -WebRoot $portalRoot -CatalogPath $CatalogPath 2>&1
    $aExit = $LASTEXITCODE
    $aOut | Out-String | Write-Host
    if ($aExit -ne 0) { throw "assembled-root preflight failed: refusing to freeze" }
}

function Get-BridgePins([string]$FwDir, [string]$BinDir, [string]$Scope) {
    $found = @()
    foreach ($pat in @("DesktopWasm.Interop.*.wasm", "WfWasm.WinForms.*.wasm", "WfWasm.Drawing.*.wasm", "Microsoft.Win32.SystemEvents.*.wasm")) {
        $hits = @()
        if (Test-Path $FwDir -PathType Container) {
            $hits = @(Get-ChildItem $FwDir -File -Filter $pat | Where-Object { $_.Extension -eq ".wasm" })
        }
        if ($hits.Count -eq 0) {
            $found += @{ label = ("bridge:" + $pat); scope = $Scope; dir = $FwDir; state = "absent" }
        } else {
            foreach ($f in ($hits | Sort-Object Name)) {
                $found += @{ label = ("bridge:" + $f.Name); scope = $Scope; dir = $FwDir; state = "present";
                    sha256 = (Get-FileHash $f.FullName -Algorithm SHA256).Hash; bytes = $f.Length; mtimeUtc = $f.LastWriteTimeUtc.ToString("o") }
            }
        }
    }
    foreach ($dll in @("DesktopWasm.Interop.dll", "WfWasm.WinForms.dll", "WfWasm.Drawing.dll", "Microsoft.Win32.SystemEvents.dll")) {
        $found += Get-Pin (Join-Path $BinDir $dll) ("bridge-bin:" + $dll)
    }
    return $found
}

$pins = @()
$pins += Get-Pin (Join-Path $WebRoot "index.html") "portal:index.html"
$pins += Get-Pin (Join-Path $WpfRoot "index.html") "wpf:index.html"
$pins += Get-Pin (Join-Path $WpfRoot "entry.mjs") "wpf:entry.mjs"
$pins += Get-Pin (Join-Path $WpfRoot "app-library.json") "wpf:app-library.json"
$pins += Get-Pin (Join-Path $WpfRoot "_framework/blazor.boot.json") "wpf:_framework/blazor.boot.json"
$pins += Get-Pin (Join-Path $RepoRoot "src/DesktopWasm.BrowserHost/wwwroot/entry.mjs") "source:BrowserHost/entry.mjs"
$pins += Get-Pin $CatalogPath "catalog:apps.json"
$pins += Get-Pin (Join-Path $RepoRoot "src/WfWasm.CanvasHost/bin/Debug/net9.0/wwwroot/_framework/blazor.boot.json") "backend:canvas-boot-manifest"
$pins += Get-Pin (Join-Path $RepoRoot "src/WfWasm.Host.Server/bin/Debug/net9.0/wwwroot/_framework/blazor.boot.json") "backend:host-boot-manifest"
$pins += Get-Pin (Join-Path $RepoRoot "src/DesktopWasm.BrowserHost/bin/Debug/net9.0/wwwroot/_framework/blazor.boot.json") "wpf:build-boot-manifest"
$buildFw = Join-Path $BrowserHostBin "wwwroot/_framework"
$pins += Get-BridgePins $buildFw $BrowserHostBin "build-output"
if ($assembledRoot) {
    $pins += Get-BridgePins (Join-Path $assembledRoot "_framework") $BrowserHostBin "assembled-root"
}
$pins += Get-Pin "E:/AI/Code/desktop-wasm-coordination/verification/dw-shell-route/testapp-joint/TestApp.joint.dll" "testapp:TestApp.joint.dll"
$pins += Get-Pin "E:/AI/Code/desktop-wasm-coordination/verification/dw-shell-route/testapp-joint/TestApp.exe" "testapp:TestApp.exe"

# App inputs inventory: relative path + bytes + mtime for every file, SHA256
# for files up to 8 MB (larger binaries are pinned by path/bytes/mtime and
# the catalog hash that references them).
$inputsRoot = Join-Path $RepoRoot "inputs"
$inputs = @()
if (Test-Path $inputsRoot -PathType Container) {
    foreach ($f in (Get-ChildItem $inputsRoot -Recurse -File | Sort-Object FullName)) {
        $rel = $f.FullName.Substring($inputsRoot.Length + 1)
        $entry = @{ path = ("inputs/" + $rel.Replace("\", "/")); bytes = $f.Length; mtimeUtc = $f.LastWriteTimeUtc.ToString("o") }
        if ($f.Length -le 8MB) {
            $entry.sha256 = (Get-FileHash $f.FullName -Algorithm SHA256).Hash
        } else {
            $entry.sha256 = $null
            $entry.note = "large-pinned-by-path-bytes-mtime"
        }
        $inputs += $entry
    }
}

# Byte-copies of the small manifests so the generation is self-contained.
Copy-Item $CatalogPath (Join-Path $genDir "manifests/apps.json") -Force
Copy-Item (Join-Path $WpfRoot "_framework/blazor.boot.json") (Join-Path $genDir "manifests/wpf.blazor.boot.json") -Force
Copy-Item (Join-Path $WpfRoot "entry.mjs") (Join-Path $genDir "manifests/wpf.entry.mjs") -Force
Copy-Item (Join-Path $WpfRoot "index.html") (Join-Path $genDir "manifests/wpf.index.html") -Force
Copy-Item (Join-Path $WebRoot "index.html") (Join-Path $genDir "manifests/portal.index.html") -Force
$canvasBoot = Join-Path $RepoRoot "src/WfWasm.CanvasHost/bin/Debug/net9.0/wwwroot/_framework/blazor.boot.json"
if (Test-Path $canvasBoot -PathType Leaf) { Copy-Item $canvasBoot (Join-Path $genDir "manifests/canvas.blazor.boot.json") -Force }
$buildBoot = Join-Path $BrowserHostBin "wwwroot/_framework/blazor.boot.json"
if (Test-Path $buildBoot -PathType Leaf) { Copy-Item $buildBoot (Join-Path $genDir "manifests/wpf.build.blazor.boot.json") -Force }

# App staging (packaging seam): byte-copy catalog-staged closures
# (manifest + listed bins only) into the generation's servable surfaces:
# wpfroot/apps/<slug>/ (loader path, mirrors hello-real) and
# packages/<slug>/ (package-API path). Closure-verified against the staged
# manifest before copying (refuse on mismatch); no conversion synthesis,
# no availability claims — those belong to the catalog lane.
$appPins = @()
foreach ($slug in $StageSlugs) {
    $srcDir = Join-Path $CatalogStagingRoot $slug
    $manifestPath = Join-Path $srcDir "app-manifest.json"
    if (-not (Test-Path $manifestPath -PathType Leaf)) {
        throw "staged manifest missing for '$slug': refusing to stage"
    }
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    $manifestHash = (Get-FileHash $manifestPath -Algorithm SHA256).Hash
    foreach ($asm in $manifest.assemblies) {
        $binPath = Join-Path $srcDir $asm.file
        if (-not (Test-Path $binPath -PathType Leaf)) {
            throw "staged bin missing for '$slug': $($asm.file): refusing to stage"
        }
        $binHash = (Get-FileHash $binPath -Algorithm SHA256).Hash
        if ($binHash -ne $asm.sha256) {
            throw "closure mismatch for '$slug': $($asm.file) hash $binHash != manifest $($asm.sha256): refusing to stage"
        }
    }
    $pkgDir = Join-Path $genDir ("packages/" + $slug)
    $wpfAppDir = $null
    if ($assembledRoot) {
        $wpfAppDir = Join-Path $assembledRoot ("apps/" + $slug)
        New-Item -ItemType Directory -Force -Path $wpfAppDir | Out-Null
        Copy-Item $manifestPath (Join-Path $wpfAppDir "app-manifest.json") -Force
    }
    New-Item -ItemType Directory -Force -Path $pkgDir | Out-Null
    Copy-Item $manifestPath (Join-Path $pkgDir "app-manifest.json") -Force
    foreach ($asm in $manifest.assemblies) {
        if ($assembledRoot) { Copy-Item (Join-Path $srcDir $asm.file) (Join-Path $wpfAppDir $asm.file) -Force }
        Copy-Item (Join-Path $srcDir $asm.file) (Join-Path $pkgDir $asm.file) -Force
        $appPins += @{ label = ("app:" + $slug + "/" + $asm.file); state = "present"; sha256 = $asm.sha256;
            bytes = (Get-Item (Join-Path $srcDir $asm.file)).Length; manifestSlug = $slug }
    }
    if ($assembledRoot) {
        $appPins += @{ label = ("app:" + $slug + "/app-manifest.json"); state = "present"; sha256 = $manifestHash;
            bytes = (Get-Item $manifestPath).Length; manifestSlug = $slug; entryAssembly = $manifest.entryAssembly }
    }
    Copy-Item $manifestPath (Join-Path $genDir ("manifests/" + $slug + ".app-manifest.json")) -Force
    Write-Host ("staged app " + $slug + ": " + $manifest.assemblies.Count + " bins closure-verified")
}

# Joint package staging (dw-runtime producer output): closure-verify the
# manifest + listed bins, then stage byte-copies into the generation's
# servable surfaces: wpfroot/apps/<joint-slug>/ (loader path) and
# packages/<joint-slug>/ (package-API path), plus receipt/provenance
# byte-copies under manifests/. Same refusal policy as catalog staging:
# missing manifest/bin or hash mismatch aborts the freeze.
if (-not [string]::IsNullOrWhiteSpace($JointPackageRoot)) {
    $jointManifestPath = Join-Path $JointPackageRoot "app-manifest.json"
    if (-not (Test-Path $jointManifestPath -PathType Leaf)) {
        throw "staged joint manifest missing: refusing to stage"
    }
    $jointManifest = Get-Content $jointManifestPath -Raw | ConvertFrom-Json
    if ($jointManifest.slug -ne $JointSlug) {
        throw ("joint manifest slug '" + $jointManifest.slug + "' != '" + $JointSlug + "': refusing to stage")
    }
    $jointManifestHash = (Get-FileHash $jointManifestPath -Algorithm SHA256).Hash
    foreach ($asm in $jointManifest.assemblies) {
        $binPath = Join-Path $JointPackageRoot $asm.file
        if (-not (Test-Path $binPath -PathType Leaf)) {
            throw ("staged joint bin missing: " + $asm.file + ": refusing to stage")
        }
        $binHash = (Get-FileHash $binPath -Algorithm SHA256).Hash
        if ($binHash -ne $asm.sha256) {
            throw ("joint closure mismatch: " + $asm.file + " hash " + $binHash + " != manifest " + $asm.sha256 + ": refusing to stage")
        }
    }
    $jointPkgDir = Join-Path $genDir ("packages/" + $JointSlug)
    $jointWpfDir = $null
    if ($assembledRoot) {
        $jointWpfDir = Join-Path $assembledRoot ("apps/" + $JointSlug)
        New-Item -ItemType Directory -Force -Path $jointWpfDir | Out-Null
        Copy-Item $jointManifestPath (Join-Path $jointWpfDir "app-manifest.json") -Force
        # Prune base-carried stale files (superseded entry bins, ad-hoc
        # EXE/DLL copies): the served loader dir must be byte-exact to the
        # manifest closure — manifest + listed bins, nothing else.
        $wanted = @("app-manifest.json") + @($jointManifest.assemblies | ForEach-Object { $_.file })
        foreach ($stale in (Get-ChildItem $jointWpfDir -File | Where-Object { $_.Name -notin $wanted })) {
            Remove-Item $stale.FullName -Force
            Write-Host ("pruned stale joint file: " + $stale.Name)
        }
    }
    New-Item -ItemType Directory -Force -Path $jointPkgDir | Out-Null
    Copy-Item $jointManifestPath (Join-Path $jointPkgDir "app-manifest.json") -Force
    foreach ($asm in $jointManifest.assemblies) {
        if ($assembledRoot) { Copy-Item (Join-Path $JointPackageRoot $asm.file) (Join-Path $jointWpfDir $asm.file) -Force }
        Copy-Item (Join-Path $JointPackageRoot $asm.file) (Join-Path $jointPkgDir $asm.file) -Force
        $appPins += @{ label = ("joint:" + $JointSlug + "/" + $asm.file); state = "present"; sha256 = $asm.sha256;
            bytes = (Get-Item (Join-Path $JointPackageRoot $asm.file)).Length; manifestSlug = $JointSlug }
    }
    if ($assembledRoot) {
        $appPins += @{ label = ("joint:" + $JointSlug + "/app-manifest.json"); state = "present"; sha256 = $jointManifestHash;
            bytes = (Get-Item $jointManifestPath).Length; manifestSlug = $JointSlug; entryAssembly = $jointManifest.entryAssembly }
    }
    # Served-library merge (guest reads app-library.json, NOT the catalog).
    # Assembled roots only: no-assemble freezes carry no servable library.
    if ($assembledRoot) {
    # upsert the catalog-derived joint record into the assembled root's
    # library copy so the guest registry carries testapp-joint. Source of
    # truth stays the catalog (read-only here); shape follows the library's
    # validateCatalog contract (external + github source + license +
    # boundary; serving-unverified stays "not-converted", never "ready").
    $jointLibPath = Join-Path $assembledRoot "app-library.json"
    $catalogJson = Get-Content $CatalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $jointDesc = @($catalogJson.apps | Where-Object { $_.id -eq $JointSlug })
    if ($jointDesc.Count -ne 1) {
        throw "catalog must carry exactly one '$JointSlug' descriptor: refusing library merge"
    }
    $jd = $jointDesc[0]
    $jointRecord = [ordered]@{
        slug = $JointSlug; label = $jd.label; description = $jd.description
        category = $jd.category; icon = $jd.icon; kind = "external"; status = "not-converted"
        source = $jd.provenance.sourceUrl; repository = $jd.provenance.repository
        commit = $jd.provenance.commit; project = $jd.provenance.project
        license = $jd.provenance.license; boundary = $jd.provenance.boundary
        recipe = [bool]$jd.provenance.hasRecipe
    }
    $lib = Get-Content $jointLibPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $lib = @($lib | Where-Object { $_.slug -ne $JointSlug }) + @([PSCustomObject]$jointRecord)
    $dupes = @($lib.slug | Group-Object | Where-Object { $_.Count -ne 1 })
    if ($dupes.Count -ne 0) { throw "duplicate library slugs after merge: refusing freeze" }
    $libJson = $lib | ConvertTo-Json -Depth 6
    [System.IO.File]::WriteAllText($jointLibPath, $libJson + "`n", [System.Text.UTF8Encoding]::new($false))
    Copy-Item $jointLibPath (Join-Path $genDir "manifests/app-library.json") -Force
    Write-Host ("merged library record " + $JointSlug + ": library now " + $lib.Count + " entries")
    }
    Copy-Item $jointManifestPath (Join-Path $genDir ("manifests/" + $JointSlug + ".app-manifest.json")) -Force
    $jointReceipt = Join-Path $JointPackageRoot ($JointSlug + ".receipt.json")
    if (Test-Path $jointReceipt -PathType Leaf) { Copy-Item $jointReceipt (Join-Path $genDir ("manifests/" + $JointSlug + ".receipt.json")) -Force }
    $jointProv = Join-Path $JointPackageRoot "PROVENANCE.json"
    if (Test-Path $jointProv -PathType Leaf) { Copy-Item $jointProv (Join-Path $genDir ("manifests/" + $JointSlug + ".PROVENANCE.json")) -Force }
    Write-Host ("staged joint " + $JointSlug + ": " + $jointManifest.assemblies.Count + " bins closure-verified, manifest " + $jointManifestHash)
}

$receipt = @{
    generation = $Generation
    owner = "dw-shell"
    harness = "scripts/unified/served-generation-freeze.ps1"
    utc = ([DateTime]::UtcNow.ToString("o"))
    wpfRoot = $WpfRoot
    webRoot = $WebRoot
    portalRoot = $portalRoot
    portalFramework = $PortalFrameworkRoot
    catalogPath = $CatalogPath
    browserHostBin = $BrowserHostBin
    assembledRoot = $assembledRoot
    runtimeReport = "reports/RVW2100-runtime.md (WFI bridge closure input)"
    preflight = "scripts/unified/served-generation-preflight.ps1:OK (portal + staged root + assembled root)"
    pins = ($pins + $portalPins)
    appPins = $appPins
    stagedSlugs = $StageSlugs
    inputs = $inputs
    inputsCount = $inputs.Count
    note = "Large wasm payloads pinned by hash in pins/inputs; small manifests byte-copied under manifests/. Assembled wpfroot = staged root + fresh build _framework (bridge included); serve it, never the stale staged root."
} | ConvertTo-Json -Depth 6
$receipt | Out-File -Encoding utf8 -FilePath (Join-Path $genDir "generation.json")
Write-Host ("generation=" + $Generation)
Write-Host ("dir=" + $genDir)
Write-Host ("pins=" + $pins.Count + " inputs=" + $inputs.Count)
