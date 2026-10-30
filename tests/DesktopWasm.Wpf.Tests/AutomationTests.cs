using System.Text;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Gallery automation slice: ColorTile/ControlExample/IconDataField copy
/// handlers create a peer for the clicked element and raise a notification
/// (Other/ImportantMostRecent with a copied-text message), and gallery XAML
/// sets AutomationProperties.Name/HelpText/HeadingLevel. Announcements must
/// be observable (event + record), never dropped.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class AutomationTests : IDisposable
{
    private readonly Application? _previousApp = Application.Current;

    public AutomationTests()
    {
        Application.Current = new Application();
        AutomationPeer.ClearRaisedForTests();
    }

    public void Dispose()
    {
        AutomationPeer.ClearRaisedForTests();
        Application.Current = _previousApp;
    }

    private static Window Load(string xaml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xaml));
        return XamlLoader.LoadComponent<Window>(stream);
    }

    [Fact]
    public void Notification_contract_matches_wpf_numeric_values()
    {
        Assert.Equal(4, (int)AutomationNotificationKind.Other);
        Assert.Equal(0, (int)AutomationNotificationKind.ItemAdded);
        Assert.Equal(1, (int)AutomationNotificationKind.ItemRemoved);
        Assert.Equal(2, (int)AutomationNotificationKind.ActionCompleted);
        Assert.Equal(3, (int)AutomationNotificationKind.ActionAborted);
        Assert.Equal(0, (int)AutomationNotificationProcessing.ImportantAll);
        Assert.Equal(1, (int)AutomationNotificationProcessing.ImportantMostRecent);
        Assert.Equal(2, (int)AutomationNotificationProcessing.All);
        Assert.Equal(3, (int)AutomationNotificationProcessing.MostRecent);
        Assert.Equal(4, (int)AutomationNotificationProcessing.CurrentThenMostRecent);
    }

    [Fact]
    public void Heading_contract_matches_wpf_numeric_values()
    {
        Assert.Equal(0, (int)AutomationHeadingLevel.None);
        Assert.Equal(1, (int)AutomationHeadingLevel.Level1);
        Assert.Equal(2, (int)AutomationHeadingLevel.Level2);
        Assert.Equal(9, (int)AutomationHeadingLevel.Level9);
    }

    [Fact]
    public void CreatePeerForElement_returns_bound_peer_and_null_for_null()
    {
        var button = new Button { Content = "Copy" };
        var peer = UIElementAutomationPeer.CreatePeerForElement(button);
        Assert.NotNull(peer);
        Assert.IsType<UIElementAutomationPeer>(peer);
        Assert.Same(button, ((UIElementAutomationPeer)peer).Owner);
        Assert.Null(UIElementAutomationPeer.CreatePeerForElement(null));
        Assert.Throws<ArgumentNullException>(() => new UIElementAutomationPeer(null!));
    }

    [Fact]
    public void Gallery_copy_pattern_raises_observable_announcement()
    {
        // Mirrors ColorTile.Copy_ColorBrushName: peer for the clicked
        // element + RaiseNotificationEvent(Other, ImportantMostRecent, ...).
        var button = new Button { Content = "Copy" };
        AutomationNotificationEventArgs? forwarded = null;
        AutomationPeer.NotificationRaised += OnRaised;
        try
        {
            AutomationPeer peer = UIElementAutomationPeer.CreatePeerForElement(button)!;
            peer.RaiseNotificationEvent(
                AutomationNotificationKind.Other,
                AutomationNotificationProcessing.ImportantMostRecent,
                "Color Brush Name Copied",
                "ButtonClickedActivity");
            Assert.NotNull(forwarded);
            Assert.Same(peer, forwarded.Peer);
            Assert.Equal(AutomationNotificationKind.Other, forwarded.Kind);
            Assert.Equal(AutomationNotificationProcessing.ImportantMostRecent, forwarded.Processing);
            Assert.Equal("Color Brush Name Copied", forwarded.Text);
            Assert.Equal("ButtonClickedActivity", forwarded.ActivityId);
            Assert.Same(forwarded, AutomationPeer.LastRaised);
        }
        finally
        {
            AutomationPeer.NotificationRaised -= OnRaised;
        }

        void OnRaised(object? sender, AutomationNotificationEventArgs e) => forwarded = e;
    }

    [Fact]
    public void Peer_name_reads_xaml_set_accessible_name()
    {
        var button = new Button { Content = "Copy" };
        Assert.Null(UIElementAutomationPeer.CreatePeerForElement(button)!.GetName());
        AutomationProperties.SetName(button, "Copy XAML Code");
        Assert.Equal("Copy XAML Code", AutomationProperties.GetName(button));
        Assert.Equal("Copy XAML Code", UIElementAutomationPeer.CreatePeerForElement(button)!.GetName());
    }

    [Fact]
    public void AutomationProperties_round_trip_help_and_heading()
    {
        var button = new Button { Content = "Copy" };
        Assert.Equal(string.Empty, AutomationProperties.GetHelpText(button));
        Assert.Equal(AutomationHeadingLevel.None, AutomationProperties.GetHeadingLevel(button));
        AutomationProperties.SetHelpText(button, "Copies the value");
        AutomationProperties.SetHeadingLevel(button, AutomationHeadingLevel.Level2);
        Assert.Equal("Copies the value", AutomationProperties.GetHelpText(button));
        Assert.Equal(AutomationHeadingLevel.Level2, AutomationProperties.GetHeadingLevel(button));
    }

    [Fact]
    public void Loader_accepts_automation_attributes_and_property_element()
    {
        var window = Load(
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<StackPanel>" +
            "<Button AutomationProperties.Name=\"Scroll left\" AutomationProperties.HelpText=\"Moves back\" AutomationProperties.HeadingLevel=\"Level1\" />" +
            "<Button><AutomationProperties.Name>Scroll right</AutomationProperties.Name></Button>" +
            "</StackPanel></Window>");
        var panel = (StackPanel)((Window)window).Content!;
        var first = (Button)panel.LogicalChildren[0];
        var second = (Button)panel.LogicalChildren[1];
        Assert.Equal("Scroll left", AutomationProperties.GetName(first));
        Assert.Equal("Moves back", AutomationProperties.GetHelpText(first));
        Assert.Equal(AutomationHeadingLevel.Level1, AutomationProperties.GetHeadingLevel(first));
        Assert.Equal("Scroll right", AutomationProperties.GetName(second));
    }

    [Fact]
    public void Loader_rejects_bad_heading_and_nested_property_content()
    {
        var badHeading = Assert.Throws<XamlLoadException>(() => Load(
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Button AutomationProperties.HeadingLevel=\"Level99\" /></Window>"));
        Assert.Contains("AutomationHeadingLevel", badHeading.Message);
        var nested = Assert.Throws<XamlLoadException>(() => Load(
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Button><AutomationProperties.Name><MultiBinding /></AutomationProperties.Name></Button></Window>"));
        Assert.Contains("needs at least one <Binding>", nested.Message);
        var unknown = Assert.Throws<XamlLoadException>(() => Load(
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Button AutomationProperties.LiveSetting=\"Polite\" /></Window>"));
        Assert.Contains("Unknown attached property", unknown.Message);
    }
}
