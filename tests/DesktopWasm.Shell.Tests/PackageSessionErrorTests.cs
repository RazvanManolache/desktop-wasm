using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Host.Server;
using Xunit;

namespace DesktopWasm.Shell.Tests;

[Collection("shell-host")]
public sealed class PackageSecurityTests(ShellHost host)
{
    [Fact]
    public async Task Manifest_lists_content_hashed_files_with_pinned_urls()
    {
        var manifest = await host.Client.GetFromJsonAsync<JsonElement>(
            "api/packages/test-smoke-form/manifest");
        var files = manifest.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal(2, files.Count);
        string entryHash = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3, 4 }));
        var entry = files.Single(f => f.GetProperty("name").GetString() == "entry.bin");
        Assert.Equal(entryHash, entry.GetProperty("sha256").GetString());
        Assert.Contains($"v={entryHash}", entry.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Content_serves_bytes_with_hash_header()
    {
        using var response = await host.Client.GetAsync(
            "api/packages/test-smoke-form/content?name=entry.bin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await response.Content.ReadAsByteArrayAsync());
        string entryHash = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3, 4 }));
        Assert.Equal(entryHash, response.Headers.GetValues("X-DesktopWasm-Sha256").Single());
    }

    [Fact]
    public async Task Stale_version_is_conflict_not_silent_bytes()
    {
        using var response = await host.Client.GetAsync(
            "api/packages/test-smoke-form/content?name=entry.bin&v=DEADBEEF");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("content-mismatch", doc.RootElement.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("../apps.json")]
    [InlineData("..\\apps.json")]
    [InlineData("/etc/passwd")]
    [InlineData("sub/dir.bin")]
    [InlineData("evil.")]
    [InlineData("")]
    public async Task Traversal_and_unsafe_names_are_rejected(string name)
    {
        using var response = await host.Client.GetAsync(
            $"api/packages/test-smoke-form/content?name={Uri.EscapeDataString(name)}");
        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest,
            $"name '{name}' returned {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Unknown_app_and_unknown_content_are_404()
    {
        using var app = await host.Client.GetAsync("api/packages/no-such-app/manifest");
        Assert.Equal(HttpStatusCode.NotFound, app.StatusCode);
        using var content = await host.Client.GetAsync(
            "api/packages/test-smoke-form/content?name=no-such.bin");
        Assert.Equal(HttpStatusCode.NotFound, content.StatusCode);
    }

    [Fact]
    public async Task Unstaged_package_is_503_with_concrete_gap()
    {
        using var response = await host.Client.GetAsync("api/packages/test-color-lab/manifest");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("package-unavailable", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void Guard_rejects_traversal_without_touching_disk()
    {
        foreach (string bad in new[] { "../x", "..\\x", "a/b", "x.", "", " " })
            Assert.Throws<FileNotFoundException>(() => PackageService.GuardContentName("app", bad));
        PackageService.GuardContentName("app", "ok-file.bin");
    }
}

[Collection("shell-host")]
public sealed class SessionAndErrorTests(ShellHost host)
{
    [Fact]
    public async Task Session_create_returns_stable_launcher_identity()
    {
        using var response = await host.Client.PostAsJsonAsync("api/sessions", new { appId = "test-smoke-form" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<JsonElement>();
        string sid = session.GetProperty("sessionId").GetString()!;
        Assert.Equal(32, sid.Length);
        Assert.True(sid.All(c => Uri.IsHexDigit(c)));
        Assert.Equal($"/launch/test-smoke-form?session={sid}", session.GetProperty("launchUrl").GetString());
    }

    [Fact]
    public async Task Session_alias_resolves_and_supplied_id_is_stable()
    {
        using var response = await host.Client.PostAsJsonAsync("api/sessions",
            new { appId = "test-smokeform", sessionId = "probe_123-ABC" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("test-smoke-form", session.GetProperty("appId").GetString());
        Assert.Equal("probe_123-ABC", session.GetProperty("sessionId").GetString());
    }

    [Fact]
    public async Task Session_for_unknown_app_is_404_and_bad_id_is_400()
    {
        using var unknown = await host.Client.PostAsJsonAsync("api/sessions", new { appId = "no-such-app" });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using var bad = await host.Client.PostAsJsonAsync("api/sessions",
            new { appId = "test-smoke-form", sessionId = "../evil" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public void Session_guard_accepts_only_safe_charset()
    {
        Assert.Equal("aB1_-", SessionService.GuardSession("aB1_-"));
        Assert.Throws<ArgumentException>(() => SessionService.GuardSession("../evil"));
        Assert.Throws<ArgumentException>(() => SessionService.GuardSession(new string('x', 129)));
    }

    [Fact]
    public async Task Error_batch_is_attributed_per_app_and_fixable()
    {
        using var posted = await host.Client.PostAsJsonAsync("api/client-errors", new[]
        {
            new { app = "test-smoke-form", session = "s1", kind = "app-load", message = "boom", stack = (string?)null, source = "probe" },
            new { app = "test-color-lab", session = "s2", kind = "render", message = "flicker", stack = (string?)null, source = "probe" },
        });
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);

        var filtered = await host.Client.GetFromJsonAsync<List<JsonElement>>(
            "api/client-errors?app=test-smoke-form");
        Assert.NotNull(filtered);
        Assert.Single(filtered);
        Assert.Equal("test-smoke-form", filtered[0].GetProperty("app").GetString());
        Assert.Equal("s1", filtered[0].GetProperty("session").GetString());
        string signature = filtered[0].GetProperty("signature").GetString()!;

        using var oversized = await host.Client.PostAsJsonAsync("api/client-errors",
            Enumerable.Range(0, 51).Select(i => new
                { app = "test-smoke-form", session = "s", kind = "k", message = $"m{i}", stack = (string?)null, source = "p" }).ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);

        using var fixed_ = await host.Client.PostAsJsonAsync("api/errors/mark-fixed",
            new { signature, note = "fixture triage" });
        Assert.Equal(HttpStatusCode.OK, fixed_.StatusCode);
        var reread = await host.Client.GetFromJsonAsync<List<JsonElement>>(
            "api/client-errors?app=test-smoke-form");
        Assert.Equal("fixed", reread![0].GetProperty("status").GetString());

        using var inventory = await host.Client.GetAsync("errors?app=test-smoke-form");
        Assert.Equal(HttpStatusCode.OK, inventory.StatusCode);
        Assert.Contains(signature, await inventory.Content.ReadAsStringAsync());
    }
}
