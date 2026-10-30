# Portability diffs — MERGE-IMPORT narrow repoint edits (2026-10-30)

Authority: `reviews/MERGE-copy-gate-feedback.md` (explicitly authorized;
"no blind global replace" does NOT mean leaving known broken absolute paths).
Pre-edit baseline: `<import-manifest-dir>/` (`wf/wpf-src/tests.tsv`) —
every imported file hash-verified before editing; post-edit re-manifest
(`post-src/tests.tsv`) proves ONLY these 5 files changed (plus peer-new
files, which are not imports and were not touched).

Mechanical replacements, assembly names/identity unchanged:

1. `src/WfWasm.WinForms/WfWasm.WinForms.csproj` (65 occurrences)
   - OLD: `..\..\runtimes\winforms\src\...` (mutable original root)
   - NEW: `..\..\inputs\winforms\runtimes\winforms\src\...`
     (pinned copy at `inputs/winforms/runtimes/winforms/src`, 5,547 files,
     hash-manifested post-copy)

2. `src/WfWasm.CanvasHost/WfWasm.CanvasHost.csproj`
   - OLD: `<Reference Include="<staging>\rewritten-small\WfWasm.WinForms.dll" />`
   - NEW: `<Reference Include="..\..\inputs\winforms\rewritten-small\WfWasm.WinForms.dll" />`
   - OLD: `$(MSBuildProjectDirectory)/../../scripts/ensure-windowsbase-full.ps1`
   - NEW: `$(MSBuildProjectDirectory)/../../scripts/winforms/ensure-windowsbase-full.ps1`
   - OLD: `$(MSBuildProjectDirectory)/../../scripts/check-bundle.ps1`
   - NEW: `$(MSBuildProjectDirectory)/../../scripts/winforms/check-bundle.ps1`

3. `tests/WfWasm.CanvasHost.Tests/WfWasm.CanvasHost.Tests.csproj`
   - OLD: `<Reference Include="<staging>\rewritten-small\WfWasm.WinForms.dll" />`
   - NEW: `<Reference Include="..\..\inputs\winforms\rewritten-small\WfWasm.WinForms.dll" />`

4. `tests/WfWasm.WinForms.Tests/WfWasm.WinForms.Tests.csproj`
   - OLD comment: `<tooling>\rewrite-small.ps1`
   - NEW comment: `..\..\scripts\winforms\rewrite-small.ps1`
     (script pinned from `<tooling>/rewrite-small.ps1` to
     `scripts/winforms/rewrite-small.ps1`)
   - OLD: `<Reference Include="<staging>\rewritten-small\WfWasm.WinForms.dll">`
   - NEW: `<Reference Include="..\..\inputs\winforms\rewritten-small\WfWasm.WinForms.dll">`

5. `src/DesktopWasm.BrowserHost/DesktopWasm.BrowserHost.csproj`
   - OLD: `$(MSBuildProjectDirectory)/../../scripts/Ensure-FullAssembly.ps1`
   - NEW: `$(MSBuildProjectDirectory)/../../scripts/wpf/Ensure-FullAssembly.ps1`
   - OLD: `$(MSBuildProjectDirectory)/../../scripts/BundleCheck.ps1`
   - NEW: `$(MSBuildProjectDirectory)/../../scripts/wpf/BundleCheck.ps1`

No silent fallback into originals: every repointed path resolves inside
the new workspace. Remaining non-portable defaults (recorded, not build
blockers): staging-based temp/result dirs in `ensure-windowsbase-full.ps1`
(`<cache>/wasm-cache/tmp` — same-volume hardlink requirement, intentional),
`prepare-syncfusion.ps1`, `test-gallery.ps1`, `test-platform.ps1` —
relocatable-roots follow-up, not part of this slice.

6. `scripts/winforms/ensure-windowsbase-full.ps1` (build-break fix)
   - OLD: `$ws = Split-Path $PSScriptRoot -Parent` (assumed `scripts/` at
     root; partitioned `scripts/winforms/` broke it → MSB3073 exit 1)
   - NEW: `$ws = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent`
   - OLD: `$pristineDir = Join-Path $ws 'targets/paint.net'`
   - NEW: `$pristineDir = Join-Path $ws 'inputs/winforms/targets/paint.net'`
     (415 files / 321MB pinned from
     the frozen WinForms checkout's `targets/paint.net`, 0 hash diff;
     local import receipt, not shipped)
   - `check-bundle.ps1` and WPF `Ensure-FullAssembly.ps1`/`BundleCheck.ps1`
     verified build-safe without edits (absolute `-Root` passed by MSBuild;
     fail-soft framework discovery).
