using DesktopWasm.Wpf;
using Microsoft.Win32;

namespace DesktopWasm.BrowserHost;

// Engine-native Gallery automation slice: the ColorTile/ControlExample/
// IconDataField copy handlers announce through UIElementAutomationPeer +
// RaiseNotificationEvent, and MainWindow/HeaderTile refresh on
// SystemEvents.UserPreferenceChanged. The lab runs those exact engine paths:
// Copy stages session text then announces it (the readout mirrors the
// announcement a screen reader would voice); Theme change drives the
// preference event and repaints the swatch (the gallery refresh pattern).
// Pure engine surface so the headless suite links it directly; the browser
// mirrors announcements into a DOM aria-live region through AnnounceNote.
public sealed class A11yEventsLab
{
    public static A11yEventsLab? Current { get; private set; }

    public Window Window { get; } = new() { Title = "Announcements & preferences lab" };

    public TextBox CopyInput { get; } = new() { Text = "Forest #156E61", Width = 560 };

    public Button CopyButton { get; } = new() { Content = "Copy + announce", Width = 170, Height = 38, Margin = new Thickness(0, 8, 0, 8) };

    public TextBlock AnnounceReadout { get; } = new() { Text = "No announcement yet" };

    public TextBlock PrefsReadout { get; } = new() { Text = "Preferences idle" };

    public Button ThemeButton { get; } = new() { Content = "Simulate theme change", Width = 220, Height = 38, Margin = new Thickness(0, 8, 0, 8) };

    public Border Swatch { get; } = new() { Width = 560, Height = 120, CornerRadius = new CornerRadius(12) };

    /// <summary>Browser mirror for announcements (DOM aria-live; null headless).</summary>
    public Action<string, string>? AnnounceNote { get; set; }

    private bool _dark;
    private bool _composed;

    public static Window Build()
    {
        Current?.Detach();
        var lab = new A11yEventsLab();
        Current = lab;
        lab.Compose();
        return lab.Window;
    }

    /// <summary>Unsubscribes static handlers (rebuilds must not double-announce).</summary>
    public void Detach()
    {
        AutomationPeer.NotificationRaised -= OnNotificationRaised;
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        _composed = false;
    }

    private void Compose()
    {
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.AddChild(new TextBlock { Text = "Announcements & preferences", FontCss = "28px sans-serif", ForegroundRgba = 0x214C40FF, Margin = new Thickness(0, 0, 0, 8) });
        panel.AddChild(new TextBlock { Text = "Gallery copy buttons announce through automation peers; theme changes refresh through SystemEvents.", FontCss = "13px sans-serif", ForegroundRgba = 0x7B8D82FF, Margin = new Thickness(0, 0, 0, 22) });
        AutomationProperties.SetName(CopyButton, "Copy swatch name");
        AutomationProperties.SetName(ThemeButton, "Simulate theme change");
        panel.AddChild(CopyInput);
        panel.AddChild(CopyButton);
        panel.AddChild(AnnounceReadout);
        panel.AddChild(PrefsReadout);
        panel.AddChild(ThemeButton);
        panel.AddChild(Swatch);
        Window.Content = panel;
        CopyButton.Click += (_, _) => CopyAndAnnounce();
        ThemeButton.Click += (_, _) => SimulateThemeChange();
        AutomationPeer.NotificationRaised += OnNotificationRaised;
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
        OnPreferenceChanged(this, new UserPreferenceChangedEventArgs(UserPreferenceCategory.General));
        _composed = true;
        WasmMilCore.RenderHeadless(Window, new WpfSize(800, 600));
    }

    /// <summary>Gallery copy pattern: stage text, then announce it through a peer.</summary>
    public void CopyAndAnnounce()
    {
        var text = CopyInput.Text;
        Clipboard.SetText(string.IsNullOrEmpty(text) ? "Untitled swatch" : text);
        AutomationPeer peer = UIElementAutomationPeer.CreatePeerForElement(CopyButton)!;
        peer.RaiseNotificationEvent(
            AutomationNotificationKind.Other,
            AutomationNotificationProcessing.ImportantMostRecent,
            $"{Clipboard.GetText()} Copied",
            "ButtonClickedActivity");
    }

    /// <summary>Host/theme driver: alternates the swatch through the preference event.</summary>
    public void SimulateThemeChange()
    {
        _dark = !_dark;
        SystemEvents.RaiseUserPreferenceChanged(UserPreferenceCategory.General);
    }

    private void OnNotificationRaised(object? sender, AutomationNotificationEventArgs e)
    {
        if (!_composed) return;
        AnnounceReadout.Text = $"Announced ({e.Kind}/{e.Processing}): {e.Text} [{e.ActivityId}]";
        AnnounceNote?.Invoke(e.Text, e.ActivityId);
    }

    // Gallery refresh pattern (MainWindow/HeaderTile): a preference change
    // repaints from current state; here the swatch alternates light/dark and
    // the readout names the category that drove it.
    private void OnPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        PrefsReadout.Text = $"Theme: {(_dark ? "Dark" : "Light")} ({e.Category})";
        Swatch.BackgroundRgba = _dark ? 0x2B3A36FF : 0xEDF4EBFF;
    }
}
