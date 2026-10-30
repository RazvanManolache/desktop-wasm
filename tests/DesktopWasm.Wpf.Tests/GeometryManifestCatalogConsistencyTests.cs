using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Pins the honest converted-and-booting reading of the staged Geometry
/// package: the manifest must be a genuine successful conversion pinned to
/// the catalog commit (the exact preparation_checks assert), while the
/// catalog promotes the app to ready with a boundary that names the boot
/// proof and the remaining fidelity limits. Boot plus the radio-select
/// workflow are proven by <c>ConvertedGeometryInteractionTests</c> and the
/// Convert boot/press oracles, not by this file.
/// </summary>
[ResetAmbientState]
public sealed class GeometryManifestCatalogConsistencyTests
{
    [Fact]
    public void Staged_geometry_manifest_matches_catalog_pin()
    {
        var root = FindRepoRoot();
        var package = JsonSerializer.Deserialize<ApplicationPackage>(
            File.ReadAllText(System.IO.Path.Combine(root, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "geometry", "app-manifest.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        package.Validate("geometry", WpfAppBoot.MapVersion);
        Assert.Equal("converted", package.Status);
        Assert.Empty(package.Gaps);
        var catalogEntry = CatalogGeometry(root);
        Assert.Equal(catalogEntry.GetProperty("commit").GetString(), package.SourceCommit);
        Assert.False(string.IsNullOrEmpty(package.SourceCommit));
        var entry = Assert.Single(package.Assemblies);
        var bytes = File.ReadAllBytes(System.IO.Path.Combine(root, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "geometry", entry.File));
        Assert.Equal(entry.Sha256, Convert.ToHexString(SHA256.HashData(bytes)), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Geometry_catalog_entry_is_ready_with_boot_proof_boundary()
    {
        var catalogEntry = CatalogGeometry(FindRepoRoot());
        Assert.Equal("external", catalogEntry.GetProperty("kind").GetString());
        Assert.Equal("ready", catalogEntry.GetProperty("status").GetString());
        var boundary = catalogEntry.GetProperty("boundary").GetString();
        Assert.False(string.IsNullOrEmpty(boundary));
        Assert.Contains("boots", boundary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("radio", boundary, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement CatalogGeometry(string root)
    {
        using var catalog = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(root, "src", "DesktopWasm.BrowserHost", "wwwroot", "app-library.json")));
        foreach (var app in catalog.RootElement.EnumerateArray())
        {
            if (app.GetProperty("slug").GetString() == "geometry")
                return app.Clone();
        }
        throw new InvalidDataException("Geometry is missing from app-library.json.");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "DesktopWasm.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return directory.FullName;
    }
}
