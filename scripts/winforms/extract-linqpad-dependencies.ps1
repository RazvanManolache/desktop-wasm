param(
    [Parameter(Mandatory=$true)][string]$AppFolder,
    [string]$CecilPath = ''
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (!$CecilPath) { $CecilPath = Join-Path $repoRoot 'src/WfWasm.Host.Server/bin/Debug/net9.0/Mono.Cecil.dll' }
Add-Type -LiteralPath $CecilPath
$folder = [IO.Path]::GetFullPath($AppFolder)
$output = Join-Path $folder 'dll'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $folder 'LINQPad.exe'))
$records = @()
try {
    foreach ($resource in $assembly.MainModule.Resources) {
        if ($resource.Name -notlike 'LINQPad.assemblies.*.bin') { continue }
        # F# optimization/signature data are not assemblies.
        if ($resource.Name -match '\.(optdata|sigdata)\.bin$') { continue }
        # Alternative x86 launchers have the same assembly identity as AnyCPU.
        if ($resource.Name -match '\.x86\.bin$') { continue }
        $input = New-Object IO.MemoryStream(,$resource.GetResourceData())
        $expanded = New-Object IO.MemoryStream
        $deflate = New-Object IO.Compression.DeflateStream($input, [IO.Compression.CompressionMode]::Decompress)
        try { $deflate.CopyTo($expanded); $bytes = $expanded.ToArray() }
        finally { $deflate.Dispose(); $input.Dispose(); $expanded.Dispose() }
        $peStream = New-Object IO.MemoryStream(,$bytes)
        $dependency = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($peStream)
        try {
            $name = $dependency.Name.Name
            if (!$name -or $name -ne [IO.Path]::GetFileName($name) -or $name.IndexOfAny([char[]]'\/:') -ge 0) {
                throw "Unsafe embedded assembly identity: $name"
            }
            $path = Join-Path $output ($name + '.dll')
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','') }
            finally { $sha.Dispose() }
            if (Test-Path -LiteralPath $path) {
                if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $hash) { throw "Existing dependency differs: $path" }
            } else { [IO.File]::WriteAllBytes($path, $bytes) }
            $records += [pscustomobject]@{resource=$resource.Name; name=$name; identity=$dependency.Name.FullName; sha256=$hash; bytes=$bytes.Length}
        } finally { $dependency.Dispose(); $peStream.Dispose() }
    }
} finally { $assembly.Dispose() }
$records | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $folder 'embedded-dependencies.json') -Encoding utf8
Write-Output "Extracted and verified $($records.Count) original embedded assemblies."
