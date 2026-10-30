# DesktopWasm.Host.Server — unified shell gateway (MERGE-SHELL lane, port 5220)

One catalog API + launcher portal for both WinForms and WPF apps, read from the
canonical `catalog/apps.json` registry (owned by the MERGE-CATALOG lane).

## Build and start

```powershell
dotnet publish E:\AI\Code\DesktopWasm\src\DesktopWasm.Web\DesktopWasm.Web.csproj -o $env:TEMP\dwshell-live\web
$env:DESKTOPWASM_WEBROOT = "$env:TEMP\dwshell-live\web\wwwroot"
dotnet run --project E:\AI\Code\DesktopWasm\src\DesktopWasm.Host.Server --urls http://127.0.0.1:5220
```

(Serve the *published* portal: plain `bin/Debug` output omits `index.html`.
`DESKTOPWASM_WEBROOT` may also point at any staged portal directory.)

Then open `http://127.0.0.1:5220/` in a browser.

Catalog path resolution: `--Catalog:Path <file>`, else `DESKTOPWASM_CATALOG`,
else upward search for `catalog/apps.json`. Without a registry the API returns
`503 catalog-unavailable` (honest gap, never a fake list).

## Tests

```powershell
dotnet test E:\AI\Code\DesktopWasm\tests\DesktopWasm.Shell.Tests\DesktopWasm.Shell.Tests.csproj
```
