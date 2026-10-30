using System.Text.Json;
using DesktopWasm.AppModel;

namespace DesktopWasm.Catalog.Tests;

/// <summary>Every generated entry traces to a snapshot entry; every snapshot
/// entry lands in the registry. Counts come from the files, not docs.</summary>
public sealed class CatalogAccountingTests
{
    private static (AppRegistry Registry, string Dir) Load()
    {
        var dir = Fx.CatalogDir();
        return (CatalogSerializer.Deserialize(File.ReadAllText(Path.Combine(dir, "apps.json"))), dir);
    }

    private static HashSet<string> SnapshotSlugs(JsonElement snap, string kind)
    {
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var data = snap.GetProperty("data");
        switch (kind)
        {
            case "scenes":
                foreach (var s in data.GetProperty("scenes").EnumerateArray())
                    slugs.Add(s.GetProperty("slug").GetString()!);
                break;
            case "slugs":
                foreach (var s in data.GetProperty("slugs").EnumerateArray())
                    slugs.Add(s.GetString()!);
                break;
            case "entries":
                foreach (var e in data.GetProperty("entries").EnumerateArray())
                    slugs.Add(e.GetProperty("slug").GetString()!);
                break;
        }
        return slugs;
    }

    [Fact]
    public void EverySnapshotEntryIsRegistered_AndNothingElse()
    {
        var (registry, dir) = Load();
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mixed-session-pilot" };
        expected.UnionWith(SnapshotSlugs(Fx.Snapshot(dir, "winforms-scenestate"), "scenes"));
        expected.UnionWith(SnapshotSlugs(Fx.Snapshot(dir, "winforms-syncfusion"), "slugs"));
        expected.UnionWith(SnapshotSlugs(Fx.Snapshot(dir, "winforms-benchmarks"), "entries"));
        expected.UnionWith(SnapshotSlugs(Fx.Snapshot(dir, "wpf-applibrary"), "entries"));
        var actual = registry.Apps.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FamilyCountsMatchSnapshots()
    {
        var (registry, dir) = Load();
        var fixedCount = Fx.Snapshot(dir, "winforms-scenestate").GetProperty("data").GetProperty("count").GetInt32();
        var syncCount = Fx.Snapshot(dir, "winforms-syncfusion").GetProperty("data").GetProperty("count").GetInt32();
        var benchCount = Fx.Snapshot(dir, "winforms-benchmarks").GetProperty("data").GetProperty("count").GetInt32();
        var libCount = Fx.Snapshot(dir, "wpf-applibrary").GetProperty("data").GetProperty("count").GetInt32();
        Assert.Equal(fixedCount + syncCount + benchCount,
            registry.Apps.Count(a => a.Capabilities == AppCapability.WinForms));
        Assert.Equal(libCount, registry.Apps.Count(a => a.Capabilities == AppCapability.Wpf));
        Assert.Single(registry.Apps, a => a.Capabilities == AppCapability.Mixed);
        Assert.Equal(fixedCount + syncCount + benchCount + libCount + 1, registry.Count);
    }

    [Fact]
    public void WpfDemosAreSubsetOfLibrary()
    {
        var (_, dir) = Load();
        var snap = Fx.Snapshot(dir, "wpf-demos");
        Assert.True(snap.GetProperty("data").GetProperty("subsetOfLibrary").GetBoolean());
    }

    [Fact]
    public void ConflictMapMatchesRecomputation()
    {
        var (registry, dir) = Load();
        using var doc = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(dir, "conflict-map.json")));
        var recorded = doc.RootElement.GetProperty("conflicts").EnumerateArray()
            .Select(c => c.GetString()!).ToArray();
        Assert.Equal(CatalogValidator.FindConflicts(registry.Apps), recorded);
        Assert.Empty(recorded);
    }

    [Fact]
    public void ConvertedManifestsKeepPackageIdentity()
    {
        var (registry, _) = Load();
        var converted = registry.Apps.Where(a => a.Package?.Status == "converted").ToList();
        Assert.NotEmpty(converted); // guards against a silently emptied package set
        foreach (var app in converted)
        {
            Assert.Contains(app.Package!.Assemblies, a => a.Name == app.Entry.EntryAssembly);
            Assert.Empty(app.Package.Gaps);
        }
    }

    [Fact]
    public void NotConvertedEntriesStayNotConverted()
    {
        var (registry, _) = Load();
        var blocked = registry.Apps.Where(a => a.UpstreamStatus == "not-converted").ToList();
        Assert.NotEmpty(blocked);
        Assert.All(blocked, a =>
        {
            Assert.Equal(AppAvailability.NotConverted, a.Availability);
            Assert.NotEmpty(a.Gaps);
        });
    }

    [Fact]
    public void NothingIsMarkedWorking()
    {
        var (registry, _) = Load();
        // No staged receipt has been applied yet: nothing may be available,
        // and availability can never mean runtime-verified (no such state).
        Assert.DoesNotContain(registry.Apps, a => a.Availability == AppAvailability.Available);
    }
}
