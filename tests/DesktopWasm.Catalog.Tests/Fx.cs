using System.Text.Json;
using DesktopWasm.AppModel;
using DesktopWasm.Catalog;

namespace DesktopWasm.Catalog.Tests;

/// <summary>Shared fixtures. Synthetic slugs only (fixture-*): these tests
/// must never impersonate real upstream apps.</summary>
internal static class Fx
{
    public static AppDescriptor Descriptor(
        string id,
        string[]? aliases = null,
        AppCapability capabilities = AppCapability.Wpf,
        AppAvailability availability = AppAvailability.MissingInput,
        string availabilityNote = "missing test input",
        string[]? gaps = null,
        AppEntry? entry = null,
        PackageIdentity? package = null,
        string transformVersion = "test-v1",
        string family = "test")
    {
        return new AppDescriptor(
            id, aliases ?? [],
            "Fixture " + id, "Synthetic test entry, not an upstream app.", "Fixtures", "",
            new AppProvenance(family, "fixture", null, null, null, null, null, null, false),
            entry ?? new AppEntry("native-demo", id, null, null),
            capabilities, transformVersion, package,
            availability, "fixture", availabilityNote, [], gaps ?? (availability == AppAvailability.Available ? [] : ["gap"]));
    }

    public static AppDescriptor Available(string id, AppCapability capabilities = AppCapability.Wpf) =>
        Descriptor(id, capabilities: capabilities, availability: AppAvailability.Available,
            availabilityNote: "staged", gaps: []);

    public static PackageIdentity BinPackage(string entry, string mapVersion = "wpf-convert-v1") =>
        new(1, mapVersion, "converted",
            [new PackageAssembly(entry, entry + ".deadbeef.bin",
                new string('a', 64), ["System.Runtime"])],
            [], null);

    public static PackageIdentity DllPackage(string entry, string mapVersion = "wf-rewrite-v1") =>
        LegacyAdapters.FromWinFormsTransform(entry,
            [new PackageAssembly(entry, entry + ".dll", new string('b', 64), ["System.Runtime"])],
            mapVersion);

    /// <summary>Locates the generated catalog: DW_CATALOG_DIR wins, else walk
    /// up from the test assembly. Tests never require frozen originals.</summary>
    public static string CatalogDir()
    {
        var env = Environment.GetEnvironmentVariable("DW_CATALOG_DIR");
        if (env is not null && File.Exists(Path.Combine(env, "apps.json"))) return env;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "catalog");
            if (File.Exists(Path.Combine(candidate, "apps.json"))) return candidate;
            if (File.Exists(Path.Combine(dir.FullName, "apps.json")) &&
                Directory.Exists(Path.Combine(dir.FullName, "sources"))) return dir.FullName;
        }
        throw new DirectoryNotFoundException("Cannot locate catalog/apps.json; set DW_CATALOG_DIR.");
    }

    public static JsonElement Snapshot(string catalogDir, string name)
    {
        using var doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(catalogDir, "sources", name + ".snapshot.json")));
        return doc.RootElement.Clone();
    }
}
