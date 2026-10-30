# Optional inputs (not shipped)

This directory ships only this README. Nothing under `inputs/` is required
for a clean source-only build; every consumer degrades gracefully (see the
top-level README).

## Layout expected by tooling (stage locally)

- `inputs/winforms/runtimes/winforms/src` — .NET Windows Forms fork source
  used by conversion tooling. Pinned revision: see the catalog provenance
  (`dotnet/winforms` at `a84cf1f1...`, MIT).
- `inputs/winforms/targets/paint.net` — pristine Paint.NET closure inputs
  (e.g. `WindowsBase.dll` for
  `scripts/winforms/ensure-windowsbase-full.ps1`). Separately obtained;
  never commit binaries here in a public clone.
- `inputs/<family>/...` — additional family inputs as lanes document them.
- Converted preview applications: place under
  `src/DesktopWasm.BrowserHost/ConvertedApps/<App>/<App>.dll` plus
  `<App>.receipt.json`; the build stages them only when present.

## Strictness

Default builds skip absent optional inputs with a warning. Set
`DESKTOPWASM_STRICT_INPUTS=1` (or pass
`-p:DesktopWasmFullWindowsBase=true` for the full-copy path) to turn absent
inputs into hard errors, e.g. in conversion pipelines where a silent skip
would be a defect.
