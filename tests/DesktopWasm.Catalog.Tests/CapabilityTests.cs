using DesktopWasm.AppModel;

namespace DesktopWasm.Catalog.Tests;

public sealed class CapabilityTests
{
    [Fact]
    public void BothSingleCapabilitiesValidate()
    {
        CatalogValidator.ValidateDescriptor(Fx.Descriptor("fixture-wf", capabilities: AppCapability.WinForms));
        CatalogValidator.ValidateDescriptor(Fx.Descriptor("fixture-wpf", capabilities: AppCapability.Wpf));
    }

    [Fact]
    public void MixedWithTypedBridgeAndAvailableValidates()
    {
        var app = Fx.Descriptor("fixture-mixed",
            capabilities: AppCapability.Mixed,
            availability: AppAvailability.Available,
            availabilityNote: "staged",
            gaps: [],
            entry: new AppEntry("mixed-session", "FixtureMixed", null,
                "Session proved by interop lane.",
                new AppBridge("interop-host", "mixed-session-v1")));
        CatalogValidator.ValidateDescriptor(app);
    }

    [Fact]
    public void MixedAvailableWithProseOnlyBridgeFails()
    {
        // Review item 6: a BootNote mentioning "bridge" is not readiness.
        var app = Fx.Descriptor("fixture-mixed-prose",
            capabilities: AppCapability.Mixed,
            availability: AppAvailability.Available,
            availabilityNote: "staged",
            gaps: [],
            entry: new AppEntry("mixed-session", "FixtureMixed", null,
                "This bridge note mentions the bridge keyword."));
        var ex = Assert.Throws<InvalidDataException>(() => CatalogValidator.ValidateDescriptor(app));
        Assert.Contains("typed bridge identity", ex.Message);
    }

    [Fact]
    public void MixedAvailableWithNullBridgeFails()
    {
        var app = Fx.Descriptor("fixture-mixed-nobridge",
            capabilities: AppCapability.Mixed,
            availability: AppAvailability.Available,
            availabilityNote: "staged",
            gaps: []);
        Assert.Throws<InvalidDataException>(() => CatalogValidator.ValidateDescriptor(app));
    }

    [Fact]
    public void UnknownCapabilityFlagsRejected()
    {
        var app = Fx.Descriptor("fixture-weird", capabilities: (AppCapability)4);
        var ex = Assert.Throws<InvalidDataException>(() => CatalogValidator.ValidateDescriptor(app));
        Assert.Contains("Unknown capability", ex.Message);
    }

    [Fact]
    public void MixedPilotIsUnavailableWithoutBridge()
    {
        var pilot = LegacyAdapters.MixedPilot();
        Assert.Equal(AppCapability.Mixed, pilot.Capabilities);
        Assert.Equal(AppAvailability.Unavailable, pilot.Availability);
        Assert.Null(pilot.Entry.Bridge);
        CatalogValidator.ValidateDescriptor(pilot); // unavailable + no bridge is honest
    }
}
