// Unified shell gateway: one catalog + launcher for WinForms and WPF on :5220.
// Run: dotnet run --project src/DesktopWasm.Host.Server --urls http://127.0.0.1:5220
// Catalog registry: catalog/apps.json (MERGE-CATALOG lane owns the file);
// override with --Catalog:Path <file>, DESKTOPWASM_CATALOG, or appsettings.json.
using DesktopWasm.Host.Server;

string? catalogArg = args.FirstOrDefault(a => a.StartsWith("--Catalog:Path=", StringComparison.Ordinal))?["--Catalog:Path=".Length..];
var app = ShellApp.Create(new ShellOptions(
    CatalogPath: catalogArg,
    WebRootPath: Environment.GetEnvironmentVariable("DESKTOPWASM_WEBROOT")), args);
app.Run();
