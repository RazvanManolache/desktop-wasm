# DesktopWasm — WinForms and WPF applications on WebAssembly

One workspace, one catalog, and one launcher for WinForms and WPF
applications running on WebAssembly. Real mixed-framework hosting (both UI
stacks inside one app session) is work in progress.

**Conversion starts from compiled .NET binaries.** DesktopWasm takes an
application's managed `.exe` and `.dll` files, together with its dependencies
and resources, and rewrites their .NET intermediate language (IL) for the
browser-hosted compatibility runtime. This works with compiled applications
even when their original source code is unavailable.

The conversion pipeline is language-independent. It accepts managed .NET IL
produced by C#, VB.NET, F# or any other .NET language. Current UI support focuses
on WinForms and WPF. Successful execution depends on the application's API
usage, native dependencies and runtime compatibility coverage.

**[Try the live browser showcase](https://razvanmanolache.github.io/desktop-wasm/)** —
four WPF mini apps plus the existing converted WinForms Calculator, To-do and
Tic-Tac-Toe applications. The runtime loads when you open a demo. The site also
shows local screenshots of larger applications with links to build them.
Converted showcase inputs are pinned by checksum in
[showcase-inputs.json](scripts/ci/showcase-inputs.json), with upstream attribution
and available license texts in the input release.

**Working demos (screenshots of tested builds, staged vs public labeled):**

| | |
|---|---|
| <a href="docs/progress/images/word-type-public-gen43.png"><img src="docs/progress/images/word-type-public-gen43.png" width="400" alt="Word 2016 demo with Hello World typed at the document origin"/></a><br/>**Word 2016 (public)** — typed “Hello World” at the doc origin | <a href="docs/progress/images/rte-type-shell63.png"><img src="docs/progress/images/rte-type-shell63.png" width="400" alt="Telerik rich-text editor with inserted text after Overview"/></a><br/>**Telerik RichTextEditor (staged)** — typed text inserted in the document |
| <a href="docs/progress/images/sf01-popup-shell63.png"><img src="docs/progress/images/sf01-popup-shell63.png" width="400" alt="AutoComplete suggestion popup showing United Kingdom after typing Uni"/></a><br/>**Syncfusion AutoComplete (staged)** — typing “Uni” opened the United Kingdom suggestion | <a href="docs/progress/images/wpf-nav-shell63.png"><img src="docs/progress/images/wpf-nav-shell63.png" width="400" alt="WPF gallery navigating to a sample page after tree selection"/></a><br/>**WPF Gallery (staged)** — tree selection navigated to the sample page |
| <a href="docs/progress/images/01-chart-stacking-column.png"><img src="docs/progress/images/01-chart-stacking-column.png" width="400" alt="Chart repainted from grouped to stacked columns after a ChartType pick"/></a><br/>**Syncfusion Chart (staged)** — ChartType pick repainted stacked columns | <a href="docs/progress/images/paint-twist-shell63.png"><img src="docs/progress/images/paint-twist-shell63.png" width="400" alt="Paint.NET with a freehand stroke and the Twist settings dialog open"/></a><br/>**Paint.NET 5.1.12 (staged)** — stroke drawn, Twist dialog opened (effect output still unverified) |

Full scope, remaining limitations, and attribution: [progress 2026-11-03](docs/progress/2026-11-03.md).

**Latest source progress:** [6 November integration fixes and validation](docs/progress/2026-11-06.md).
Download replaceable development builds from [ci-latest](https://github.com/RazvanManolache/desktop-wasm/releases/tag/ci-latest).

| November 6: tested local builds | |
|---|---|
| <a href="docs/progress/images/planner-20261106.png"><img src="docs/progress/images/planner-20261106.png" width="400" alt="Project planner rendered after mounted WPF app navigation"/></a><br/>**WPF planner** — app navigation retains the `/wpf` mount | <a href="docs/progress/images/paint-colors-less-20261106.png"><img src="docs/progress/images/paint-colors-less-20261106.png" width="400" alt="Paint.NET Colors window after clicking Less, with the RGB controls clipped"/></a><br/>**Paint.NET Colors** — More/Less sizing and clipping verified |

Origin: this repository is the successor of
[winforms-wasm](https://github.com/RazvanManolache/winforms-wasm). Standalone
WinForms development ends at that repository's final checkpoint; future
development and releases happen here.

## Status (honest)

- The unified catalog (`catalog/apps.json`) lists 140 entries across both
  families with provenance and per-entry availability. Entries whose inputs
  are not staged in a given checkout report `missing-input`,
  `not-converted`, or `unavailable` — never ready. A source-only clone
  therefore launches no converted upstream app until its inputs are staged
  (see `inputs/README.md`).
- Pure-framework paths are the proven slice. Mixed sessions, full control
  parity, and performance work remain open and are tracked as gaps, not
  claims. Replaceable development downloads are published under `ci-latest`;
  these previews are separate from any future stable versioned release.

## Layout

- `src/DesktopWasm.AppModel/` — catalog/package/session contracts, no engine
  dependency.
- `src/DesktopWasm.Catalog/` — one app registry, validation, legacy import
  adapters, deterministic JSON.
- `src/DesktopWasm.Catalog.Tool/` — `import` (frozen sources → catalog),
  `verify` (drift check), `refresh` (staging receipts → availability).
- `src/DesktopWasm.Interop/`, `src/DesktopWasm.Packaging/` — mixed-session
  adapters and the joint conversion closure.
- `src/DesktopWasm.Host.Server/`, `src/DesktopWasm.Web/` — one public
  catalog API/launcher (serves the unified registry).
- `src/WfWasm.*`, `src/DesktopWasm.{BrowserHost,Convert,Core,Graphics,Wpf}`,
  `src/Microsoft.Win32.SystemEvents/` — imported first-party engines and
  hosts (names preserved).
- `tests/` — imported suites plus catalog/package/mixed-runtime tests.
- `catalog/` — `apps.json` (authoritative registry), `apps.schema.json`,
  `conflict-map.json`, `sources/` (redacted extraction receipts).
- `scripts/{unified,winforms,wpf}/` — relocatable build/packaging scripts.
- `docs/architecture/` — `UNIFICATION.md`, `CATALOG-CONTRACT.md`.
- `docs/progress/` — dated progress pages with screenshot evidence.
- `inputs/winforms/runtimes/` — required .NET WinForms fork source and
  resources (MIT, see `inputs/README.md`). Shipped as source; its binaries
  are never included.
- `inputs/` (everything else) — NOT shipped: optional app closures,
  commercial/vendor trees and generated binaries are excluded (see
  `inputs/README.md`).

## Source-only build

Requires the .NET 9+ SDK plus the `wasm-tools` workload for the browser
hosts (`dotnet workload install wasm-tools`). No other bootstrap is needed;
NuGet restore fetches declared packages separately.

```powershell
dotnet build DesktopWasm.slnx
dotnet test tests/DesktopWasm.Catalog.Tests/DesktopWasm.Catalog.Tests.csproj
```

Build evidence for publication is produced from staged-export trees like
this one. The project-local `.tools` interpreter is not shipped: a
plain source build needs no Python, but the optional packaging step
(static precompression of served assets) requires a standalone CPython
3.14 at the repo-relative path `.tools/python314/python.exe`, with
Brotli installed for that interpreter. A virtualenv or global Python
does not satisfy the packaging resolver. One-time setup from the repo
root, after installing the official Python install manager from
[python.org](https://www.python.org/downloads/) (see the
[official Windows runtime documentation](https://docs.python.org/3.14/using/windows.html#installing-runtimes)):
no global registration is needed — the `--target` option extracts the
runtime to the chosen directory:

```powershell
py install --target .tools/python314 3.14
& ./.tools/python314/python.exe -m ensurepip --upgrade
& ./.tools/python314/python.exe -m pip install Brotli
& ./scripts/shell/Resolve-LocalPython.ps1
```

Bootstrapping needs the installer/manager, but all project Python
execution is the qualified local exe — no runtime binaries are
shipped. This is documented setup, not a claim that a fresh-machine
installer was tested here; the .NET source build validation above is
the actual tested result.

Optional capabilities are opt-in and never required for a clean build:

- Full WindowsBase / Paint.NET closure:
  `dotnet build -p:DesktopWasmFullWindowsBase=true` (needs
  `inputs/winforms/targets/paint.net`, staged locally, otherwise strict fail).
- Converted preview apps (`ConvertedApps/*`): staging is skipped when the
  inputs are absent; catalog entries stay `missing-input`. No stubs.

See `inputs/README.md` for the optional-input layout and recipes.

## License

First-party code: [MIT](LICENSE) (winforms-wasm contributors). Vendored
browser libraries and upstream references: see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). No third-party application
binaries are distributed here.
## GitHub builds

Open **Actions → Browser demo build → Run workflow**. Pushes and pull requests
also build. Download `DesktopWasm-browser-demos`, extract the inner ZIP and run
`python serve_demo.py`, then open **http://127.0.0.1:8080**. The package includes
35 first-party toolkit demos, compiled WebAssembly, licenses and checksums.

CI compiles the full solution, runs eleven source-only test suites, and checks
that the packaged calculator renders in Chromium. The screenshot, test reports
and compiler log are available in `DesktopWasm-build-reports`.

**The older engine still has compiler warnings and tests requiring unavailable
inputs.** Catalog/shell builds reject warnings; the full engine does not yet.
See [build scope and remaining work](docs/GITHUB-BUILDS.md). Converted external
apps and the unified WinForms backend are not included in this demo ZIP.
