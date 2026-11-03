using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DesktopWasm.AppModel;
using DesktopWasm.Catalog;
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
    public async Task Status_filter_uses_contract_availability_names()
    {
        var missing = await host.Client.GetFromJsonAsync<List<JsonElement>>(
            "api/catalog?status=missingInput");
        Assert.NotNull(missing);
        Assert.Equal(
            ["test-color-lab", "test-mixed-fixture", "test-smoke-form"],
            missing.Select(a => a.GetProperty("id").GetString()).ToList());
        var notConverted = await host.Client.GetFromJsonAsync<List<JsonElement>>(
            "api/catalog?status=notConverted");
        Assert.NotNull(notConverted);
        Assert.Equal(["test-gif-recorder"],
            notConverted.Select(a => a.GetProperty("id").GetString()).ToList());
        using var bad = await host.Client.GetAsync("api/catalog?status=ready");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Status_and_text_filters_combine()
    {
        var apps = await host.Client.GetFromJsonAsync<List<JsonElement>>(
            "api/catalog?status=missingInput&q=fixture");
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
        Assert.Equal("mixed-session", app.GetProperty("kind").GetString());
        Assert.Equal("pilot", app.GetProperty("origin").GetString());
        Assert.Equal("mixed", app.GetProperty("provenance").GetProperty("family").GetString());
        var unavailable = await host.Client.GetFromJsonAsync<JsonElement>("api/catalog/test-gif-recorder");
        Assert.False(unavailable.GetProperty("available").GetBoolean());
        Assert.Equal("notConverted", unavailable.GetProperty("status").GetString());
        Assert.Contains("Fixture boundary note.",
            unavailable.GetProperty("gaps").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("https://example.invalid/repo",
            unavailable.GetProperty("provenance").GetProperty("sourceUrl").GetString());
    }

    [Theory]
    [InlineData("test-smokeform")]
    [InlineData("TEST-SMOKE-FORM")]
    public async Task Legacy_aliases_resolve_to_canonical_id(string alias)
    {
        var app = await host.Client.GetFromJsonAsync<JsonElement>(
            $"api/catalog/{Uri.EscapeDataString(alias)}");
        Assert.Equal("test-smoke-form", app.GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("photo-album")]
    [InlineData("photoalbum")]
    [InlineData("/app/photo-album")]
    [InlineData("/demo/photo-album")]
    public void Photo_album_aliases_resolve_in_live_catalog(string alias)
    {
        string catalogPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../catalog/apps.json"));
        var catalog = new CatalogSource(catalogPath);
        Assert.True(catalog.Loaded);
        var app = catalog.Find(alias);
        Assert.NotNull(app);
        Assert.Equal("telerik-photo", app.Id);
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
    public async Task Unknown_app_slug_serves_shell_page_not_api_json()
    {
        // FACTORY-shell-2: /app/* is browser navigation. A raw JSON 404 kills
        // the shell (no library, fromUrl's Unknown-app overlay unreachable).
        // Unknown slugs keep the 404 status but serve the shell page.
        using var response = await host.Client.GetAsync("app/no-such-app");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.StartsWith("text/html",
            response.Content.Headers.ContentType?.MediaType ?? "");
        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"unknown-app\"", body);
        Assert.Contains("portal", body);
    }

    [Fact]
    public void Duplicate_ids_are_rejected()
    {
        var descriptors = ShellHost.FixtureDescriptors().ToList();
        descriptors.Add(descriptors[0]);
        var ex = Assert.Throws<InvalidDataException>(() => new AppRegistry(descriptors));
        Assert.Contains("duplicate id 'test-color-lab'", ex.Message);
    }

    [Fact]
    public void Alias_colliding_with_an_id_is_rejected()
    {
        var descriptors = ShellHost.FixtureDescriptors().ToList();
        descriptors[3] = descriptors[3] with { Aliases = ["test-color-lab"] };
        var ex = Assert.Throws<InvalidDataException>(() => new AppRegistry(descriptors));
        Assert.Contains("collides with an id", ex.Message);
    }

    [Fact]
    public void Missing_registry_reports_concrete_gap_not_empty_list()
    {
        var source = new CatalogSource(Path.Combine(Path.GetTempPath(), "dwshell-missing-" + Guid.NewGuid().ToString("N"), "apps.json"));
        Assert.False(source.Loaded);
        Assert.Contains("catalog-unavailable", source.LoadError);
        Assert.Null(source.Registry);
    }

    [Fact]
    public void Invalid_registry_reports_concrete_error()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dwshell-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "apps.json");
            File.WriteAllText(file, """{"schemaVersion": 2, "apps": [{"id": "BAD ID!"}]}""");
            var source = new CatalogSource(file);
            Assert.False(source.Loaded);
            Assert.Contains("catalog-invalid", source.LoadError);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
