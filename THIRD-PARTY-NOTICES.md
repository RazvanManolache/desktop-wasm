# Third-party notices

The root [MIT license](LICENSE) applies to original project code. Existing copyright headers and component licenses continue to apply to adapted or vendored material.

## Vendored source and browser libraries

| Component | Version or source | License and notice |
| --- | --- | --- |
| .NET Windows Forms fork source | [`dotnet/winforms` at `a84cf1f1da5530adca2598d57b58063228e3353f`](https://github.com/dotnet/winforms/tree/a84cf1f1da5530adca2598d57b58063228e3353f) | MIT ([local copy](inputs/winforms/runtimes/LICENSE.TXT)), [.NET third-party notices](inputs/winforms/runtimes/THIRD-PARTY-NOTICES.TXT). Shipped as source under `inputs/winforms/runtimes/`; fork binaries are NOT shipped. |
| Bootstrap | 5.3.3 (`src/WfWasm.CanvasHost/wwwroot/lib/bootstrap`) | [MIT](licenses/bootstrap-LICENSE.txt), [upstream](https://github.com/twbs/bootstrap/blob/v5.3.3/LICENSE). |
| PixiJS | 7.4.3 (`src/WfWasm.CanvasHost/wwwroot/lib/pixi/pixi.min.js`) | [MIT](licenses/pixijs-LICENSE.txt), [upstream](https://github.com/pixijs/pixijs/blob/v7.4.3/LICENSE). |
| PixiJS Filters | 5.3.0 (`src/WfWasm.CanvasHost/wwwroot/lib/pixi/pixi-filters.js`) | [MIT](licenses/pixi-filters-LICENSE.txt), [upstream](https://github.com/pixijs/filters/blob/v5.3.0/LICENSE). |

Adapted .NET source files outside the vendored tree retain their original notices and are covered by the applicable .NET license.

## Restored dependencies

Project files declare NuGet dependencies, including Mono.Cecil, .NET browser hosting packages, and test tooling. Package restore obtains them separately; their own licenses apply. The source distribution does not include restored package binaries.

## Applications referenced by the catalog

The catalog (`catalog/apps.json`, 139 entries) describes WinForms and WPF applications, including third-party samples (Paint.NET, Syncfusion and Telerik demos, WPF-Samples). No third-party application, executable, installer, converted assembly, commercial control library, or sample asset is distributed in this repository. Catalog entries carry provenance (repository, commit, license) and honest availability: entries whose inputs are not staged here report `missing-input` / `not-converted` / `unavailable`, never ready. Screenshots or captures, if any, document local experiments and do not grant redistribution rights or imply vendor affiliation.
