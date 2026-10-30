using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DesktopWasm.Host.Server;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace DesktopWasm.Shell.Tests;

/// <summary>
/// In-process Kestrel host for the unified shell with synthetic fixture apps
/// only (test-* ids). Fixtures exercise registry mechanics; they never
/// impersonate real upstream apps.
/// </summary>
public sealed class ShellHost : IAsyncLifetime
{
    private WebApplication? _app;

    public string CatalogDir { get; } = Path.Combine(Path.GetTempPath(), "dwshell-" + Guid.NewGuid().ToString("N"));
    public string PackagesRoot => Path.Combine(CatalogDir, "packages");
    public string LogDir => Path.Combine(CatalogDir, "logs");
    public string WebRoot => Path.Combine(CatalogDir, "wwwroot");
    public HttpClient Client { get; private set; } = new();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string FixtureCatalog() => """
        [
          {"id":"test-color-lab","aliases":[],"label":"Color lab (fixture)","description":"Fixture color explorer","category":"Fixtures","frameworks":["wpf"],"kind":"demo","status":"ready","available":true,"gaps":[],"provenance":null,"launch":{"route":"/launch/test-color-lab","legacyRoutes":["/demo/test-color-lab"]}},
          {"id":"test-gif-recorder","aliases":[],"label":"Recorder (fixture)","description":"Fixture unavailable app","category":"Fixtures","frameworks":["wpf"],"kind":"original","status":"not-converted","available":false,"gaps":["capture APIs unavailable in browser"],"provenance":{"sourceUrl":"https://example.invalid/repo","repository":"https://example.invalid/repo","commit":"abc123","project":"Rec/Rec.csproj","license":"MIT","boundary":"Fixture boundary note."},"launch":{"route":"/launch/test-gif-recorder","legacyRoutes":[]}},
          {"id":"test-mixed-fixture","aliases":["test-mixedfixture"],"label":"Mixed fixture","description":"Fixture declaring both frameworks","category":"Fixtures","frameworks":["winforms","wpf"],"kind":"demo","status":"ready","available":true,"gaps":[],"provenance":null,"launch":{"route":"/launch/test-mixed-fixture","legacyRoutes":[]}},
          {"id":"test-smoke-form","aliases":["test-smokeform"],"label":"Smoke form (fixture)","description":"Fixture smoke form","category":"Fixtures","frameworks":["winforms"],"kind":"demo","status":"ready","available":true,"gaps":[],"provenance":null,"launch":{"route":"/launch/test-smoke-form","legacyRoutes":["/app/test-smokeform"]}}
        ]
        """;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(CatalogDir);
        Directory.CreateDirectory(PackagesRoot);
        Directory.CreateDirectory(WebRoot);
        await File.WriteAllTextAsync(Path.Combine(CatalogDir, "apps.json"), FixtureCatalog());
        await File.WriteAllTextAsync(Path.Combine(WebRoot, "index.html"),
            "<!doctype html><html><head><title>portal</title></head><body>portal</body></html>");

        // One staged package: deterministic bytes for hash pinning.
        string pkg = Path.Combine(PackagesRoot, "test-smoke-form");
        Directory.CreateDirectory(pkg);
        await File.WriteAllBytesAsync(Path.Combine(pkg, "entry.bin"), [1, 2, 3, 4]);
        await File.WriteAllTextAsync(Path.Combine(pkg, "app.json"), """{"entry":"test-smoke-form"}""");

        var app = ShellApp.Create(new ShellOptions(
            CatalogPath: Path.Combine(CatalogDir, "apps.json"),
            PackagesRoot: PackagesRoot,
            LogDir: LogDir,
            ContentRootPath: CatalogDir,
            WebRootPath: WebRoot));
        app.Urls.Clear();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        _app = app;
        Client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        if (_app is not null)
            await _app.DisposeAsync();
        try { Directory.Delete(CatalogDir, recursive: true); } catch { }
    }

    public static JsonElement Body(JsonElement root, string property) => root.GetProperty(property);
}

[CollectionDefinition("shell-host")]
public sealed class ShellHostCollection : ICollectionFixture<ShellHost>;
