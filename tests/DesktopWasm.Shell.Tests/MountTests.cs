using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DesktopWasm.Host.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DesktopWasm.Shell.Tests;

/// <summary>Mount proxy/status tests against a fake WinForms backend and a
/// staged WPF static root (synthetic fixtures only).</summary>
public sealed class MountHost : IAsyncLifetime
{
    private WebApplication? _shell;
    private WebApplication? _backend;

    public string CatalogDir { get; } = Path.Combine(Path.GetTempPath(), "dwmount-" + Guid.NewGuid().ToString("N"));
    public string WpfRoot => Path.Combine(CatalogDir, "wpfroot");
    public HttpClient Client { get; private set; } = new();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(CatalogDir);
        Directory.CreateDirectory(Path.Combine(CatalogDir, "packages"));
        Directory.CreateDirectory(WpfRoot);
        await File.WriteAllTextAsync(Path.Combine(CatalogDir, "apps.json"), ShellHost.FixtureCatalog());
        await File.WriteAllTextAsync(Path.Combine(WpfRoot, "index.html"),
            "<!doctype html><html><head><base href=\"/\" /></head><body>wpf</body></html>");
        await File.WriteAllTextAsync(Path.Combine(WpfRoot, "app.css"), "body{}");
        // One staged converted app (test-gif-recorder is a converted-app in
        // the fixture catalog): legacy routes must resolve it into the mount.
        Directory.CreateDirectory(Path.Combine(WpfRoot, "apps", "test-gif-recorder"));
        await File.WriteAllTextAsync(
            Path.Combine(WpfRoot, "apps", "test-gif-recorder", "app-manifest.json"),
            """{"slug":"test-gif-recorder"}""");

        var backendBuilder = WebApplication.CreateBuilder();
        backendBuilder.Logging.ClearProviders();
        var backend = backendBuilder.Build();
        backend.Urls.Clear();
        backend.Urls.Add("http://127.0.0.1:0");
        backend.MapGet("/api/preview-config", () => Results.Ok(new
        {
            apps = new[]
            {
                new { slug = "test-smoke-form", label = "Smoke", available = true, setupNote = (string?)null },
                new { slug = "test-todo", label = "Todo", available = false, setupNote = "folder missing" },
            },
        }));
        backend.MapGet("/mw-page", () => Results.Content(
            "<!doctype html><html><head><base href=\"/\" /></head><body>backend</body></html>", "text/html"));
        backend.MapGet("/app/{slug}", (string slug) => Results.Content(
            "<!doctype html><html><head><base href=\"/\" /></head><body>library " + slug + "</body></html>", "text/html"));
        backend.MapGet("/runtime.html", () => Results.Content(
            "<!doctype html><html><head><base href=\"/\" /></head><body>runtime</body></html>", "text/html"));
        backend.MapGet("/mw-data", () => Results.Ok(new { ok = true }));
        backend.MapGet("/mw-asm", (HttpContext context) =>
        {
            context.Response.Headers["X-WfWasm-Sha256"] = new string('A', 64);
            context.Response.Headers.CacheControl = "private,max-age=31536000,immutable";
            return Results.Bytes(new byte[] { 1, 2, 3 }, "application/octet-stream");
        });
        backend.MapGet("/mw-etag", () => Results.File(
            new byte[] { 4, 5, 6, 7 },
            "application/octet-stream",
            entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue("\"mw-etag-v1\"")));
        await backend.StartAsync();
        _backend = backend;

        var shell = ShellApp.Create(new ShellOptions(
            CatalogPath: Path.Combine(CatalogDir, "apps.json"),
            PackagesRoot: Path.Combine(CatalogDir, "packages"),
            LogDir: Path.Combine(CatalogDir, "logs"),
            ContentRootPath: CatalogDir,
            WebRootPath: Path.Combine(CatalogDir, "wwwroot"),
            WinFormsBackendUrl: backend.Urls.First(),
            WpfWebRootPath: WpfRoot));
        shell.Urls.Clear();
        shell.Urls.Add("http://127.0.0.1:0");
        await shell.StartAsync();
        _shell = shell;
        Client = new HttpClient { BaseAddress = new Uri(shell.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        if (_shell is not null) await _shell.DisposeAsync();
        if (_backend is not null) await _backend.DisposeAsync();
        try { Directory.Delete(CatalogDir, recursive: true); } catch { }
    }
}

[CollectionDefinition("shell-mount")]
public sealed class MountHostCollection : ICollectionFixture<MountHost>;

[Collection("shell-mount")]
public sealed class MountTests(MountHost host)
{
    [Fact]
    public void Base_href_rewrite_scopes_backend_paths()
    {
        string html = "<head><base href=\"/\" /></head>";
        Assert.Equal("<head><base href=\"/winforms/\" /></head>",
            MountService.RewriteBaseHref(html, "/winforms"));
    }

    [Fact]
    public async Task Winforms_proxy_forwards_integrity_and_cache_headers()
    {
        // The browser demand hook rejects hash-less assembly bytes; the
        // mount must forward the backend's integrity + cache headers.
        using var response = await host.Client.GetAsync("winforms/mw-asm");
        Assert.True(response.Headers.Contains("X-WfWasm-Sha256"));
        Assert.Equal(new string('A', 64),
            Assert.Single(response.Headers.GetValues("X-WfWasm-Sha256")));
        // Stacks may reorder directives; compare as a set.
        Assert.Equal(["immutable", "max-age=31536000", "private"],
            response.Headers.CacheControl?.ToString().Split(", ").OrderBy(d => d).ToArray());
    }

    [Fact]
    public async Task Winforms_proxy_forwards_etag_and_honors_304()
    {
        // Warm app boots revalidate multi-MB assemblies through the gateway:
        // a dropped ETag forces a full re-download every boot. The proxy
        // must forward the backend ETag and convert an upstream 304 into a
        // downstream 304 instead of a buffered 200.
        using var first = await host.Client.GetAsync("winforms/mw-etag");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        string? etag = first.Headers.ETag?.ToString();
        Assert.False(string.IsNullOrEmpty(etag));
        Assert.Equal(new byte[] { 4, 5, 6, 7 }, await first.Content.ReadAsByteArrayAsync());
        using var revalidate = new HttpRequestMessage(HttpMethod.Get, "winforms/mw-etag");
        revalidate.Headers.IfNoneMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue(etag!));
        using var second = await host.Client.SendAsync(revalidate);
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Empty(await second.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Winforms_proxy_rewrites_html_and_passes_api_through()
    {
        string page = await host.Client.GetStringAsync("winforms/mw-page");
        Assert.Contains("<base href=\"/winforms/\" />", page);
        var data = await host.Client.GetFromJsonAsync<JsonElement>("api/preview-config");
        Assert.Equal("test-smoke-form", data.GetProperty("apps")[0].GetProperty("slug").GetString());
    }

    [Fact]
    public async Task Mount_status_reports_backend_truth_per_framework()
    {
        var report = await host.Client.GetFromJsonAsync<JsonElement>(
            "api/mount-status?app=test-smoke-form");
        var wf = report.GetProperty("winforms");
        Assert.True(wf.GetProperty("canBoot").GetBoolean());
        Assert.StartsWith("/winforms/app/test-smoke-form?session=",
            wf.GetProperty("mountedUrl").GetString());
        Assert.Equal(JsonValueKind.Null, report.GetProperty("wpf").ValueKind);

        var wpfReport = await host.Client.GetFromJsonAsync<JsonElement>(
            "api/mount-status?app=test-color-lab");
        var wpf = wpfReport.GetProperty("wpf");
        Assert.True(wpf.GetProperty("canBoot").GetBoolean());
        Assert.StartsWith("/app/test-color-lab?session=",
            wpf.GetProperty("mountedUrl").GetString());

        var converted = await host.Client.GetFromJsonAsync<JsonElement>(
            "api/mount-status?app=test-gif-recorder");
        Assert.False(converted.GetProperty("wpf").GetProperty("canBoot").GetBoolean());
        Assert.Equal(JsonValueKind.Null, converted.GetProperty("wpf").GetProperty("mountedUrl").ValueKind);
    }

    [Fact]
    public async Task Wpf_static_serves_files_and_rewritten_fallback()
    {
        Assert.Equal("body{}", await host.Client.GetStringAsync("wpf/app.css"));
        string fallback = await host.Client.GetStringAsync("wpf/demo/test-color-lab");
        Assert.Contains("<base href=\"/wpf/\" />", fallback);
        // Encoded traversal is never decoded by routing: it safely lands on
        // the public fallback instead of escaping the root.
        string encoded = await host.Client.GetStringAsync("wpf/..%2Fapps.json");
        Assert.Contains("<base href=\"/wpf/\" />", encoded);
    }

    [Theory]
    [InlineData("wpf/apps/testapp-joint/app-manifest.json")]
    [InlineData("wpf/apps/no-such-app/app-manifest.json")]
    [InlineData("wpf/apps/test-gif-recorder/missing.dll")]
    [InlineData("wpf/_framework/no-such-asset.dat")]
    public async Task Wpf_reserved_namespaces_missing_files_are_404_json_not_spa_fallback(string path)
    {
        // RECOVER-dw-shell: a missing package manifest or framework asset
        // must be an honest 404 JSON error. Serving the 200 SPA fallback
        // here makes the loader misparse index.html as the manifest.
        using var response = await host.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Wpf_staged_manifest_still_serves_bytes()
    {
        using var response = await host.Client.GetAsync("wpf/apps/test-gif-recorder/app-manifest.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("test-gif-recorder", body);
    }

    [Fact]
    public async Task Wpf_client_routes_keep_spa_fallback_after_reserved_fix()
    {
        using var response = await host.Client.GetAsync("wpf/demo/test-color-lab");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<base href=\"/wpf/\" />", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("app.css", true)]
    [InlineData("demo/test-color-lab", true)]
    [InlineData("../apps.json", false)]
    [InlineData("..\\apps.json", false)]
    [InlineData("sub/../../apps.json", false)]
    public void Wpf_path_guard_rejects_traversal(string? rest, bool expected)
    {
        Assert.Equal(expected, ShellApp.TryMapWpfPath(host.WpfRoot, rest, out _));
    }

    [Fact]
    public async Task Legacy_alias_redirects_into_mount_when_bootable()
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = host.Client.BaseAddress };
        using var response = await client.GetAsync("app/test-smokeform");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/winforms/app/test-smoke-form?session=",
            response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task App_subdomain_proxies_to_backend_origin()
    {
        // The isolated origin IS the backend origin, so proxied pages keep
        // the backend's native "/" base: with a "/winforms/" base the
        // backend's own absolute /app/{slug} URL fails its containment
        // guard and boot crashes ("not contained by the base URI", live
        // smoke probe 2026-10-30). Main-host pages keep the re-root (see
        // Winforms_proxy_rewrites_html_and_passes_api_through).
        using var request = new HttpRequestMessage(HttpMethod.Get, "runtime.html?app=test-smoke-form");
        request.Headers.Host = "test-smokeform.localhost:80";
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("<base href=\"/\" />", body);
        Assert.DoesNotContain("/winforms/", body);
        Assert.Contains("runtime", body);
    }

    [Fact]
    public async Task App_route_serves_studio_shell_for_wpf_apps()
    {
        string studio = await host.Client.GetStringAsync("app/test-color-lab");
        Assert.Contains("<base href=\"/wpf/\" />", studio);
    }

    [Fact]
    public async Task App_route_serves_studio_for_staged_converted_app()
    {
        // Converted + staged bytes boot through the mount like native demos;
        // unstaged converted apps still land on the launch page (covered by
        // Legacy_routes_redirect_into_unified_launcher in CatalogApiTests).
        string studio = await host.Client.GetStringAsync("app/test-gif-recorder");
        Assert.Contains("<base href=\"/wpf/\" />", studio);
    }

    [Fact]
    public async Task Runtime_route_redirects_staged_converted_into_mounted_runtime()
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = host.Client.BaseAddress };
        using var response = await client.GetAsync("runtime/test-gif-recorder?session=s7");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/wpf/runtime/test-gif-recorder?session=s7",
            response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Demo_route_redirects_staged_converted_to_app_studio()
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = host.Client.BaseAddress };
        using var response = await client.GetAsync("demo/test-gif-recorder?session=s7");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/app/test-gif-recorder?session=s7",
            response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Trailing_slash_wpf_root_still_serves_files()
    {
        var shell = ShellApp.Create(new ShellOptions(
            CatalogPath: Path.Combine(host.CatalogDir, "apps.json"),
            PackagesRoot: Path.Combine(host.CatalogDir, "packages"),
            LogDir: Path.Combine(host.CatalogDir, "logs"),
            ContentRootPath: host.CatalogDir,
            WinFormsBackendUrl: "http://127.0.0.1:1",
            WpfWebRootPath: host.WpfRoot + Path.DirectorySeparatorChar));
        shell.Urls.Clear();
        shell.Urls.Add("http://127.0.0.1:0");
        await shell.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(shell.Urls.First()) };
            Assert.Equal("body{}", await client.GetStringAsync("wpf/app.css"));
            using var health = await client.GetAsync("api/healthz");
            using var doc = JsonDocument.Parse(await health.Content.ReadAsStringAsync());
            Assert.Equal(host.WpfRoot, doc.RootElement.GetProperty("wpfRoot").GetString());
            Assert.True(doc.RootElement.GetProperty("wpfStaged").GetBoolean());
        }
        finally { await shell.DisposeAsync(); }
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void Normalize_web_root_blank_stays_unstaged(string? input, string? expected)
    {
        Assert.Equal(expected, ShellApp.NormalizeWebRoot(input));
    }

    [Fact]
    public void Normalize_web_root_strips_trailing_separators_but_keeps_root()
    {
        string sep = Path.DirectorySeparatorChar.ToString();
        Assert.Equal($"{sep}wpfroot", ShellApp.NormalizeWebRoot($"{sep}wpfroot{sep}"));
        Assert.Equal($"{sep}wpfroot", ShellApp.NormalizeWebRoot($"{sep}wpfroot"));
        Assert.Equal(sep, ShellApp.NormalizeWebRoot(sep));
    }

    [Fact]
    public async Task Runtime_route_redirects_into_mounted_runtime()
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = host.Client.BaseAddress };
        using var response = await client.GetAsync("runtime/test-color-lab?session=s1");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/wpf/runtime/test-color-lab?session=s1",
            response.Headers.Location?.ToString());
    }

    [Theory]
    [InlineData("wpf/runtime/test-color-lab?session=s1")]
    [InlineData("wpf/demo/test-color-lab")]
    public async Task Runtime_paths_serve_shell_owned_shim(string path)
    {
        string shim = await host.Client.GetStringAsync(path);
        Assert.Contains("<base href=\"/wpf/\" />", shim);
        Assert.Contains("/wpf/runtime-channel.mjs", shim);
        Assert.Contains("/wpf/_framework/blazor.webassembly.js", shim);
        Assert.Contains("id=\"app\"", shim);
    }

    [Fact]
    public async Task Winforms_deep_link_page_carries_mount_compat_shim()
    {
        // The backend library reads backend-native /app/{slug} routes; under
        // the /winforms/ mount that defaults a deep-link to the smoke scene.
        // The shell-owned shim normalizes the prefixed deep-link (preserving
        // query so app selection survives) and re-roots later navigations.
        string page = await host.Client.GetStringAsync("winforms/app/test-smoke-form?session=s9&x=1");
        Assert.Contains("<base href=\"/winforms/\" />", page);
        Assert.Contains("data-dw-mount=\"/winforms\"", page);
        Assert.Contains("location.search", page);
        Assert.Contains("/app/", page);
        Assert.Contains("library test-smoke-form", page);
    }

    [Fact]
    public async Task Winforms_prefixed_runtime_page_carries_shim()
    {
        string page = await host.Client.GetStringAsync("winforms/runtime.html?app=test-smoke-form");
        Assert.Contains("<base href=\"/winforms/\" />", page);
        Assert.Contains("data-dw-mount=\"/winforms\"", page);
    }

    [Theory]
    [InlineData("<html><head><title>t</title></head><body>x</body></html>")]
    [InlineData("<html><body>x</body></html>")]
    [InlineData("no tags at all")]
    public void Inject_mount_compat_inserts_shim_once(string html)
    {
        string once = MountService.InjectMountCompat(html, "/winforms");
        Assert.Contains("data-dw-mount=\"/winforms\"", once);
        string twice = MountService.InjectMountCompat(once, "/winforms");
        Assert.Equal(once, twice);
        Assert.Equal(1, twice.Split("data-dw-mount=").Length - 1);
    }

    [Fact]
    public void Inject_mount_compat_targets_head_first()
    {
        string html = "<!doctype html><html><head><base href=\"/\" /></head><body>x</body></html>";
        string injected = MountService.InjectMountCompat(html, "/winforms");
        int head = injected.IndexOf("<head>", StringComparison.Ordinal);
        int shim = injected.IndexOf("data-dw-mount=", StringComparison.Ordinal);
        int body = injected.IndexOf("<body>", StringComparison.Ordinal);
        Assert.True(head < shim && shim < body);
    }

    [Theory]
    [InlineData("/launch/test-smoke-form?session=s1", "", "/launch/test-smoke-form?session=s1")]
    [InlineData("/launch/test-smoke-form?session=s1", "?rendering=1", "/launch/test-smoke-form?session=s1&rendering=1")]
    [InlineData("/launch/test-smoke-form?session=s1", "?session=s1", "/launch/test-smoke-form?session=s1")]
    [InlineData("/wpf/runtime/test-color-lab", "?session=s1", "/wpf/runtime/test-color-lab?session=s1")]
    public void With_preserved_query_merges_caller_extras(string target, string query, string expected)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(query);
        Assert.Equal(expected, ShellApp.WithPreservedQuery(target, context.Request));
    }

    [Fact]
    public async Task Legacy_winforms_redirect_preserves_query_and_session()
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = host.Client.BaseAddress };
        using var response = await client.GetAsync("app/test-smokeform?session=s1session&rendering=1");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/winforms/app/test-smoke-form?session=s1session&rendering=1",
            response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Legacy_route_rejects_malformed_session()
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = host.Client.BaseAddress };
        using var response = await client.GetAsync("app/test-smokeform?session=bad!!");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_legacy_slug_is_404_not_default_app()
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = host.Client.BaseAddress };
        using var response = await client.GetAsync("app/no-such-app?session=s1");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Backend_down_is_503_not_hang()
    {
        var shell = ShellApp.Create(new ShellOptions(
            CatalogPath: Path.Combine(host.CatalogDir, "apps.json"),
            PackagesRoot: Path.Combine(host.CatalogDir, "packages"),
            LogDir: Path.Combine(host.CatalogDir, "logs"),
            ContentRootPath: host.CatalogDir,
            WinFormsBackendUrl: "http://127.0.0.1:1",
            WpfWebRootPath: host.WpfRoot));
        shell.Urls.Clear();
        shell.Urls.Add("http://127.0.0.1:0");
        await shell.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(shell.Urls.First()) };
            using var response = await client.GetAsync("winforms/mw-page");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("mount-unavailable", doc.RootElement.GetProperty("error").GetString());
            var report = await client.GetFromJsonAsync<JsonElement>(
                "api/mount-status?app=test-smoke-form");
            Assert.False(report.GetProperty("winforms").GetProperty("canBoot").GetBoolean());
        }
        finally { await shell.DisposeAsync(); }
    }
}
