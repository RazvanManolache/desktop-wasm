# COPY-REPORT — MERGE-IMPORT first-party source copy (2026-10-30T11:57Z real UTC)

Owner: dw-import. Roots frozen per `merge-transition-stop.md` (freeze 11:50Z).
Originals read-only throughout: robocopy copy only, no moves/deletes/hardlinks.
No original `.git` or old AGENTS policy copied into the new root.

## Pinned import set (from frozen originals, manifest-pinned once)

Manifests: `<import-manifest-dir>\` (`wf-src.tsv`, `wpf-src.tsv`,
`wf-tests.tsv`, `wpf-tests.tsv`, plus `wf/wpf-scripts.tsv`, `wf/wpf-docs.tsv`).
Each line: relative path, bytes, SHA-256. Verification = every mapped
destination hash equals the pinned original hash.

| Tree | Original files | Copied files | Hash diff |
|---|---|---|---|
| `src` (WF 405 + WPF 326) | 731 | 731 | 0 lines |
| `tests` (WF 372 + WPF 292, excl. bin/obj/TestResults) | 664 | 664 | 0 lines |
| `scripts` (winforms 14 + wpf 6) | 20 | 20 | 0 lines |
| `docs` (winforms 6 + wpf 25) | 31 | 31 | 0 lines (verified by tree manifest) |

Layout preserves relative project references: `src/<Proj>/`, `tests/<Proj>/`
flat per family (names disjoint, no collisions); scripts/docs partitioned as
`scripts/winforms|wpf`, `docs/legacy/winforms|wpf`; legacy solutions kept as
reference only under `docs/migration/legacy-solutions/`.
`DesktopWasm.slnx` (new root) lists exactly the 53 imported `.csproj`
(src 33 + tests 20 families verified present, zero copy gaps).

## Included fork compile sources + binary inputs

- Full `WfWasm.WinForms` fork sources + `WfWasm.Drawing`, `WfWasm.Rewrite`,
  `Microsoft.Win32.SystemEvents` (hash-verified, 0 diff).
- Intentionally staged binaries under source preserved (NOT under bin/obj):
  `BrowserHost/ConvertedApps/*.dll`, `BrowserHost/wwwroot/apps/**/*.bin`
  + receipts/manifests (WPF staged app closures, hash-verified).
- `inputs/winforms/rewritten-small/` ← `<staging>/rewritten-small`
  (`WfWasm.WinForms.dll` SHA-256 `BA68DEB9…9629`, identical both sides).

## Excluded (with reason)

- `bin/`, `obj/`, `.git`, `__pycache__/`, `.vs/`, `TestResults/`,
  `*.user`, `*.suo`: disposable outputs/caches. Largest WPF-docs case:
  49 files under `docs/review-evidence-2026-10-22/{bin,obj}` (build outputs
  of a probe); evidence sources (`.cs`, `.csproj`, log, json, png, README,
  snapshot) copied.
- Original-root `Slice*.cs` (48 zero-byte stubs at root, unreferenced
  by `DesktopWasm.slnx`): excluded as junk, not source.
- `WfWasm.BrowserHost` (WF `src/`): empty directory in the original; recreated
  empty (no `.csproj`, no sources to lose).

## Retained integrated fixes (frozen shared-tree changes, copied verbatim)

- WF: shared tree had zero lane edits at freeze — nothing to retain/lose.
- WPF IsSelected-DP fallout fix, present and hash-verified:
  `src/DesktopWasm.Wpf/Grouping.cs` (`ItemsPresenter.EnsureHosts`),
  `src/DesktopWasm.Wpf/UiElement.cs` (`ListBoxItem` `IsTabStop=false`),
  `DependencyObject.cs` (F2/F3), `tests/**/ListBoxTests.cs`,
  `tests/**/NativeParityTests.cs`.

## Quarantined (recorded, NOT copied/applied)

Unmerged isolated work per the two freeze handoffs — no auto-apply
(local staging paths, redacted):
`wf-escapedisc-1030/*`, `wf-perf-1145/*`, `wfr-nre0730-fix`,
`wf-escapedisc-1030`, `wpf-c1145-*`, `armb-*`, `hangfix_full4.log`,
`wpf_suite_run.log`, `srv-escapedisc.log*`,
TestResults hangdump `086fc585…`, Sequence XMLs (`dd9d46f1`, `0dbb6dfc`).

## Explicit input gaps / blockers (not portable yet, no silent fallback)

1. `src/WfWasm.CanvasHost/WfWasm.CanvasHost.csproj:42`,
   `tests/WfWasm.CanvasHost.Tests/*.csproj`,
   `tests/WfWasm.WinForms.Tests/*.csproj` still `<Reference Include=
   "<staging-dir>\WfWasm.WinForms.dll">` — absolute mutable-original
   path. Pinned copy exists at `inputs/winforms/rewritten-small/` but csproj
   files were NOT repointed (no blind path rewrite per assignment). Required
   before any root build.
2. `targets/` (paint.net, ILSpy, mRemoteNG, …), `runtimes/`, `telerik/`,
   WPF `targets/studio-inputs` + `references/` closures: not yet mapped —
   continued after this signal, size/usage inventory first (no whole-upstream
   copy). Archival `surveys/`, `baselines/` explicitly out of scope.
3. Peer-new projects landed concurrently and were left untouched, excluded
   from this solution pending negotiation: `src/DesktopWasm.{AppModel,
   Catalog, Host.Server, Packaging, Web}`.

## Copy tooling

- `scripts/unified/Copy-ImportedSources.ps1` (reproducible copy),
  `scripts/unified/New-SourceManifest.ps1` (manifest/hash tool).
- Single-name local functions (`Cp`, etc.) are FORBIDDEN in copy scripts:
  the `cp = Copy-Item` alias shadows functions and caused the earlier
  `src/src` nesting (fixed, verified flat).

Source-ready gate: PASS for first-party src/tests/scripts/docs. Build +
full input mapping continue after this signal.

## Correction 2026-10-30T12:02Z (copy-gate feedback)

The original "Included fork compile sources" claim was wrong while
`runtimes/` was uncopied: `WfWasm.WinForms.csproj` compiles
`runtimes/winforms/src/{System.Windows.Forms{,.Primitives},
System.Private.Windows.Core,Common,System.Windows.Forms.Analyzers}` plus
embedded resources. Corrected this slice:

- `inputs/winforms/runtimes/winforms/src/` ← frozen WinForms fork source
  (5,547 files; local import receipt, not shipped).
- Narrow repoint edits (5 files, 65+7 replacements; full record in
  `docs/migration/portability-diffs/DIFFS.md`): fork prefix
  `..\..\runtimes\` → `..\..\inputs\winforms\runtimes\` in
  `WfWasm.WinForms.csproj`; `<staging-dir>` DLL refs →
  `..\..\inputs\winforms\rewritten-small\...` in CanvasHost(+Tests) and
  WinForms.Tests; build Exec script paths → partitioned
  `scripts/winforms|wpf/` in both BrowserHost/CanvasHost projects;
  regenerator `<tooling>/rewrite-small.ps1` pinned to
  `scripts/winforms/rewrite-small.ps1` (comment repointed).
- Post-edit re-manifest proves ONLY those 5 files differ from the pinned
  pre-edit baseline; every other imported byte is unchanged.
- `DesktopWasm.slnx` extended to 64 projects: 53 imported + 11 peer-new
  validated well-formed with resolvable refs, frozen 2026-10-30T12:02:06Z
  (`AppModel, Catalog{,.Tool,.Tests}, Host.Server, Interop{,.Tests},
  Packaging{,.Tests}, Web, Shell.Tests`). Peer files are owner-lane
  property; this solution only references them.

Scope marker: engine compile inputs for the WinForms fork are now LOCAL
(first-party trees + fork compile/resource trees + rewritten-small +
regenerator + `inputs/winforms/targets/paint.net` pristine closure).
Still open: `targets/` app closures (other than staged
BrowserHost bundles), `runtimes/` remainder beyond `winforms/src`,
`telerik/`, WPF `targets/studio-inputs` + `references/`, and
Local temp/result defaults in scripts (non-build-blocking).
First-party `source-ready` below is therefore scoped, not a full-build
readiness claim; QA verifies the distinction independently.

## Build receipt 2026-10-30T12:06Z (64-project `DesktopWasm.slnx`)

- `dotnet build DesktopWasm.slnx`: **0 errors** (log
  `artifacts/migration/build-64.log`; 571 inherited style/analyzer
  warnings, no new error classes). One build break found and fixed in-slice:
  partitioned `scripts/winforms/` broke `ensure-windowsbase-full.ps1`
  `$ws` assumption (MSB3073) — fixed + `targets/paint.net` pinned (see
  portability diff #6). Lease: `build-DesktopWasm.lock` held for the whole
  child command, released after; old roots untouched.
- Baseline (imported suites, new root): WPF targeted IsSelected families
  (`ListBox*|Slice68|PlotView*|PlotRaster*|OxyPlot*|RendererSwitch|
  StagedArrange*|Slice64Focus`) — **174/174 passed, 0 failed** (log
  `artifacts/migration/test-wpf-targeted.log`). Broader suites
  (Rewrite/Drawing/CanvasHost/full Wpf recount) are the next bounded slice;
  known inherited risks recorded: full-Wpf recount hang, Escape wedge.
