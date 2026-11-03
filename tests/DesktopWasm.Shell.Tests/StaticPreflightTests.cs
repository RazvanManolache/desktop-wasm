using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DesktopWasm.Host.Server;
using Xunit;

namespace DesktopWasm.Shell.Tests;

/// <summary>Missing-static-asset preflight: a deployment missing portal, WPF
/// or catalog bytes must fail visibly (503 + missing list), never serve a
/// silent black canvas or MIME-rejected fallback as if it were the app.</summary>
public sealed class StaticPreflightTests
{
    private static string NewStagedTree(out string webRoot, out string wpfRoot, out string catalogPath)
    {
        string dir = Path.Combine(Path.GetTempPath(), "dwpreflight-" + Guid.NewGuid().ToString("N"));
        webRoot = Path.Combine(dir, "wwwroot");
        wpfRoot = Path.Combine(dir, "wpfroot");
        Directory.CreateDirectory(webRoot);
        Directory.CreateDirectory(Path.Combine(webRoot, "_framework"));
        Directory.CreateDirectory(Path.Combine(wpfRoot, "_framework"));
        File.WriteAllText(Path.Combine(webRoot, "index.html"), "<!doctype html><html></html>");
        File.WriteAllText(Path.Combine(webRoot, "_framework", "blazor.webassembly.js"), "// loader");
        File.WriteAllText(Path.Combine(webRoot, "_framework", "blazor.boot.json"), """{"resources":{}}""");
        File.WriteAllText(Path.Combine(wpfRoot, "index.html"), "<!doctype html><html></html>");
        File.WriteAllText(Path.Combine(wpfRoot, "_framework", "blazor.boot.json"), """{"resources":{}}""");
        File.WriteAllText(Path.Combine(wpfRoot, "_framework", "blazor.webassembly.js"), "// loader");
        File.WriteAllText(Path.Combine(wpfRoot, "entry.mjs"), "export default {};");
        catalogPath = Path.Combine(dir, "apps.json");
        File.WriteAllText(catalogPath, ShellHost.FixtureCatalog());
        return dir;
    }

    [Fact]
    public void Source_only_portal_is_not_a_ready_deployment()
    {
        string dir = NewStagedTree(out string web, out string wpf, out string catalog);
        try
        {
            Directory.Delete(Path.Combine(web, "_framework"), recursive: true);
            Assert.Equal(["portal:_framework/blazor.webassembly.js", "portal:_framework/blazor.boot.json"],
                ShellApp.StaticPreflight(web, wpf, catalog));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Boot_manifest_without_its_payload_is_not_ready()
    {
        string dir = NewStagedTree(out string web, out string wpf, out string catalog);
        try
        {
            File.WriteAllText(Path.Combine(web, "_framework", "blazor.boot.json"),
                """{"resources":{"assembly":{"client.123.wasm":"sha256-AAAA"},"satelliteResources":{"fr":{"client.resources.wasm":"sha256-BBBB"}}}}""");
            Assert.Equal(["portal:_framework/client.123.wasm", "portal:_framework/fr/client.resources.wasm"],
                ShellApp.StaticPreflight(web, wpf, catalog));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Staged_tree_reports_no_missing_assets()
    {
        string dir = NewStagedTree(out string web, out string wpf, out string catalog);
        try
        {
            Assert.Empty(ShellApp.StaticPreflight(web, wpf, catalog));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public void Missing_portal_index_is_listed()
    {
        string dir = NewStagedTree(out string web, out string wpf, out string catalog);
        try
        {
            File.Delete(Path.Combine(web, "index.html"));
            Assert.Equal(["portal:index.html"], ShellApp.StaticPreflight(web, wpf, catalog));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public void Missing_wpf_root_lists_all_three_wpf_entries()
    {
        string dir = NewStagedTree(out string web, out _, out string catalog);
        try
        {
            Assert.Equal(
                ["wpf:index.html", "wpf:_framework/blazor.boot.json", "wpf:entry.mjs"],
                ShellApp.StaticPreflight(web, null, catalog));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public void Missing_catalog_is_listed()
    {
        string dir = NewStagedTree(out string web, out string wpf, out _);
        try
        {
            var missing = ShellApp.StaticPreflight(
                web, wpf, Path.Combine(dir, "no-such-apps.json"));
            Assert.Equal(["catalog:apps.json"], missing);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public async Task Preflight_endpoint_is_200_when_staged_and_503_when_broken()
    {
        string dir = NewStagedTree(out string web, out string wpf, out string catalog);
        var shell = ShellApp.Create(new ShellOptions(
            CatalogPath: catalog,
            PackagesRoot: Path.Combine(dir, "packages"),
            LogDir: Path.Combine(dir, "logs"),
            ContentRootPath: dir,
            WebRootPath: web,
            WinFormsBackendUrl: "http://127.0.0.1:1",
            WpfWebRootPath: wpf));
        shell.Urls.Clear();
        shell.Urls.Add("http://127.0.0.1:0");
        await shell.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(shell.Urls.First()) };
            using var ok = await client.GetAsync("api/static-preflight");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            using var okDoc = JsonDocument.Parse(await ok.Content.ReadAsStringAsync());
            Assert.True(okDoc.RootElement.GetProperty("ok").GetBoolean());

            using var health = await client.GetAsync("api/healthz");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            using var hdoc = JsonDocument.Parse(await health.Content.ReadAsStringAsync());
            Assert.True(hdoc.RootElement.GetProperty("staticOk").GetBoolean());
            Assert.Equal(JsonValueKind.Array, hdoc.RootElement.GetProperty("staticMissing").ValueKind);
            Assert.True(hdoc.RootElement.GetProperty("portalStaged").GetBoolean());
            Assert.True(hdoc.RootElement.GetProperty("wpfManifestStaged").GetBoolean());
            Assert.True(hdoc.RootElement.GetProperty("wpfEntryStaged").GetBoolean());
        }
        finally { await shell.DisposeAsync(); }

        // Break the staged tree: remove the WPF boot manifest behind the
        // same catalog/portal, and confirm the endpoint fails visibly.
        File.Delete(Path.Combine(wpf, "_framework", "blazor.boot.json"));
        var broken = ShellApp.Create(new ShellOptions(
            CatalogPath: catalog,
            PackagesRoot: Path.Combine(dir, "packages"),
            LogDir: Path.Combine(dir, "logs"),
            ContentRootPath: dir,
            WebRootPath: web,
            WinFormsBackendUrl: "http://127.0.0.1:1",
            WpfWebRootPath: wpf));
        broken.Urls.Clear();
        broken.Urls.Add("http://127.0.0.1:0");
        await broken.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(broken.Urls.First()) };
            using var response = await client.GetAsync("api/static-preflight");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(body.GetProperty("ok").GetBoolean());
            Assert.Equal("static-asset-missing", body.GetProperty("error").GetString());
            Assert.Contains("wpf:_framework/blazor.boot.json",
                body.GetProperty("missing").EnumerateArray().Select(e => e.GetString()));

            using var health = await client.GetAsync("api/healthz");
            using var hdoc = JsonDocument.Parse(await health.Content.ReadAsStringAsync());
            Assert.False(hdoc.RootElement.GetProperty("staticOk").GetBoolean());
            Assert.Contains("wpf:_framework/blazor.boot.json",
                hdoc.RootElement.GetProperty("staticMissing").EnumerateArray().Select(e => e.GetString()));
        }
        finally
        {
            await broken.DisposeAsync();
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
