using DesktopWasm.AppModel;
using DesktopWasm.Catalog;
using DesktopWasm.Host.Server;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace DesktopWasm.Shell.Tests;

/// <summary>
/// In-process Kestrel host for the unified shell with synthetic fixture apps
/// only (test-* ids) built as shared <see cref="AppDescriptor"/> records and
/// serialized with <see cref="CatalogSerializer"/>. Fixtures exercise registry
/// mechanics; they never impersonate real upstream apps.
/// </summary>
public sealed class ShellHost : IAsyncLifetime
{
    private WebApplication? _app;

    public string CatalogDir { get; } = Path.Combine(Path.GetTempPath(), "dwshell-" + Guid.NewGuid().ToString("N"));
    public string PackagesRoot => Path.Combine(CatalogDir, "packages");
    public string LogDir => Path.Combine(CatalogDir, "logs");
    public string WebRoot => Path.Combine(CatalogDir, "wwwroot");
    public HttpClient Client { get; private set; } = new();

    public static IReadOnlyList<AppDescriptor> FixtureDescriptors() => new[]
    {
        new AppDescriptor(
            "test-color-lab", ["/demo/test-color-lab"],
            "Color lab (fixture)", "Fixture color explorer", "Fixtures", "",
            new AppProvenance("wpf", "native-demo", null, null, null, null, null, null, false),
            new AppEntry("native-demo", "test-color-lab", null, null),
            AppCapability.Wpf, "wpf-convert-v1", null,
            AppAvailability.MissingInput, "ready",
            "Engine-native demo; staged host pending.",
            [], ["Engine-native demo; staged host pending."]),
        new AppDescriptor(
            "test-gif-recorder", [],
            "Recorder (fixture)", "Fixture unavailable app", "Fixtures", "",
            new AppProvenance("wpf", "original", "https://example.invalid/repo",
                "https://example.invalid/repo", "abc123", "Rec/Rec.csproj", "MIT",
                "Fixture boundary note.", true),
            new AppEntry("converted-app", "Rec", null, "No package staged."),
            AppCapability.Wpf, "wpf-convert-v1", null,
            AppAvailability.NotConverted, "not-converted",
            "Fixture boundary note.", [], ["Fixture boundary note."]),
        new AppDescriptor(
            "test-mixed-fixture", ["test-mixedfixture"],
            "Mixed fixture", "Fixture declaring both frameworks", "Fixtures", "",
            new AppProvenance("mixed", "pilot", null, null, null, null, null,
                "Fixture pilot; no bridge proven.", false),
            new AppEntry("mixed-session", "MixedPilot", null, "Fixture pilot entry."),
            AppCapability.Mixed, "mixed-v1", null,
            AppAvailability.MissingInput, "pilot",
            "Proven mixed session pending (interop lane).",
            [], ["Proven mixed session pending (interop lane)."]),
        new AppDescriptor(
            "test-smoke-form", ["test-smokeform", "/app/test-smokeform"],
            "Smoke form (fixture)", "Fixture smoke form", "Fixtures", "",
            new AppProvenance("winforms", "original", null, null, null, null, null, null, false),
            new AppEntry("native-demo", "test-smoke-form", null, null),
            AppCapability.WinForms, "wf-rewrite-v1", null,
            AppAvailability.MissingInput, "ready",
            "App folder 'test-smoke-form' not staged in this root.",
            [], ["App folder 'test-smoke-form' not staged in this root."]),
    };

    public static string FixtureCatalog() => CatalogSerializer.Serialize(FixtureDescriptors());

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
}

[CollectionDefinition("shell-host")]
public sealed class ShellHostCollection : ICollectionFixture<ShellHost>;
