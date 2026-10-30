using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Pins the honest converted-and-booting reading of the staged WPFGallery
/// package: the manifest must be a genuine successful conversion pinned to
/// the catalog commit, every staged assembly hash-verified (no stale
/// bundles served), while the catalog promotes the app to ready with a
/// boundary that names the boot-chain state proof and the remaining page
/// limits. Boot, navigation and the checkbox interaction are proven by
/// <c>FluentPackTests</c> and benchmarks/gallery_state_proof.py, not here.
/// </summary>
[ResetAmbientState]
public sealed class GalleryManifestCatalogConsistencyTests
{
    [Fact]
    public void Staged_gallery_manifest_matches_catalog_pin()
    {
        var root = FindRepoRoot();
        var manifestPath = System.IO.Path.Combine(root, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "wpf-gallery", "app-manifest.json");
        var package = JsonSerializer.Deserialize<ApplicationPackage>(
            File.ReadAllText(manifestPath), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        package.Validate("wpf-gallery", WpfAppBoot.MapVersion);
        Assert.Equal("converted", package.Status);
        Assert.Empty(package.Gaps);
        var catalogEntry = CatalogGallery(root);
        Assert.Equal(catalogEntry.GetProperty("commit").GetString(), package.SourceCommit);
        Assert.False(string.IsNullOrEmpty(package.SourceCommit));
        Assert.NotEmpty(package.Assemblies);
        foreach (var entry in package.Assemblies)
        {
            var bytes = File.ReadAllBytes(System.IO.Path.Combine(root, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "wpf-gallery", entry.File));
            Assert.Equal(entry.Sha256, Convert.ToHexString(SHA256.HashData(bytes)), StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Gallery_catalog_entry_is_ready_with_boot_proof_boundary()
    {
        var catalogEntry = CatalogGallery(FindRepoRoot());
        Assert.Equal("external", catalogEntry.GetProperty("kind").GetString());
        Assert.Equal("ready", catalogEntry.GetProperty("status").GetString());
        var boundary = catalogEntry.GetProperty("boundary").GetString();
        Assert.False(string.IsNullOrEmpty(boundary));
        Assert.Contains("boots", boundary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ButtonPage", boundary, StringComparison.Ordinal);
    }

    private static JsonElement CatalogGallery(string root)
    {
        using var catalog = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(root, "src", "DesktopWasm.BrowserHost", "wwwroot", "app-library.json")));
        foreach (var app in catalog.RootElement.EnumerateArray())
            if (app.GetProperty("slug").GetString() == "wpf-gallery")
                return app.Clone();
        throw new InvalidOperationException("No wpf-gallery entry in app-library.json.");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "DesktopWasm.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return directory.FullName;
    }
}
