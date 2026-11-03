param(
    [Parameter(Mandatory = $true)]
    [string]$TargetDir,
    [string[]]$Extensions = @('.wasm', '.dll', '.dat', '.bin', '.js', '.mjs', '.css', '.json', '.html')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $TargetDir)) {
    Write-Error "Target directory '$TargetDir' does not exist."
    exit 1
}

$targetFullPath = (Resolve-Path $TargetDir).Path
Write-Host "Pre-compressing static assets in: $targetFullPath"

# Project-local standalone interpreter only. Resolver throws when the local
# runtime is missing or fails its version gate; there is deliberately NO
# fallback to global/system python.
$LocalPython = & (Join-Path $PSScriptRoot 'Resolve-LocalPython.ps1')
$Helper = Join-Path $PSScriptRoot 'Compress-StaticAsset.py'
if (-not (Test-Path $Helper -PathType Leaf)) {
    Write-Error "Compression helper missing at '$Helper'."
    exit 1
}
Write-Host "Using project-local Python: $LocalPython"

$files = Get-ChildItem -Path $targetFullPath -Recurse -File | Where-Object {
    $ext = $_.Extension.ToLowerInvariant()
    $Extensions -contains $ext -and -not ($_.Name.EndsWith('.br') -or $_.Name.EndsWith('.gz'))
}

$processed = 0
$totalRawBytes = 0
$totalBrBytes = 0
$totalGzBytes = 0
$failures = 0

foreach ($file in $files) {
    $filePath = $file.FullName
    $fileBytes = [System.IO.File]::ReadAllBytes($filePath)
    $totalRawBytes += $fileBytes.Length

    # 1. Brotli (.br) via local helper: atomic write, nonempty + roundtrip
    # verified inside the helper, honest exit code enforced here.
    $brPath = "$filePath.br"
    if (-not (Test-Path $brPath) -or ((Get-Item $brPath).LastWriteTimeUtc -lt $file.LastWriteTimeUtc)) {
        $helperOut = & $LocalPython $Helper $filePath $brPath 'br' 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Brotli helper failed for '$filePath' (exit $LASTEXITCODE): $helperOut"
            $failures++
            continue
        }
        $brInfo = Get-Item $brPath -ErrorAction SilentlyContinue
        if ($null -eq $brInfo -or $brInfo.Length -eq 0) {
            Write-Error "Brotli helper reported success but '$brPath' is missing or empty."
            $failures++
            continue
        }
        $totalBrBytes += $brInfo.Length
    } else {
        $totalBrBytes += (Get-Item $brPath).Length
    }

    # 2. GZip (.gz) via .NET (unchanged contract), now output-validated.
    $gzPath = "$filePath.gz"
    if (-not (Test-Path $gzPath) -or ((Get-Item $gzPath).LastWriteTimeUtc -lt $file.LastWriteTimeUtc)) {
        $ms = New-Object System.IO.MemoryStream
        $gz = New-Object System.IO.Compression.GZipStream($ms, [System.IO.Compression.CompressionLevel]::Optimal)
        $gz.Write($fileBytes, 0, $fileBytes.Length)
        $gz.Dispose()
        $gzBytes = $ms.ToArray()
        if ($gzBytes.Length -eq 0) {
            Write-Error "GZip produced empty output for '$filePath'."
            $failures++
            continue
        }
        [System.IO.File]::WriteAllBytes($gzPath, $gzBytes)
        $totalGzBytes += $gzBytes.Length
    } else {
        $totalGzBytes += (Get-Item $gzPath).Length
    }

    $processed++
}

Write-Host "Pre-compression completed: $processed assets processed."
if ($totalRawBytes -gt 0) {
    $brSavings = [Math]::Round((1.0 - ($totalBrBytes / $totalRawBytes)) * 100, 1)
    $gzSavings = [Math]::Round((1.0 - ($totalGzBytes / $totalRawBytes)) * 100, 1)
    Write-Host "Raw size: $([Math]::Round($totalRawBytes / 1MB, 2)) MB"
    Write-Host "Brotli size: $([Math]::Round($totalBrBytes / 1MB, 2)) MB ($brSavings% reduction)"
    Write-Host "GZip size: $([Math]::Round($totalGzBytes / 1MB, 2)) MB ($gzSavings% reduction)"
}

if ($failures -gt 0) {
    Write-Error "Pre-compression finished with $failures failure(s); no bogus success reported."
    exit 1
}
