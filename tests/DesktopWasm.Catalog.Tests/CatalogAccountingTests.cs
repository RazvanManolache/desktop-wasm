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
        // Contract change RECOVER-CATALOG-2: the joint record is a permanent
        // registry member built from the packaging producer's real manifest
        // (not from import snapshots), alongside the pilot.
        var (registry, dir) = Load();
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mixed-session-pilot", "testapp-joint" };
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
        // Contract change RECOVER-CATALOG-2: exactly two mixed entries — the
        // unavailable pilot and the staged joint record.
        Assert.Equal(["mixed-session-pilot", "testapp-joint"],
            registry.Apps.Where(a => a.Capabilities == AppCapability.Mixed)
                .Select(a => a.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(fixedCount + syncCount + benchCount + libCount + 2, registry.Count);
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
    public void NotConvertedUpstreamEntriesStayBlocked()
    {
        // Contract change RVW2100 (decision RVW-20261030-2100-4467 §15.1):
        // a VALID converted manifest outranks stale upstream not-converted
        // metadata for preparation eligibility only. Such entries are
        // MissingInput with the manifest's package-copy gap (never Available
        // from metadata alone); entries without a valid converted manifest
        // stay NotConverted. Both provenance fields are preserved either way.
        var (registry, _) = Load();
        var blocked = registry.Apps.Where(a => a.UpstreamStatus == "not-converted").ToList();
        Assert.NotEmpty(blocked);
        Assert.All(blocked, a =>
        {
            if (a.Availability == AppAvailability.NotConverted) { Assert.NotEmpty(a.Gaps); return; }
            // Preparation-eligible or staged-verified: a valid converted
            // gapless package containing the entry assembly is mandatory,
            // and Available additionally requires hash-pinned local staging
            // evidence (never Available from metadata alone).
            Assert.NotNull(a.Package);
            Assert.Equal("converted", a.Package.Status);
            Assert.Empty(a.Package.Gaps);
            Assert.Contains(a.Package.Assemblies, x => x.Name == a.Entry.EntryAssembly);
            if (a.Availability == AppAvailability.MissingInput) { Assert.NotEmpty(a.Gaps); return; }
            Assert.Equal(AppAvailability.Available, a.Availability);
            Assert.Empty(a.Gaps);
            Assert.Contains(a.Evidence, e => e.StartsWith("local:", StringComparison.Ordinal));
            Assert.Contains("unverified", a.AvailabilityNote, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void WpfGalleryIsAvailableOnStagedClosure()
    {
        // RVW8900 staging slice: wpf-gallery's converted package identity
        // (transcribed from the frozen manifest) is now backed by staged
        // bytes at inputs/wpf/catalog-staging/wpf-gallery, digest-verified
        // on stage (manifest sha 65ECEA...FCDDF, 29-assembly closure).
        // Availability is prepared inputs with an honest runtime-unverified
        // note — no per-page full-parity precondition. The provenance
        // boundary records the staged copy and must not present frozen-root
        // staging as the local claim.
        var (registry, _) = Load();
        var app = registry.Resolve("wpf-gallery");
        Assert.Equal(AppAvailability.Available, app.Availability);
        Assert.Empty(app.Gaps);
        Assert.NotNull(app.Package);
        Assert.Equal("converted", app.Package.Status);
        Assert.Empty(app.Package.Gaps);
        Assert.Contains(app.Package.Assemblies, a => a.Name == app.Entry.EntryAssembly);
        Assert.Contains(app.Evidence, e => e.StartsWith("local:", StringComparison.Ordinal));
        Assert.Contains(app.Evidence, e => e.Contains("inputs/wpf/catalog-staging/wpf-gallery"));
        Assert.Contains(app.Evidence, e => e.Contains("65ECEA648ADC65F2E6BC6156BACA502AE73F7F69A6683400FE2FDF04586FCDDF"));
        Assert.Contains("unverified", app.AvailabilityNote, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("staged under apps/", app.Provenance.Boundary ?? "");
        Assert.Contains("import lane", app.Provenance.Boundary ?? "", StringComparison.OrdinalIgnoreCase);
        CatalogValidator.ValidateDescriptor(app);
        Assert.True(Assert.Single(registry.Menu(), m => m.Id == "wpf-gallery").IsLaunchable);
    }

    [Fact]
    public void AvailableMeansStagedNotVerified()
    {
        // Contract change RV1745: hello-real carries a real staged package
        // receipt, so Available entries now exist by design. What the old
        // NothingIsMarkedWorking guard still owns: availability can never
        // mean runtime-verified (no such state), and every Available entry
        // must trace to hash-pinned local staging evidence with empty gaps.
        var (registry, _) = Load();
        foreach (var app in registry.Apps.Where(a => a.Availability == AppAvailability.Available))
        {
            Assert.Contains(app.Evidence, e => e.StartsWith("local:", StringComparison.Ordinal));
            Assert.Empty(app.Gaps);
            Assert.Contains("unverified", app.AvailabilityNote, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("verified runtime", app.AvailabilityNote, StringComparison.OrdinalIgnoreCase);
        }
    }
}
