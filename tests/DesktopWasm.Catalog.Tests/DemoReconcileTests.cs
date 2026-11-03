using DesktopWasm.AppModel;
using DesktopWasm.Catalog;
using DesktopWasm.Catalog.Tool;

namespace DesktopWasm.Catalog.Tests;

/// <summary>RECOVER-CATALOG-REC: stale native-demo records reconcile only
/// through producer receipts with byte-verified digests — never by flipping
/// flags from directory existence. Fixture tests use synthetic slugs and
/// temp files only; the live test pins the reconciled records.</summary>
public sealed class DemoReconcileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "demorec-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string WriteRegistry(params AppDescriptor[] apps)
    {
        Directory.CreateDirectory(_dir);
        var appsPath = Path.Combine(_dir, "apps.json");
        File.WriteAllText(appsPath, CatalogSerializer.Serialize([.. apps]));
        return appsPath;
    }

    private string WriteReceipts(params (string Id, string Path, string Sha)[] receipts)
    {
        var path = Path.Combine(_dir, "receipts-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(receipts.Select(r =>
            new { id = r.Id, localPath = r.Path, sha256 = r.Sha, stagedUtc = "2026-10-31T00:00:00Z" })));
        return path;
    }

    private static AppDescriptor NativeDemo(string id, AppCapability capabilities = AppCapability.Wpf) =>
        Fx.Descriptor(id, capabilities: capabilities,
            entry: new AppEntry("native-demo", id, null, null));

    [Fact]
    public void NativeDemoPromotesOnlyOnDigestVerifiedFileReceipt()
    {
        // Positive: a byte-verified host-manifest file receipt promotes a
        // stale native-demo record with the standard unverified note.
        var dir = Path.Combine(_dir, "staging");
        Directory.CreateDirectory(dir);
        var host = Path.Combine(dir, "blazor.boot.json");
        File.WriteAllText(host, "host-manifest-bytes");
        string sha;
        using (var stream = File.OpenRead(host))
            sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
        WriteRegistry(NativeDemo("fixture-demo"));
        var batch = WriteReceipts(("fixture-demo", host, sha));

        var rc = Importer.Refresh(_dir, new[] { batch });

        Assert.Equal(0, rc);
        var app = CatalogSerializer.Deserialize(File.ReadAllText(Path.Combine(_dir, "apps.json")))
            .Apps.Single(a => a.Id == "fixture-demo");
        Assert.Equal(AppAvailability.Available, app.Availability);
        Assert.Empty(app.Gaps);
        Assert.Contains("unverified", app.AvailabilityNote, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(app.Evidence, e => e == $"local:{host}#{sha}");
    }

    [Fact]
    public void NativeDemoStaleDigestAbortsWithoutWriting()
    {
        // Negative: a receipt whose pin does not match the real bytes
        // aborts unwritten — no flip from a stale pin.
        var dir = Path.Combine(_dir, "staging");
        Directory.CreateDirectory(dir);
        var host = Path.Combine(dir, "blazor.boot.json");
        File.WriteAllText(host, "current-host-bytes");
        var appsPath = WriteRegistry(NativeDemo("fixture-demo"));
        var before = File.ReadAllText(appsPath);
        var bad = WriteReceipts(("fixture-demo", host, new string('d', 64)));

        var ex = Assert.Throws<InvalidDataException>(() => Importer.Refresh(_dir, new[] { bad }));
        Assert.Contains("digest mismatch", ex.Message);
        Assert.Equal(before, File.ReadAllText(appsPath));
    }

    [Fact]
    public void SharedPreparedInputPromotesEachRecordWithOwnReceipt()
    {
        // Batch-2 pattern: one staged prepared input (e.g. the serving
        // backend bundle manifest) backs several records, each with its OWN
        // digest-verified receipt row; a row for another id never promotes.
        var dir = Path.Combine(_dir, "staging");
        Directory.CreateDirectory(dir);
        var host = Path.Combine(dir, "blazor.boot.json");
        File.WriteAllText(host, "shared-host-manifest-bytes");
        string sha;
        using (var stream = File.OpenRead(host))
            sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
        WriteRegistry(NativeDemo("fixture-one"), NativeDemo("fixture-two"));
        var batch = WriteReceipts(
            ("fixture-one", host, sha),
            ("fixture-two", host, sha));

        Importer.Refresh(_dir, new[] { batch });

        var apps = CatalogSerializer.Deserialize(File.ReadAllText(Path.Combine(_dir, "apps.json")))
            .Apps.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(AppAvailability.Available, apps["fixture-one"].Availability);
        Assert.Equal(AppAvailability.Available, apps["fixture-two"].Availability);
        Assert.Equal($"local:{host}#{sha}", Assert.Single(apps["fixture-one"].Evidence,
            e => e.StartsWith("local:", StringComparison.Ordinal)));
    }

    [Fact]
    public void LiveSyncfusionBatchesAreAvailableAsPreparedInputs()
    {
        // RVW0900-CATALOG-2: 8 Input-family + 7 Data-family Syncfusion records
        // promoted through digest-verified staged inputs. Available means
        // prepared inputs with an honest runtime-unverified note — no
        // per-demo full-parity precondition. Autocomplete was already
        // available on the old backend bundle and is re-pinned to the new
        // staged input, keeping its earlier acceptance pointer as history.
        var registry = CatalogSerializer.Deserialize(
            File.ReadAllText(Path.Combine(Fx.CatalogDir(), "apps.json")));
        foreach (var id in new[]
        {
            "syncfusion-auto-complete", "syncfusion-buttons", "syncfusion-combobox",
            "syncfusion-numeric", "syncfusion-masked-edit", "syncfusion-toggle",
            "syncfusion-rating", "syncfusion-font-combo", "syncfusion-datagrid",
            "syncfusion-grid-grouping", "syncfusion-grid-cells",
            "syncfusion-multi-column-tree", "syncfusion-calendar",
            "syncfusion-clock", "syncfusion-wizard",
        })
        {
            var app = registry.Resolve(id);
            Assert.Equal(AppAvailability.Available, app.Availability);
            Assert.Empty(app.Gaps);
            Assert.Contains("unverified", app.AvailabilityNote, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(app.Evidence, e => e.StartsWith("local:", StringComparison.Ordinal));
            Assert.Contains(app.Evidence, e => e.Contains("inputs/winforms/syncfusion-staging/" + id));
            CatalogValidator.ValidateDescriptor(app);
            Assert.True(Assert.Single(registry.Menu(), m => m.Id == id).IsLaunchable);
        }
        var auto = registry.Resolve("syncfusion-auto-complete");
        Assert.Contains(auto.Evidence, e => e.StartsWith("acceptance:", StringComparison.Ordinal));
        Assert.Equal(50, registry.Apps.Count(a => a.Availability == AppAvailability.Available)); // RVW8900: +wpf-gallery staged
    }

    [Fact]
    public void LiveSyncfusionVizRestAreAvailableAsPreparedInputs()
    {
        // RVW0900-CATALOG-3: 8 Visualization-family + 8 Navigation/Layout/
        // Workspace/Documents-family records promoted through digest-verified
        // staged inputs. Same bar as slice 2: prepared inputs with an honest
        // runtime-unverified note, no per-demo parity precondition.
        var registry = CatalogSerializer.Deserialize(
            File.ReadAllText(Path.Combine(Fx.CatalogDir(), "apps.json")));
        foreach (var id in new[]
        {
            "syncfusion-chart", "syncfusion-gauges", "syncfusion-maps",
            "syncfusion-tree-map", "syncfusion-bullet-graph",
            "syncfusion-smith-chart", "syncfusion-pivot-grid",
            "syncfusion-pivot-chart", "syncfusion-navigation-drawer",
            "syncfusion-tabbed-mdi", "syncfusion-split-container",
            "syncfusion-tile-layout", "syncfusion-scroll-frame",
            "syncfusion-spell-checker", "syncfusion-docking",
            "syncfusion-treeview",
        })
        {
            var app = registry.Resolve(id);
            Assert.Equal(AppAvailability.Available, app.Availability);
            Assert.Empty(app.Gaps);
            Assert.Contains("unverified", app.AvailabilityNote, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(app.Evidence, e => e.StartsWith("local:", StringComparison.Ordinal));
            Assert.Contains(app.Evidence, e => e.Contains("inputs/winforms/syncfusion-staging/" + id));
            CatalogValidator.ValidateDescriptor(app);
            Assert.True(Assert.Single(registry.Menu(), m => m.Id == id).IsLaunchable);
        }
        Assert.Equal(50, registry.Apps.Count(a => a.Availability == AppAvailability.Available)); // RVW8900: +wpf-gallery staged
    }

    [Fact]
    public void LiveSyncfusionFinalAreAvailableAsPreparedInputs()
    {
        // RVW0900-CATALOG-4: 9 assigned + 2 gap-closure (barcode, html)
        // records promoted through digest-verified staged inputs, closing
        // 42/42 Syncfusion prepared inputs. Same bar: prepared inputs with
        // an honest runtime-unverified note, no per-demo parity precondition.
        var registry = CatalogSerializer.Deserialize(
            File.ReadAllText(Path.Combine(Fx.CatalogDir(), "apps.json")));
        foreach (var id in new[]
        {
            "syncfusion-skin-manager", "syncfusion-word",
            "syncfusion-control-bars", "syncfusion-range-slider",
            "syncfusion-radial-slider", "syncfusion-tool-tip",
            "syncfusion-hub-tile", "syncfusion-status-bar",
            "syncfusion-diagram", "syncfusion-barcode", "syncfusion-html",
        })
        {
            var app = registry.Resolve(id);
            Assert.Equal(AppAvailability.Available, app.Availability);
            Assert.Empty(app.Gaps);
            Assert.Contains("unverified", app.AvailabilityNote, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(app.Evidence, e => e.StartsWith("local:", StringComparison.Ordinal));
            Assert.Contains(app.Evidence, e => e.Contains("inputs/winforms/syncfusion-staging/" + id));
            CatalogValidator.ValidateDescriptor(app);
            Assert.True(Assert.Single(registry.Menu(), m => m.Id == id).IsLaunchable);
        }
        Assert.Equal(50, registry.Apps.Count(a => a.Availability == AppAvailability.Available)); // RVW8900: +wpf-gallery staged
    }

    [Fact]
    public void LiveReconciledDemosAreAvailableWithProducerPins()
    {
        // Live acceptance for controls/input/smoke: available via the real
        // staged host-manifest bytes matching the shell generation pins,
        // explicitly unverified, launchable, and nothing else inflated.
        var registry = CatalogSerializer.Deserialize(
            File.ReadAllText(Path.Combine(Fx.CatalogDir(), "apps.json")));
        foreach (var (id, pin) in new[]
        {
            ("controls", "37660C5D20B97D4BBE12A6B5B7EC980CDAD07AD7554EBE2D656639986119623A"),
            ("input", "37660C5D20B97D4BBE12A6B5B7EC980CDAD07AD7554EBE2D656639986119623A"),
            ("smoke", "5913C8D8AC031E1E77361EC3DD7D556DFBE4AF37114BFEE39B8FA9710784DE59"),
        })
        {
            var app = registry.Resolve(id);
            Assert.Equal(AppAvailability.Available, app.Availability);
            Assert.Empty(app.Gaps);
            Assert.Contains("unverified", app.AvailabilityNote, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(app.Evidence, e => e.StartsWith("local:", StringComparison.Ordinal));
            Assert.Contains(app.Evidence, e => e.Contains(pin));
            CatalogValidator.ValidateDescriptor(app);
            Assert.True(Assert.Single(registry.Menu(), m => m.Id == id).IsLaunchable);
        }
        Assert.Equal(50, registry.Apps.Count(a => a.Availability == AppAvailability.Available)); // RVW8900: +wpf-gallery staged
    }

    [Fact]
    public void LiveAutoCompleteIsAvailableOnStagedInput()
    {
        // RVW0900-CATALOG-2: re-pinned from the REC2 backend bundle to the
        // importer-staged app input (manifest digest 84A949D5…3C12416015,
        // closure + Flags content verified). The REC2 launch/render
        // acceptance stays as history only; availability is prepared inputs,
        // runtime unverified — the old backend pin is no longer claimed.
        var registry = CatalogSerializer.Deserialize(
            File.ReadAllText(Path.Combine(Fx.CatalogDir(), "apps.json")));
        var app = registry.Resolve("syncfusion-auto-complete");
        Assert.Equal(AppAvailability.Available, app.Availability);
        Assert.Empty(app.Gaps);
        Assert.Contains("unverified", app.AvailabilityNote, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(app.Evidence, e => e.Contains("84A949D5680F4B9F7DA71AC7BCB2A8E0FA4DC4E53C6AC1530B0F2D3C12416015"));
        Assert.Contains(app.Evidence, e => e.StartsWith("acceptance:", StringComparison.Ordinal));
        CatalogValidator.ValidateDescriptor(app);
        Assert.True(Assert.Single(registry.Menu(), m => m.Id == "syncfusion-auto-complete").IsLaunchable);
        Assert.Equal(50, registry.Apps.Count(a => a.Availability == AppAvailability.Available)); // RVW8900: +wpf-gallery staged
        Assert.Equal(AppAvailability.Staged, registry.Resolve("testapp-joint").Availability);
    }
}
