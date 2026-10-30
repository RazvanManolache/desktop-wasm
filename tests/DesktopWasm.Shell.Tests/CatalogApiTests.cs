using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DesktopWasm.Host.Server;
using Xunit;

namespace DesktopWasm.Shell.Tests;

[Collection("shell-host")]
public sealed class CatalogApiTests(ShellHost host)
{
    [Fact]
    public async Task Healthz_reports_catalog_and_pinned_hashes()
    {
        using var response = await host.Client.GetAsync("api/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.True(root.GetProperty("catalogLoaded").GetBoolean());
        Assert.Equal(4, root.GetProperty("catalogCount").GetInt32());
        string expectedCatalogHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(ShellHost.FixtureCatalog())));
        Assert.Equal(expectedCatalogHash, root.GetProperty("catalogSha256").GetString());
        string expectedPortalHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes("<!doctype html><html><head><title>portal</title></head><body>portal</body></html>")));
        Assert.Equal(expectedPortalHash, root.GetProperty("portalSha256").GetString());
    }

    [Fact]
    public async Task Catalog_lists_all_entries_in_deterministic_id_order()
    {
        var apps = await host.Client.GetFromJsonAsync<List<JsonElement>>("api/catalog");
        Assert.NotNull(apps);
        Assert.Equal(
            ["test-color-lab", "test-gif-recorder", "test-mixed-fixture", "test-smoke-form"],
            apps.Select(a => a.GetProperty("id").GetString()).ToList());
    }

    [Theory]
    [InlineData("wpf", new[] { "test-color-lab", "test-gif-recorder", "test-mixed-fixture" })]
    [InlineData("winforms", new[] { "test-mixed-fixture", "test-smoke-form" })]
    [InlineData("mixed", new[] { "test-mixed-fixture" })]
    public async Task Framework_filter_covers_both_families_and_mixed(string framework, string[] expected)
    {
        var apps = await host.Client.GetFromJsonAsync<List<JsonElement>>(
            $"api/catalog?framework={framework}");
        Assert.NotNull(apps);
        Assert.Equal(expected, apps.Select(a => a.GetProperty("id").GetString()).ToList());
    }

    [Fact]
    public async Task Unknown_framework_filter_is_rejected()
    {
        using var response = await host.Client.GetAsync("api/catalog?framework=silverlight");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Status_and_text_filters_combine()
    {
        var apps = await host.Client.GetFromJsonAsync<List<JsonElement>>(
            "api/catalog?status=ready&q=fixture");
        Assert.NotNull(apps);
        Assert.Equal(
            ["test-color-lab", "test-mixed-fixture", "test-smoke-form"],
            apps.Select(a => a.GetProperty("id").GetString()).ToList());
    }

    [Fact]
    public async Task Mixed_record_reports_both_capabilities_and_provenance()
    {
        var app = await host.Client.GetFromJsonAsync<JsonElement>("api/catalog/test-mixed-fixture");
        Assert.True(app.GetProperty("mixed").GetBoolean());
        Assert.Equal(
            ["winforms", "wpf"],
            app.GetProperty("frameworks").EnumerateArray().Select(e => e.GetString()).ToList());
        var unavailable = await host.Client.GetFromJsonAsync<JsonElement>("api/catalog/test-gif-recorder");
        Assert.False(unavailable.GetProperty("available").GetBoolean());
        Assert.Contains("capture APIs unavailable in browser",
            unavailable.GetProperty("gaps").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("https://example.invalid/repo",
            unavailable.GetProperty("provenance").GetProperty("sourceUrl").GetString());
    }

    [Fact]
    public async Task Legacy_alias_resolves_to_canonical_id()
    {
        var app = await host.Client.GetFromJsonAsync<JsonElement>("api/catalog/test-smokeform");
        Assert.Equal("test-smoke-form", app.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Unknown_app_returns_clear_error_not_default_app()
    {
        using var response = await host.Client.GetAsync("api/catalog/no-such-app");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unknown-app", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("no-such-app", doc.RootElement.GetProperty("unknownId").GetString());
    }

    [Fact]
    public async Task Legacy_routes_redirect_into_unified_launcher()
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = host.Client.BaseAddress };
        foreach (string route in new[] { "app/test-smokeform", "demo/test-color-lab", "runtime/test-mixed-fixture" })
        {
            using var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/launch/", response.Headers.Location?.ToString() ?? "");
        }
        using var unknown = await client.GetAsync("app/no-such-app");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public void Duplicate_ids_are_rejected_with_conflict_map()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dwshell-dup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "apps.json");
            File.WriteAllText(file, ShellHost.FixtureCatalog().Replace(
                "\"test-color-lab\"", "\"test-smoke-form\""));
            var ex = Assert.Throws<CatalogConflictException>(() => new FileCatalogProvider(file));
            Assert.Contains("duplicate-id:test-smoke-form", ex.Message);
            Assert.True(ex.Conflicts.ContainsKey("duplicate-id:test-smoke-form"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Alias_colliding_with_an_id_is_rejected()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dwshell-alias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "apps.json");
            File.WriteAllText(file, ShellHost.FixtureCatalog().Replace(
                "\"aliases\":[\"test-smokeform\"]", "\"aliases\":[\"test-color-lab\"]"));
            var ex = Assert.Throws<CatalogConflictException>(() => new FileCatalogProvider(file));
            Assert.Contains("alias-collision:test-color-lab", ex.Message);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Missing_registry_reports_concrete_gap_not_empty_list()
    {
        var provider = new FileCatalogProvider(Path.Combine(Path.GetTempPath(), "dwshell-missing-" + Guid.NewGuid().ToString("N"), "apps.json"));
        Assert.False(provider.Loaded);
        Assert.Contains("catalog-unavailable", provider.LoadError);
        Assert.Empty(provider.All);
    }
}
