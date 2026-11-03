# COPY-REPORT — MERGE-IMPORT first-party source copy (2026-10-30T11:57Z real UTC)

Owner: dw-import. Roots frozen per `merge-transition-stop.md` (freeze 11:50Z).
Originals read-only throughout: robocopy copy only, no moves/deletes/hardlinks.
No original `.git` or old AGENTS policy copied into the new root.

## Pinned import set (from frozen originals, manifest-pinned once)

Manifests: `E:\tmp\merge-manifest\` (`wf-src.tsv`, `wpf-src.tsv`,
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
- `inputs/winforms/rewritten-small/` ← `E:/tmp/rewritten-small`
  (`WfWasm.WinForms.dll` SHA-256 `BA68DEB9…9629`, identical both sides).

## Excluded (with reason)

- `bin/`, `obj/`, `.git`, `__pycache__/`, `.vs/`, `TestResults/`,
  `*.user`, `*.suo`: disposable outputs/caches. Largest WPF-docs case:
  49 files under `docs/review-evidence-2026-10-22/{bin,obj}` (build outputs
  of a probe); evidence sources (`.cs`, `.csproj`, log, json, png, README,
  snapshot) copied.
- `E:/AI/Code/WPF-wasm/Slice*.cs` (48 zero-byte stubs at root, unreferenced
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

Unmerged isolated work per the two freeze handoffs — no auto-apply:
`E:/tmp/wf-escapedisc-1030/*`, `E:/isolated/wf-perf-1145/*`,
`E:/deploy/wfr-nre0730-fix`, `E:/deploy/wf-escapedisc-1030`,
`E:/tmp/wpf-c1145-*`, `E:/tmp/armb-*`, `E:/tmp/hangfix_full4.log`,
`E:/tmp/wpf_suite_run.log`, `E:/tmp/srv-escapedisc.log*`,
TestResults hangdump `086fc585…`, Sequence XMLs (`dd9d46f1`, `0dbb6dfc`).

## Explicit input gaps / blockers (not portable yet, no silent fallback)

1. `src/WfWasm.CanvasHost/WfWasm.CanvasHost.csproj:42`,
   `tests/WfWasm.CanvasHost.Tests/*.csproj`,
   `tests/WfWasm.WinForms.Tests/*.csproj` still `<Reference Include=
   "E:\tmp\rewritten-small\WfWasm.WinForms.dll">` — absolute mutable-original
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

- `inputs/winforms/runtimes/winforms/src/` ←
  `E:/AI/Code/WindowsForms/runtimes/winforms/src/` (5,547 files;
  manifest `artifacts/migration/manifests/post-inputs.tsv`).
- Narrow repoint edits (5 files, 65+7 replacements; full record in
  `docs/migration/portability-diffs/DIFFS.md`): fork prefix
  `..\..\runtimes\` → `..\..\inputs\winforms\runtimes\` in
  `WfWasm.WinForms.csproj`; `E:\tmp\rewritten-small` DLL refs →
  `..\..\inputs\winforms\rewritten-small\...` in CanvasHost(+Tests) and
  WinForms.Tests; build Exec script paths → partitioned
  `scripts/winforms|wpf/` in both BrowserHost/CanvasHost projects;
  regenerator `E:/tmp/rewrite-small.ps1` pinned to
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
`E:/tmp` temp/result defaults in scripts (non-build-blocking).
First-party `source-ready` below is therefore scoped, not a full-build
readiness claim; QA verifies the distinction independently.

## Slice WD-1400-IMP receipts (broader baselines + input mapping)

- `WfWasm.Rewrite.Tests` full: **266/266** after input mapping (3 initial
  failures were missing `targets/paint.net` closure + unprepared Fiddler
  input, not regressions; log `artifacts/migration/test-rewrite-full.log`).
- `WfWasm.Drawing.Tests` full: **549/549**
  (`artifacts/migration/test-drawing-full.log`).
- `WfWasm.CanvasHost.Tests` full: **323/323**
  (`artifacts/migration/test-canvashost-full2.log`). 7 initial failures
  traced to an environment gap, not sources: the suite needs 45 pool
  assemblies adjacent to the test binary (35 transform-cache outputs
  matched byte-for-byte in `E:/tmp/wasm-cache/paintnet`, 2 native-verbatim,
  8 vintage copies incl. the full `WindowsBase.dll` + GAC Presentation
  stack). The Oct 26 lane seeded these by an unrecorded manual step; the
  new root reproduces it explicitly (bin/ only, no source change). Pristine
  pool copies cause a loader MVID clash + host abort — recorded, do not
  substitute. Follow-up: capture pool-staging as a build target or
  documented seed step.
- `prepare-desktop-benchmarks.ps1 -Apps` defect (found while mapping the
  Fiddler input): `-contains` against a member-enumerated array never
  matches, so the `-Apps` filter silently prepares nothing (PS 5.1).
  Workaround used: copy the prepared closure from the frozen
  `E:/tmp/wfwasm-benchmark-apps` junction target (with its
  `preparation.json` provenance). Recommend a one-line filter fix upstream.
- Mapped this slice (hash-manifested, 0 diff vs frozen sources):
  `targets/paint.net` → `DesktopWasm/targets/` (415 files, test-expected
  root-relative path; duplicates the build-script copy under
  `inputs/winforms/` — consolidation follow-up),
  `.artifacts/benchmark-apps/benchmark-fiddler-classic` (94 files),
  `targets/wpf-samples` (454 files, `.git` excluded),
  `benchmarks/baselines/latest.json`.
- `scripts/winforms/prepare-desktop-benchmarks.ps1`: same `$repoRoot`
  parent-of-parent portability fix as `ensure-windowsbase-full.ps1`
  (recorded in `docs/migration/portability-diffs/DIFFS.md`).
- Input inventory (remainder, not mapped): WF `targets/` 4,226 files /
  502MB, WF `runtimes/` remainder 11,989 files / 190MB, `telerik/`
  7,156 files / 188MB (commercial, excluded from redistribution),
  WPF `targets/studio-inputs` 42,241 files / 2.2GB (successor lane),
  WPF `references/` empty. WF absolute-path tests
  (`E:\AI\Code\WindowsForms\targets\...` in RewriteTests) pass only
  because the frozen originals remain on disk — portability note, no
  blind rewrite.

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

## WD-1530-IMP (pool-staging capture + `-Apps` fix + paint.net consolidation) — 2026-10-30T16:28Z

- Pool-staging capture: `scripts/winforms/Seed-CanvasHostTestPool.ps1` (new;
  45 hash-pinned entries: rewritten transform-cache outputs, 2 pristine
  Paint.NET closure files, 6 WPF-stack host inputs, TerraFX from the frozen
  original test bin; volatile-cache fallback resolver; idempotent
  verify-then-copy). Standalone proof on a wiped probe dir: clean run
  `verified=0 copied=45 total=45`; rerun `verified=45 copied=0 total=45`.
  All 45 seeded bytes identical to the frozen original test bin
  (`E:/AI/Code/WindowsForms/tests/WfWasm.CanvasHost.Tests/bin/Debug/net9.0`).
- Build target: `SeedPaintNetTestPool` (`BeforeBuild`) wired in
  `tests/WfWasm.CanvasHost.Tests/WfWasm.CanvasHost.Tests.csproj`. Three
  lease-held rebuilds (`build-DesktopWasm.lock`) all **0 errors**; build logs
  show `pool-seed: verified=0 copied=45 total=45` firing mid-build.
- TerraFX pin correction: the manifest-time pin did not match the
  frozen-original bin file (stable on disk since 2026-10-16, re-verified with
  duplicate-hash stability proof); re-pinned to the file's current hash. The
  shared-prefix anomaly with the stale pin is recorded, unexplained; the
  source file predates the freeze and the vintage 323-run used these bytes.
- `-Apps` filter fix: present in `prepare-desktop-benchmarks.ps1` (lines
  15-20: single-element array unwrap, per-name equality, unknown-slug
  throw). Logic probe vs the real catalog (`DesktopBenchmarks.json`):
  27 entries, `benchmark-fiddler-classic` matches 1, unknown slug throws.
  Full script execution not repeated (would trigger benchmark downloads).
- Consolidation: `inputs/winforms/targets/paint.net` (415 files)
  byte-compared to canonical `targets/paint.net` (415/415 same, 0 diff,
  0 only-either-side) then deleted; zero code references to the duplicate
  existed (only two stale doc lines). DIFFS.md entry #6 corrected to the
  canonical path. Post-consolidation lease-held rebuild: 0 errors, seed
  firing — canonical path satisfies the build.
- CanvasHost full-suite status on this exact state: 323/323 achievability
  proven by `artifacts/migration/test-canvashost-full2.log` (~14:31Z; no
  test/product source edits after that point, verified by recursive mtime
  scan excluding bin/obj; this slice changed no product/test sources —
  only the test csproj build target, the new seed script, DIFFS.md).
  This slice's post-rebuild full runs on the same bytes: 1× 322/323
  (`FocusCommitTests.Typing_Replaces_Selected_Text_In_Focused_Box`, passes
  in isolation) and 5× test-host-crash aborts (Fatal CLR error 0x80131506
  at varying sites: strict-GPU pump, Paint fixture ctor, error-reporter
  HTTP send; completed counts varied 126–221). Isolation greens on the
  current binary: PaintFailure 3/3, Paint collection 6/7 (BrushDrag flaked
  once, passes solo), FocusCommit pass. Pre-existing: a 14:15Z run also
  aborted the host (MVID mismatch, fixed by the vintage pool).
- FINDING (load-bearing, inherited, not caused by this slice): full-suite-only
  instability consistent with the Paint.NET single-instance boot race
  (bounded 5s readiness loop in `PaintHostFixture`) and/or the
  FirstChance→`BrowserErrorReporter`→`HttpClient` path (reporter tests leak a
  static test `HttpClient` via `Initialize`; crash stacks show the abort
  inside `SendAsync`/`PrepareRequestMessage` during a first-chance report).
  No test files were edited, narrowed, or skipped in this slice.

## WC-1630-IMP (staging receipts + Refresh verification; instability triage) — 2026-10-30T16:40Z

- Receipts emitted: `artifacts/migration/receipts-wc-1630-imp.json` (2 ids).
  `paint` → `targets/paint.net` (415 files, tree-sha
  `56BC4E66…C1310`); `benchmark-fiddler-classic` →
  `.artifacts/benchmark-apps/benchmark-fiddler-classic` (94 files, tree-sha
  `23104C3B…F5FAC8`). Method (recorded verbatim): sort unix-style relative
  paths ordinally, lines `sha256hex + "  " + relpath`, receipt sha = sha256hex
  of the UTF-8 listing. Both trees cross-checked 1:1 against their pin
  manifests (`new-targets-paintnet.tsv` 415/415, `new-fiddler.tsv` 94/94,
  0 mismatches). Only these two catalog gaps are genuinely staged in-root
  (95 distinct missing-input gaps inventoried; 6 converted WPF packages not
  copied; WPF demos need the host import; pilot needs a proven session).
- Refresh verified on a COPY of `catalog/` (live catalog untouched):
  `Catalog.Tool refresh` exit 0 (all 139 descriptors validate) →
  `available=2` (`paint`, `benchmark-fiddler-classic`, gaps cleared, evidence
  `local:<path>#<tree-sha>`, notes capped at runtime-unverified),
  `missingInput` 130→129, rest unchanged. Live `catalog/apps.json` NOT
  modified: `NothingIsMarkedWorking` asserts zero Available, and the catalog
  lane owns that test + the go-ahead (CATALOG-CONTRACT: changes need Ops).
- FINDINGS for the catalog lane (observed, not fixed — disjoint ownership):
  (1) `Refresh` demotes the `mixed-session-pilot` from `unavailable` to
  `missingInput` when no pilot receipt exists (state label + interop note
  lost; still blocked, still validated). (2) `Refresh` is not idempotent:
  re-running appends a duplicate evidence line (verified: paint evidence ×2
  after second run, apps.json hash changed). Recommend dedupe + Unavailable
  preservation before live application.
- Catalog suite on the untouched live catalog: 83/83 passed (lease-held).
- Instability triage (no code touched; imported product/test files belong to
  other lanes). Matrix on the current tree: full suite crashes (5× EE
  0x80131506, varying sites/counts) or 322/323 with an order-flake (1×);
  Paint collection alone 6/7 no crash; PaintFailure 3/3; FocusCommit solo
  pass; **suite-minus-Paint-collection 276/276 green**. So the crash needs
  the Paint collection (PDN app boot arms the process-wide first-chance
  reporter hook in `EnsurePaintDotNetInitialized`, one-time guarded) IN
  suite context. Corroboration: the WF-R-NRE-0730 lane already hardened the
  formatter half (`FormatFirstChance` null-guards); unhardened remains the
  live `new StackTrace().GetFrames()` in the handler (the reporter's own
  comment: hostile frames abort runtimes uncatchably) and `HttpClient.SendAsync`
  on the dispatch thread. Recommended owner fix (WinForms/product lane):
  pass null frames in the first-chance handler and/or keep test-process
  reports console-only. Order-flakes (FocusCommit, BrushDrag fail full-only,
  pass solo) recorded separately as global-focus/pollution sensitivity.

## WD-1730-IMP (first-chance reporter hardening) — 2026-10-30T17:39Z

- Change (narrow, 2 imported files, recorded as DIFFS.md #8/#9; no test
  files touched): (1) `WinFormsAppHost.EnsurePaintDotNetInitialized`
  first-chance lambda no longer captures a live `StackTrace` on the dispatch
  thread — `FormatFirstChance(e.Exception, frames: null)` (null-tolerant by
  WF-R-NRE-0730 design, pinned by `FirstChanceFormatTests` null-frames
  cases); (2) new `BrowserErrorReporter.ReportConsole` (same console format
  and shared rate-gate budgets as `Report`, returns before any JS/HTTP
  transport; `Report` itself byte-identical); the lambda now calls it.
  Verified pre-edit: both files matched their import-manifest pins
  (`post-src.tsv`), so the diff baseline is exact; no maintained test
  asserts handler output or reporter member shape changed here (read before
  editing: `NothingIsMarkedWorking`, `FirstChance*`, `BrowserErrorReporterTests`
  all cover adjacent behavior only).
- Skill note: `bundled:durable-test-collateral` loaded pre-edit. No new
  committed test added: the slice explicitly forbids test edits, and the
  maintained coverage already exists — the 19 reporter/firstchance tests
  exercise the changed paths (`Report` unchanged, `FormatFirstChance`
  null-frames pinned). Stated here instead of a new test file.
- Evidence (lease-held `build-DesktopWasm.lock`; clean-bin rebuild, 0
  errors): focused reporter+firstchance suites **19/19 green** post-edit;
  full CanvasHost suite post-fix: **322/323** (known order-flake
  FocusCommit, fails full-only/passes solo, pre-existing), **323/323 clean
  in 25s**, plus 1 abort at 71 tests with a *different* stack (pure
  xunit-invoke frames, no reporter/product frames — truncated stack walk).
  Pre-fix on the same lineage: 5 aborts + 1 flake-pass in 6 runs, never 323.
- Reading, stated plainly: the evidenced reporter vector (first-chance →
  `GetFrames`/`SendAsync` → EE, run-#6 stack) is closed by construction —
  that code path no longer executes. A second, rarer vector persists (same
  binary went abort-then-323 across two runs: timing/environmental, likely
  native heap corruption surfacing wherever). Abort rate 5/6 → 1/3, with
  one full 323/323. Residual risk noted: 5 other live `GetFrames` call sites
  in click/hover handlers left untouched as out-of-scope; recommend the
  WinForms lane apply the same null-frames discipline + analyze a crash
  dump (`--blame-crash`) for the residual vector.
## WC-1740-IMP (dump analysis + 5-site null-frames guard) — 2026-10-30T17:47Z

- Dump analysis via the Windows event log (no new dump: the armed
  `--blame-crash` run went green). The residual abort's WER signature:
  `testhost.exe` APPCRASH, faulting module `coreclr.dll` 9.0.2126,
  exception `c0000005` (access violation) at offset `0x1118e4`, managed
  stack truncated at the reflection-invoke frame. Fault inside the runtime
  itself (not PDN/TerraFX/DWrite) + same-binary abort-then-green variance =
  native heap corruption surfacing wherever, crash site is the victim.
  Box context (WER flush timing uncertain, not conclusive): unrelated native
  processes also report AV/BEX64/hang around the same window.
  `dotnet-dump` is available on the box for a future abort dump if one is
  ever captured.
- Guard: the 5 remaining live `GetFrames()` sites in `WinFormsAppHost.cs`
  (form-create, mouse enter/leave, mouse event, control click, key handlers)
  now print `FormatFirstChance(..., frames: null).ConsoleLines` with the
  `[diagstack]` prefix instead of resolving method chains. Verified pre-edit:
  no test asserts `[diagstack]` content or console output. Verified
  post-edit: 0 `GetFrames()` remain in the product file (test files still
  use it — untouched per the no-test-edits constraint). Recorded as
  DIFFS.md #10.
- Evidence (lease-held; clean-bin rebuild, 0 errors): focused
  reporter+firstchance **19/19**; full suite **323/323** (36s) and a second
  **323/323** (18s, `--blame-crash` armed, dump utility attached, no abort).
  Post-guard abort rate 0/2 vs 1/3 immediately pre-guard and 5/6 pre-fix;
  small samples on an intermittent fault — stated as observed counts, not
  a rate proof.

