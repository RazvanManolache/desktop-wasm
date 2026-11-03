using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DesktopWasm.Host.Server;
using Xunit;

namespace DesktopWasm.Shell.Tests;

/// <summary>Launch harnesses point the package seam at a frozen generation's
/// packages/ dir via DESKTOPWASM_PACKAGES. Explicit options still win.</summary>
public sealed class PackagesRootEnvTests
{
    [Fact]
    public async Task Env_packages_root_serves_generation_packages()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dwpkgenv-" + Guid.NewGuid().ToString("N"));
        string pkgDir = Path.Combine(dir, "packages", "test-smoke-form");
        Directory.CreateDirectory(pkgDir);
        await File.WriteAllTextAsync(Path.Combine(dir, "apps.json"), ShellHost.FixtureCatalog());
        await File.WriteAllBytesAsync(Path.Combine(pkgDir, "entry.bin"), [9, 8, 7]);
        string? prior = Environment.GetEnvironmentVariable("DESKTOPWASM_PACKAGES");
        Environment.SetEnvironmentVariable("DESKTOPWASM_PACKAGES", Path.Combine(dir, "packages"));
        var shell = ShellApp.Create(new ShellOptions(
            CatalogPath: Path.Combine(dir, "apps.json"),
            PackagesRoot: null,
            LogDir: Path.Combine(dir, "logs"),
            ContentRootPath: dir,
            WebRootPath: Path.Combine(dir, "wwwroot"),
            WinFormsBackendUrl: "http://127.0.0.1:1"));
        shell.Urls.Clear();
        shell.Urls.Add("http://127.0.0.1:0");
        await shell.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(shell.Urls.First()) };
            var manifest = await client.GetFromJsonAsync<JsonElement>(
                "api/packages/test-smoke-form/manifest");
            Assert.Equal("entry.bin",
                Assert.Single(manifest.GetProperty("files").EnumerateArray())
                    .GetProperty("name").GetString());
            using var content = await client.GetAsync(
                "api/packages/test-smoke-form/content?name=entry.bin");
            Assert.Equal(HttpStatusCode.OK, content.StatusCode);
            Assert.Equal(new byte[] { 9, 8, 7 }, await content.Content.ReadAsByteArrayAsync());
        }
        finally
        {
            await shell.DisposeAsync();
            Environment.SetEnvironmentVariable("DESKTOPWASM_PACKAGES", prior);
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Explicit_packages_root_wins_over_env()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dwpkgenv-" + Guid.NewGuid().ToString("N"));
        string explicitDir = Path.Combine(dir, "explicit", "test-smoke-form");
        Directory.CreateDirectory(explicitDir);
        Directory.CreateDirectory(Path.Combine(dir, "packages"));
        await File.WriteAllTextAsync(Path.Combine(dir, "apps.json"), ShellHost.FixtureCatalog());
        await File.WriteAllBytesAsync(Path.Combine(explicitDir, "explicit.bin"), [1]);
        string? prior = Environment.GetEnvironmentVariable("DESKTOPWASM_PACKAGES");
        Environment.SetEnvironmentVariable("DESKTOPWASM_PACKAGES", Path.Combine(dir, "packages"));
        var shell = ShellApp.Create(new ShellOptions(
            CatalogPath: Path.Combine(dir, "apps.json"),
            PackagesRoot: Path.Combine(dir, "explicit"),
            LogDir: Path.Combine(dir, "logs"),
            ContentRootPath: dir,
            WebRootPath: Path.Combine(dir, "wwwroot"),
            WinFormsBackendUrl: "http://127.0.0.1:1"));
        shell.Urls.Clear();
        shell.Urls.Add("http://127.0.0.1:0");
        await shell.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(shell.Urls.First()) };
            var manifest = await client.GetFromJsonAsync<JsonElement>(
                "api/packages/test-smoke-form/manifest");
            Assert.Equal("explicit.bin",
                Assert.Single(manifest.GetProperty("files").EnumerateArray())
                    .GetProperty("name").GetString());
        }
        finally
        {
            await shell.DisposeAsync();
            Environment.SetEnvironmentVariable("DESKTOPWASM_PACKAGES", prior);
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}

