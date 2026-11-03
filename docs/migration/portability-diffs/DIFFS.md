# Portability diffs — MERGE-IMPORT narrow repoint edits (2026-10-30)

Authority: `reviews/MERGE-copy-gate-feedback.md` (explicitly authorized;
"no blind global replace" does NOT mean leaving known broken absolute paths).
Pre-edit baseline: `E:/tmp/merge-manifest/` (`wf/wpf-src/tests.tsv`) —
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
   - OLD: `<Reference Include="E:\tmp\rewritten-small\WfWasm.WinForms.dll" />`
   - NEW: `<Reference Include="..\..\inputs\winforms\rewritten-small\WfWasm.WinForms.dll" />`
   - OLD: `$(MSBuildProjectDirectory)/../../scripts/ensure-windowsbase-full.ps1`
   - NEW: `$(MSBuildProjectDirectory)/../../scripts/winforms/ensure-windowsbase-full.ps1`
   - OLD: `$(MSBuildProjectDirectory)/../../scripts/check-bundle.ps1`
   - NEW: `$(MSBuildProjectDirectory)/../../scripts/winforms/check-bundle.ps1`

3. `tests/WfWasm.CanvasHost.Tests/WfWasm.CanvasHost.Tests.csproj`
   - OLD: `<Reference Include="E:\tmp\rewritten-small\WfWasm.WinForms.dll" />`
   - NEW: `<Reference Include="..\..\inputs\winforms\rewritten-small\WfWasm.WinForms.dll" />`

4. `tests/WfWasm.WinForms.Tests/WfWasm.WinForms.Tests.csproj`
   - OLD comment: `E:\tmp\rewrite-small.ps1`
   - NEW comment: `..\..\scripts\winforms\rewrite-small.ps1`
     (script pinned from `E:/tmp/rewrite-small.ps1` to
     `scripts/winforms/rewrite-small.ps1`)
   - OLD: `<Reference Include="E:\tmp\rewritten-small\WfWasm.WinForms.dll">`
   - NEW: `<Reference Include="..\..\inputs\winforms\rewritten-small\WfWasm.WinForms.dll">`

5. `src/DesktopWasm.BrowserHost/DesktopWasm.BrowserHost.csproj`
   - OLD: `$(MSBuildProjectDirectory)/../../scripts/Ensure-FullAssembly.ps1`
   - NEW: `$(MSBuildProjectDirectory)/../../scripts/wpf/Ensure-FullAssembly.ps1`
   - OLD: `$(MSBuildProjectDirectory)/../../scripts/BundleCheck.ps1`
   - NEW: `$(MSBuildProjectDirectory)/../../scripts/wpf/BundleCheck.ps1`

No silent fallback into originals: every repointed path resolves inside
the new workspace. Remaining non-portable defaults (recorded, not build
blockers): `E:/tmp`-based temp/result dirs in `ensure-windowsbase-full.ps1`
(`E:/tmp/wasm-cache/tmp` — same-volume hardlink requirement, intentional),
`prepare-syncfusion.ps1`, `test-gallery.ps1`, `test-platform.ps1` —
relocatable-roots follow-up, not part of this slice.

6. `scripts/winforms/ensure-windowsbase-full.ps1` (build-break fix)
   - OLD: `$ws = Split-Path $PSScriptRoot -Parent` (assumed `scripts/` at
     root; partitioned `scripts/winforms/` broke it → MSB3073 exit 1)
   - NEW: `$ws = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent`
   - OLD: `$pristineDir = Join-Path $ws 'targets/paint.net'`
   - NEW (reverted by WD-1530-IMP consolidation): `$pristineDir =
     Join-Path $ws 'targets/paint.net'` — `targets/paint.net` is the
     canonical test-expected root path; the `inputs/winforms/targets/
     paint.net` duplicate (415 files / 321MB, byte-identical, 0 hash
     diff vs `targets/paint.net`) was removed. Original pin: 415 files
     pinned from `E:/AI/Code/WindowsForms/targets/paint.net`, 0 hash
     diff; manifest `new-paintnet.tsv`.
   - `check-bundle.ps1` and WPF `Ensure-FullAssembly.ps1`/`BundleCheck.ps1`
     verified build-safe without edits (absolute `-Root` passed by MSBuild;
     fail-soft framework discovery).

7. `scripts/winforms/prepare-desktop-benchmarks.ps1` (WD-1400-IMP)
   - OLD: `$repoRoot = Split-Path $PSScriptRoot -Parent` (same partitioned-
     scripts breakage: resolved into `scripts/winforms/` instead of the root)
   - NEW: `$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent`
   - Separate pre-existing defect (recorded, not fixed here): the `-Apps`
     filter (`$Apps -contains $_.slug` against member-enumerated arrays)
     never matches on PS 5.1, so filtered runs silently prepare nothing;
     full runs iterate one wrapped array. Inputs were mapped by copying the
     prepared closures instead.

8. `src/WfWasm.CanvasHost/WinFormsAppHost.cs` (WD-1730-IMP first-chance
   hardening; crash fix, not portability — recorded here as the narrow-edit
   log: only the `EnsurePaintDotNetInitialized` first-chance lambda changed)
   - OLD: live `new StackTrace(0, ...).GetFrames()` capture on the dispatch
     thread + `BrowserErrorReporter.Report("first-chance", ...)` (attempts
     JS/HTTP transport on the dispatch thread)
   - NEW: `FormatFirstChance(e.Exception, frames: null)` (null-tolerant by
     WF-R-NRE-0730 design) + `BrowserErrorReporter.ReportConsole(...)`
   - Reason: resolving dispatch-thread frames over hostile PDN methods, and
     an HTTP send on that thread, fail-fast the runtime (EE 0x80131506) —
     5/6 full-suite aborts pre-fix. All other `GetFrames` call sites and the
     hook/subscription logic are untouched.

9. `src/WfWasm.CanvasHost/BrowserErrorReporter.cs` (WD-1730-IMP)
   - OLD: only `Report` (console + rate gate, then always attempts JS/HTTP
     transport when a backend/client is set)
   - NEW: added `ReportConsole` (identical console formatting and shared
     rate-gate budgets, returns before any transport; `Report` itself
     byte-identical, so the reporter tests covering it are unaffected)
   - Only the first-chance lambda (diff #8) calls the console-only path.

Update (RV-1745-DW-IMPORT, 2026-10-30): the `E:/tmp`-based defaults in
`prepare-syncfusion.ps1`, `test-gallery.ps1`, `test-platform.ps1` are now
system-temp-based (items 14-15 below); `ensure-windowsbase-full.ps1` keeps
its `E:/tmp/wasm-cache/tmp` scratch intentionally (same-volume hardlink
requirement) plus an explicit `-Optional` soft path. `rewrite-small.ps1`
hard-coded roots are now parameters (item 12).

11. `scripts/winforms/ensure-windowsbase-full.ps1` (RV-1745-DW-IMPORT)
    - ADDED: `-Optional` switch. Absent pristine input warns + exits 0 only
      when opted in; strict default still throws (explicit enabled paths
      fail clearly on missing required inputs).
    - NOT adopted from publication: `inputs/winforms/targets/paint.net`
      pristine path (stale — canonical test-expected root is
      `targets/paint.net`, duplicate removed WD-1530); `GetTempPath`
      scratch (same-volume hardlink requirement intentional); the
      `$env:DESKTOPWASM_STRICT_INPUTS` soft-default (inverts strictness —
      silent-facade risk, see item 17).

12. `scripts/winforms/rewrite-small.ps1` (RV-1745-DW-IMPORT)
    - OLD: hard-coded `$ws = "E:\AI\Code\WindowsForms"`,
      `$out = "E:\tmp\rewritten-small"`,
      `$dotnet10 = "C:\Users\razva\.dotnet-10\dotnet.exe"`,
      `$calcStage = "E:\tmp\build\calc"`.
    - NEW: `-Ws`/`-Out`/`-Dotnet` parameters with repo-relative defaults
      (`$Ws` = workspace root, `$Out` = `$Ws/artifacts-local/rewritten-small`,
      `$Dotnet` = `dotnet`, `$calcStage` under `artifacts-local/build/calc`).
      Missing target sources fail clearly via the existing robocopy/pristine
      checks. Not executed this slice (no conversion pool for path selection).

13. `scripts/unified/Copy-ImportedSources.ps1` (RV-1745-DW-IMPORT)
    - OLD: `-WinformsRoot`/`-WpfRoot` defaulted to the frozen original roots.
    - NEW: no portable default; throws
      `Copy-ImportedSources requires explicit -WinformsRoot and -WpfRoot`
      when either is absent. One-shot migration tooling; no local behavior
      to preserve.

14. `scripts/winforms/prepare-syncfusion.ps1` (RV-1745-DW-IMPORT)
    - NEW: vendor staging defaults under the system temp dir.
    - FIXED (same partitioned-scripts pattern as items 6-7): `$repoRoot`
      is now double `Split-Path`, so
      `src/WfWasm.CanvasHost/SyncfusionDemos.json` resolves (verified
      present; single-split resolved into `scripts/src/...`). Publication
      staging still carries the broken single-split form — live is ahead.
    - Not executed this slice (no bulk vendor download for path selection).

15. `scripts/winforms/test-gallery.ps1`, `test-platform.ps1`,
    `test-targeted.ps1` (RV-1745-DW-IMPORT)
    - Gallery run dir and platform results dir now default under the system
      temp dir. `test-targeted.ps1` triage comment no longer references
      `E:/tmp`. Output paths only; no behavior change.

16. `src/DesktopWasm.BrowserHost/DesktopWasm.BrowserHost.csproj`
    (RV-1745-DW-IMPORT; `StageConvertedPreviewApps` only)
    - NEW: per-file `Exists()` conditions on the four `Copy` tasks. Absent
      ConvertedApps inputs are skipped (the catalog already reports those
      entries missing-input; no stubs, no false ready claims). All four
      local inputs exist, so the configured local build is unchanged.

17. Deliberately NOT adopted from publication (recorded with reason;
    needs shell coordination, outside frozen browser acceptance)
    - `$(RewrittenForkDll)` reference reroute +
      `EnsureRewrittenFork.targets`/`Ensure-RewrittenFork.ps1` regeneration
      in CanvasHost + both test csprojs: changes build inputs and runs
      conversion on every build. Pinned
      `inputs/winforms/rewritten-small` references stay.
    - `DesktopWasmFullWindowsBase=true` opt-in gating of
      `Prepare/Use/GateBundleWindowsBase` in CanvasHost: the default build
      would then bundle the runtime-pack facade with no warning, silently
      replacing the working full-WindowsBase setup that Paint.NET runtime
      acceptance needs. Targets stay unconditional (strict gate preserved).
    - Publication redaction-only doc edits (`<staging>`, `<tooling>`,
      `<import-manifest-dir>`, redacted quarantine paths in
      COPY-REPORT/DIFFS/UNIFICATION/CATALOG-CONTRACT): live keeps exact
      local paths for migration auditability; not copied.
    - `WinFormsAppHost.cs` is owned by the runtime lane this slice; untouched.

10. `src/WfWasm.CanvasHost/WinFormsAppHost.cs` (WC-1740-IMP; five
    catch-block `[diagstack]` sites: form-create, mouse enter/leave, mouse
    event, control click, key handlers)
    - OLD: `new StackTrace(RootCause(ex), ...).GetFrames().Take(12)` live
      method-chain print on the exception path
    - NEW: `FormatFirstChance(RootCause(ex), frames: null).ConsoleLines`
      printed with the `[diagstack]` prefix (reuses the hardened,
      null-tolerant formatter; no live method resolution anywhere left in
      the product file — verified 0 `GetFrames()` remain outside tests)
    - No test asserts `[diagstack]` content or console output (verified by
      search before editing); output shape changes from method-chain to
      the `[1ST-EST]`/`[1ST-N]` format lines.
