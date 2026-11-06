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
    public void StagedBytesAreNotVerifiedRuntime()
    {
        // The mapper caps at Available; there is no verified state to reach.
        var after = LocalInputMapper.Refresh(Fx.Descriptor("fixture-app"), Receipts("fixture-app"));
        Assert.Equal(AppAvailability.Available, after.Availability);
        Assert.Contains("unverified", after.AvailabilityNote, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(after.AvailabilityNote, "verified runtime");
    }
}
