using DesktopWasm.AppModel;

namespace DesktopWasm.Catalog.Tests;

public sealed class AvailabilityTests
{
    private static Dictionary<string, LocalInputMapper.StagedReceipt> Receipts(params string[] ids) =>
        ids.ToDictionary(id => id,
            id => new LocalInputMapper.StagedReceipt(id, "inputs/" + id, new string('e', 64), "2026-10-30T12:00:00Z"),
            StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void ReceiptFlipsMissingInputToAvailable()
    {
        var before = Fx.Descriptor("fixture-app");
        Assert.Equal(AppAvailability.MissingInput, before.Availability);
        var after = LocalInputMapper.Refresh(before, Receipts("fixture-app"));
        Assert.Equal(AppAvailability.Available, after.Availability);
        Assert.Empty(after.Gaps);
        Assert.Contains(after.Evidence, e => e.Contains("inputs/fixture-app"));
        CatalogValidator.ValidateDescriptor(after);
    }

    [Fact]
    public void UnknownReceiptDoesNotUnblock()
    {
        var after = LocalInputMapper.Refresh(Fx.Descriptor("fixture-app"), Receipts("fixture-other"));
        Assert.Equal(AppAvailability.MissingInput, after.Availability);
        Assert.NotEmpty(after.Gaps);
    }

    [Fact]
    public void NotConvertedStaysNotConverted()
    {
        var blocked = Fx.Descriptor("fixture-blocked", availability: AppAvailability.NotConverted,
            availabilityNote: "no package upstream", gaps: ["not-converted"]);
        var after = LocalInputMapper.Refresh(blocked, Receipts("fixture-blocked"));
        Assert.Equal(AppAvailability.NotConverted, after.Availability);
    }

    [Fact]
    public void RequiredResourceNamesConcreteGap()
    {
        Assert.Contains("host", LocalInputMapper.RequiredResource(
            Fx.Descriptor("fixture-demo", family: "wpf"))!, StringComparison.OrdinalIgnoreCase);
        var converted = Fx.Descriptor("fixture-conv",
            entry: new AppEntry("converted-app", "E", "fixture-conv", null),
            package: Fx.BinPackage("E"));
        Assert.Contains("fixture-conv", LocalInputMapper.RequiredResource(converted)!,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("interop", LocalInputMapper.RequiredResource(LegacyAdapters.MixedPilot())!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefreshIsIdempotent_NoEvidenceDup()
    {
        var once = LocalInputMapper.Refresh(Fx.Descriptor("fixture-app"), Receipts("fixture-app"));
        var twice = LocalInputMapper.Refresh(once, Receipts("fixture-app"));
        Assert.Equal(
            CatalogSerializer.Serialize([once]),
            CatalogSerializer.Serialize([twice]));
        Assert.Single(twice.Evidence, e => e.StartsWith("local:", StringComparison.Ordinal));
    }

    [Fact]
    public void PilotWithReceiptStaysDemoted()
    {
        // A receipt alone never promotes a bridgeless mixed entry, and the
        // refresh must not throw: the pilot stays Unavailable with its gap.
        var pilot = LegacyAdapters.MixedPilot();
        var after = LocalInputMapper.Refresh(pilot,
            new Dictionary<string, LocalInputMapper.StagedReceipt>(StringComparer.OrdinalIgnoreCase)
            {
                ["mixed-session-pilot"] = new("mixed-session-pilot", "inputs/mixed", new string('f', 64), "2026-10-30T16:36:36Z"),
            });
        Assert.Equal(AppAvailability.Unavailable, after.Availability);
        Assert.NotEmpty(after.Gaps);
        CatalogValidator.ValidateDescriptor(after);
    }

    [Fact]
    public void MixedWithBridgeAndReceiptGoesAvailable()
    {
        var app = Fx.Descriptor("fixture-mixed",
            capabilities: AppCapability.Mixed,
            entry: new AppEntry("mixed-session", "FixtureMixed", null, null,
                new AppBridge("interop-host", "mixed-session-v1")));
        var after = LocalInputMapper.Refresh(app, Receipts("fixture-mixed"));
        Assert.Equal(AppAvailability.Available, after.Availability);
        CatalogValidator.ValidateDescriptor(after);
    }

    [Fact]
    public void StagedBytesWithoutReceiptStayMissingInput()
    {
        // RVW-1104-2100 Job1: bytes sitting in a staged app folder (entry
        // assembly + preparation.json) never promote on their own — only a
        // vetted import-lane receipt does. A WinForms native-demo entry
        // refreshed with no receipts keeps its app-folder gap, even though
        // the folder may exist on disk (Refresh takes no disk input).
        var app = Fx.Descriptor("fixture-benchmark",
            capabilities: AppCapability.WinForms,
            family: "winforms",
            transformVersion: "wf-rewrite-v1");
        var after = LocalInputMapper.Refresh(app,
            new Dictionary<string, LocalInputMapper.StagedReceipt>(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(AppAvailability.MissingInput, after.Availability);
        Assert.NotEmpty(after.Gaps);
        Assert.Contains(after.Gaps, g => g.Contains("fixture-benchmark", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Missing local input", after.AvailabilityNote, StringComparison.Ordinal);
        Assert.DoesNotContain(after.Evidence, e => e.StartsWith("local:", StringComparison.Ordinal));
        CatalogValidator.ValidateDescriptor(after);
    }

    [Fact]
    public void ReceiptBackedBenchmarkEntriesRetainBytePinsAndCurrentLaunchGates()
    {
        // RVW-1104-2100 JobA: the five staged+prepared benchmark apps flipped
        // by vetted entry-file receipts (foreman RULING: entry SHAs are byte
        // facts) plus entry-assembly transform proof. Each must stay
        // Available with its byte-pinned local: evidence, empty gaps, and a
        // runtime-unverified note. Asserted-directory provenance (e.g. the
        // fiddler pin) is never presented as a byte fact here.
        var registry = CatalogSerializer.Deserialize(
            File.ReadAllText(Path.Combine(Fx.CatalogDir(), "apps.json")));
        foreach (string id in new[]
        {
            "benchmark-openpdn", "benchmark-pkhex", "benchmark-keepass",
            "benchmark-linqpad5", "benchmark-advanceddatagridview",
            "benchmark-external-calculator",
        })
        {
            var app = registry.Resolve(id);
            Assert.Contains(app.Evidence, e =>
                e.StartsWith("local:.artifacts/benchmark-apps/" + id + "/", StringComparison.Ordinal));
            // Preparation is not evidence of a successful boot. These three
            // original apps now have observed faults/timeouts and stay gated.
            bool faulted = id is "benchmark-openpdn" or "benchmark-pkhex" or "benchmark-linqpad5";
            Assert.Equal(faulted ? AppAvailability.Unavailable : AppAvailability.Available, app.Availability);
            if (faulted)
            {
                Assert.NotEmpty(app.Gaps);
                Assert.Contains("Gated:", app.AvailabilityNote, StringComparison.Ordinal);
            }
            else
            {
                Assert.Empty(app.Gaps);
                Assert.Contains("unverified", app.AvailabilityNote, StringComparison.OrdinalIgnoreCase);
            }
            CatalogValidator.ValidateDescriptor(app);
            Assert.Equal(!faulted, Assert.Single(registry.Menu(), m => m.Id == id).IsLaunchable);
        }
    }

    [Theory]
    [InlineData("chip8")]
    [InlineData("cyotek")]
    [InlineData("dockpanel")]
    [InlineData("fastcoloredtextbox")]
    [InlineData("hexbox")]
    [InlineData("krypton")]
    [InlineData("minesweeper")]
    [InlineData("vgapainter")]
    public void NewlyStagedBenchmarkEntriesCarryFileDigestProvenance(string suffix)
    {
        var registry = CatalogSerializer.Deserialize(
            File.ReadAllText(Path.Combine(Fx.CatalogDir(), "apps.json")));
        var app = registry.Resolve("benchmark-" + suffix);
        Assert.Equal(AppAvailability.Available, app.Availability);
        Assert.Empty(app.Gaps);
        var pin = Assert.Single(app.Evidence, e => e.StartsWith("local:", StringComparison.Ordinal));
        Assert.StartsWith("local:.artifacts/benchmark-apps/" + app.Id + "/", pin);
        var digest = pin[(pin.LastIndexOf('#') + 1)..];
        Assert.Equal(64, digest.Length);
        Assert.All(digest, c => Assert.True(char.IsAsciiHexDigit(c)));
        Assert.Contains("runtime unverified", app.AvailabilityNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StagedBytesAreNotVerifiedRuntime()
    {
        // The mapper caps at Available; there is no verified state to reach.
        var after = LocalInputMapper.Refresh(Fx.Descriptor("fixture-app"), Receipts("fixture-app"));
        Assert.Equal(AppAvailability.Available, after.Availability);
        Assert.Contains("unverified", after.AvailabilityNote, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(after.AvailabilityNote, "verified runtime");
    }
}
