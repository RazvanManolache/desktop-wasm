using DesktopWasm.BrowserHost;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Gallery automation slice lab: the ColorTile copy pattern (stage text,
/// announce through a peer) and the MainWindow/HeaderTile preference
/// pattern (SystemEvents raise repaints) run through the real engine paths.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class A11yEventsTests : IDisposable
{
    private readonly Application? _previousApp = Application.Current;

    public A11yEventsTests()
    {
        WasmDispatcher.Current = new WasmDispatcher(new WasmDispatcherPump());
        WasmVirtualFileSystem.Current = new WasmVirtualFileSystem();
        Clipboard.Provider = new InMemoryClipboard();
        Clipboard.Clear();
        Application.Current = new Application();
        AutomationPeer.ClearRaisedForTests();
    }

    public void Dispose()
    {
        if (A11yEventsLab.Current is not null) A11yEventsLab.Current.Detach();
        A11yEventsLab.Current?.AnnounceNote?.Invoke(string.Empty, string.Empty);
        AutomationPeer.ClearRaisedForTests();
        Application.Current = _previousApp;
    }

    [Fact]
    public void Lab_copy_announces_through_peer_and_readout()
    {
        var window = DemoCatalog.Find("a11y-events").Build();
        var lab = A11yEventsLab.Current;
        Assert.NotNull(lab);
        Assert.Same(window, lab.Window);
        var mirrored = new List<(string Text, string ActivityId)>();
        lab.AnnounceNote = (text, activity) => mirrored.Add((text, activity));

        lab.CopyInput.Text = "Ocean #3169A0";
        lab.CopyAndAnnounce();

        Assert.Equal("Ocean #3169A0", Clipboard.GetText());
        Assert.NotNull(AutomationPeer.LastRaised);
        Assert.Equal(AutomationNotificationKind.Other, AutomationPeer.LastRaised.Kind);
        Assert.Equal("Ocean #3169A0 Copied", AutomationPeer.LastRaised.Text);
        Assert.Equal("ButtonClickedActivity", AutomationPeer.LastRaised.ActivityId);
        Assert.Equal("Announced (Other/ImportantMostRecent): Ocean #3169A0 Copied [ButtonClickedActivity]",
            lab.AnnounceReadout.Text);
        Assert.Single(mirrored);
        Assert.Equal(("Ocean #3169A0 Copied", "ButtonClickedActivity"), mirrored[0]);
    }

    [Fact]
    public void Lab_theme_change_refreshes_through_preference_event()
    {
        DemoCatalog.Find("a11y-events").Build();
        var lab = A11yEventsLab.Current;
        Assert.NotNull(lab);
        Assert.Equal("Theme: Light (General)", lab.PrefsReadout.Text);

        lab.SimulateThemeChange();
        Assert.Equal("Theme: Dark (General)", lab.PrefsReadout.Text);
        Assert.Equal(0x2B3A36FFu, lab.Swatch.BackgroundRgba);

        lab.SimulateThemeChange();
        Assert.Equal("Theme: Light (General)", lab.PrefsReadout.Text);
        Assert.Equal(0xEDF4EBFFu, lab.Swatch.BackgroundRgba);
    }

    [Fact]
    public void Lab_buttons_carry_accessible_names()
    {
        DemoCatalog.Find("a11y-events").Build();
        var lab = A11yEventsLab.Current;
        Assert.NotNull(lab);
        Assert.Equal("Copy swatch name", AutomationProperties.GetName(lab.CopyButton));
        Assert.Equal("Simulate theme change", AutomationProperties.GetName(lab.ThemeButton));
        Assert.Equal("Copy swatch name", UIElementAutomationPeer.CreatePeerForElement(lab.CopyButton)!.GetName());
    }
}
