# One DesktopWasm workspace, catalog and mixed application runtime

Coordinator proposal and execution direction, 2026-10-30. User authorized proceeding with import and a new cohort. This is the intended architecture, not a claim that integration is already complete.

## Source review

Both stacks target net9.0; both browser clients use Microsoft.AspNetCore.Components.WebAssembly9.0.20, and the Skia projects inspected use4.153.1. Preserve these aligned versions initially; do not upgrade during the merge. Existing project names are largely disjoint, so preserve names and namespaces instead of generating a large rename diff.

WinForms source: WfWasm.sln; SceneState.cs owns UI entries; WfWasm.Host.Server/Program.cs exposes preview-config and on-demand manifest/assembly/content/error APIs; AppTransform.cs owns transform specs, source overrides and caches. There are absolute staging and original-project paths, and CanvasHost references a locally staged WfWasm.WinForms.dll outside the repo. Those inputs must be inventoried and pinned/copied or explicitly marked unavailable, not silently resolved from mutable old workspaces.

WPF source: DesktopWasm.slnx with Core, Graphics, Wpf, Convert, BrowserHost and tests. BrowserHost/wwwroot/app-library.json is an external app listing separate from DemoCatalog; Home.razor fetches packages. Core/ApplicationPackage.cs already provides version/hash/closure validation; Convert/ApplicationPackager.cs converts a dependency closure; Wpf/ApplicationPackageLoader.cs handles hash-verified assembly fetch and resolution. Preserve those validation behaviors.

InteropHosts.cs currently exposes IImmediatePaintable, WindowsFormsHost.Child:IImmediatePaintable and a plain ElementHost producing WPF draw commands. They do not directly host real WfWasm.WinForms.Control instances or transfer focus/capture/keyboard/disposal across both stacks. A merged menu alone cannot satisfy mixed applications. Assembly conversion currently has framework-specific maps/loaders; combining outputs blindly risks type/assembly identity splits, ordering-dependent rewrites and duplicate runtime payloads (notably WindowsBase).

Concrete overlap: DesktopWasm.Convert/WpfTypeMap.cs already includes System.Windows.Forms and Microsoft.Win32.SystemEvents in SourceAssemblies for WPF platform prerequisites (including ScreenToGif), while WinForms has real WfWasm.WinForms and a separate Microsoft.Win32.SystemEvents project. The composed policy must route actual WinForms controls to the real fork and deliberately reconcile platform shim identities/events. Do not retain two incompatible definitions for the same source API or automatically let the WPF shim map win. This is a first integration test, not a deferred cleanup.

PlatformShims.cs actually declares types in the System.Windows.Forms namespace inside DesktopWasm.Wpf, not merely map entries. Referencing both engine assemblies can therefore create compile-time ambiguity AND runtime identity divergence. Interop/packaging must inventory the duplicate full type names and choose one canonical contract/provider per shared API; explicit aliases may isolate the transition but are not a complete identity policy. Retain standalone imported test coverage and report behavior differences before replacing shim implementations.

Recent work must be retained: WPF DP F2/F3 integration and apps IsSelected/attach/coercion fixes; runtime FF2 transport/reporting, Escape discriminators and perf experiments. Freeze handoffs determine which changes are shared vs isolated. Known failures remain tracked; do not label a copied snapshot green.

## Proposed layout

```
DesktopWasm/
  DesktopWasm.slnx                 all first-party imported + new projects
  src/
    DesktopWasm.AppModel/          catalog/package/session contracts, no engine dependency
    DesktopWasm.Catalog/           one app registry + validation + legacy import adapters
    DesktopWasm.Interop/           adapters joining real WinForms controls and WPF visuals
    DesktopWasm.Host.Server/       one public origin/API/static asset gateway, port5220
    DesktopWasm.Web/               one catalog and launcher, runtime adapters
    DesktopWasm.{Core,Graphics,Wpf,Convert,BrowserHost}/
    WfWasm.*/                     existing first-party projects, names preserved
    Microsoft.Win32.SystemEvents/ existing auxiliary project
  tests/                          imported suites + catalog/package/mixed-runtime tests
  catalog/apps.json               single authoritative catalog after adapter import
  scripts/{winforms,wpf,unified}/  copied scripts, roots/config made relocatable
  inputs/{winforms,wpf,shared}/    pinned third-party/source/runtime inputs + provenance
  artifacts/                      generated packages, outputs, captures and receipts
  docs/{architecture,migration,legacy}/
```

Old browser hosts remain engine adapters/test harnesses during transition. The public experience must have one catalog and launcher. Two separate runtimes behind a common navigation page are a transitional pure-app path only, never acceptance for mixed hosting. Preserve originals; migration is copy-first. Do not copy old .git metadata, output caches or accidental AGENTS.md policies into the active root.

## Shared boundaries

- AppModel has no WPF/WinForms/ASP.NET dependency. App descriptor: stable ID, legacy aliases, display metadata/provenance, entry point, capabilities set (winforms/wpf, both allowed), runtime/transform version, resource/dependency closure, availability and concrete gaps. Existing source IDs must have an explicit collision map; don't discard apps or arbitrarily relabel converted as working.
- Catalog consumes source adapters once into one validated registry; after migration UI/API/build preparation read the same registry. Avoid parallel hand-maintained lists. Preserve native demos separately identified as demos, original converted apps as originals.
- Package pipeline composes both transform maps over one dependency graph with explicit shared-assembly policy. Resolve by logical identity plus version/content hash; reject conflicts visibly. A mixed assembly receives both mappings without clobbering the other's types, signatures, XAML/BAML, resources or events. Manifest validation and content hashing remain mandatory.
- App session owns one dispatch/input lifecycle and shared logical dependencies for a mixed app. Runtime adapter handles initialize/mount/resize/input/render/dispose; host presentation does not own framework-specific logic. Shared rendering starts with explicit surface/texture exchange; do not pretend two existing command-list formats are interchangeable or require a premature graphics rewrite.
- Interop provides real WindowsFormsHost and ElementHost compatibility in both directions, correct sizing/clipping/DPI, invalidation, coordinate/input translation, focus/tab/capture, lifetime and exception reporting. Keep dependencies acyclic: shared contracts below engines; adapters may reference both engines, neither engine references adapters. Converter bridge names must match actual expected source API contracts.
- One public gateway/catalog/error inventory and hashed package delivery. Pure apps may initially mount imported hosts behind adapters; mixed acceptance requires both frameworks inside one app session, not iframes/process isolation as a substitute. Pin Skia/native assets/WindowsBase once per runtime and prove no facade or identity split.

## Migration and acceptance sequence

1. Stop old routing; checkpoint/stop old workers and owned children; freeze source writes. Inventory both source roots plus required vendor inputs from local staging, missing staged dependencies and unmerged isolated changes. No source writes/copy races.
2. Copy first-party source/tests and required assets/converted inputs to this root. Preserve relative project references; partition colliding script/docs/input paths. Exclude disposable bin/obj/cache/.git. Preserve intentionally checked/staged DLL/bin assets under source and required target outputs. Produce count/hash manifest, exclusions, provenance, duplicate-path/conflict report and mapped external roots. Copy only relevant upstream/runtime trees after size/usage inventory; archival surveys may remain explicitly linked read-only and are not runtime dependencies. No hardlinks to mutable originals or silent old-root fallbacks. Quarantine unmerged experiments with diffs rather than auto-applying them.
3. Produce unified solution/build entry, relocatable roots/config, imported-suite baseline and explicit inherited failure inventory. Existing SDK/workload requirements must be reproducible. Initial copy gate passes only if source manifests/counts and representative build inputs are verified. No 'ready' marker on partial copy.
4. Merge catalogs and adapters; prove listing/selection/availability and package integrity for both families from one public entry. Preserve original-app workflows, not only demo landing pixels.
5. Implement shared conversion closure and interop in both directions with failing-first tests. Use real controls/visuals and a small mixed original-API fixture plus at least one upstream mixed app if available. Test interaction/state/lifetime, not paint-only mocks.
6. Independent verification: pure WinForms representative, pure WPF representative, WPF-containing-WinForms, WinForms-containing-WPF, a mixed assembly referencing both frameworks, input/focus/capture/dispose, resize/DPI, dependency conflicts/integrity errors. Record exact source/package/served hashes, console errors, screenshots and user interactions. Full inherited suites still required with bounded hang/crash evidence, never silently skipped.

Copy/bootstrap and active new cohort are this handover's immediate deliverables. End-to-end mixed compatibility remains in progress until those runtime gates pass. Ops owns follow-ups and safe context rotations under the existing 30-minute watchdog and twice-daily review policy, retargeted to this root after transition.
