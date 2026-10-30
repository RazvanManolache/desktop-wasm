using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// ScreenToGif tab/progress/tooltip slice repro (fails before the slice).
/// Original call sites:
/// - ScreenToGif/Controls/HideableTabControl.cs: `class HideableTabControl
///   : TabControl` with SelectedIndex/Items.Count cycling on
///   PreviewMouseWheel, `TabItem_PreviewMouseDown` setting
///   `selected.IsSelected = true`, `SelectedItemProperty` animation and
///   `_tabPanel.Children` TabItem wiring.
/// - ScreenToGif/Controls/AwareTabItem.cs: `class AwareTabItem : TabItem`.
/// - ScreenToGif/Controls/CircularProgressBar.cs: `class
///   CircularProgressBar : ProgressBar` subscribing ValueChanged with
///   RoutedPropertyChangedEventArgs&lt;double&gt; and computing Percentage
///   from Value/Minimum/Maximum.
/// - ScreenToGif/Controls/ExtendedProgressBar.cs: `class
///   ExtendedProgressBar : ProgressBar`.
/// - ScreenToGif/Controls/HeaderedTooltip.cs: `class HeaderedTooltip :
///   ToolTip`.
/// - ScreenToGif/App.xaml.cs: `ToolTipService.ShowDurationProperty.
///   OverrideMetadata(...)`.
/// - ScreenToGif/Windows/Other/RegionSelection.xaml.cs:
///   `ToolTipService.SetPlacement(...)`.
/// - Dozens of XAML files: `ToolTip="..."`,
///   `ToolTipService.Placement="Top|Bottom|Right|Left"`,
///   `ToolTipService.HorizontalOffset="-5"`,
///   `ToolTipService.VerticalOffset="-5"`, `&lt;ToolTip Content="..."
///   Placement="Left" VerticalOffset="-5"/&gt;`, ProgressBar
///   Value/Minimum/Maximum/IsIndeterminate, TabPanel IsItemsHost.
/// Pointer dispatch runs through process-wide input statics, so this joins
/// the serialized ApplicationState collection like the other interaction
/// tests.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class TabProgressTooltipTests
{
    private static (WasmInputRouter Router, WpfInputManager Manager) Harness(UIElement content)
    {
        var window = new Window { Content = content };
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        manager.AddRoot(window);
        var mil = new WasmMilCore(new DesktopWasm.Graphics.SkiaSurface(800, 600));
        mil.Render(window, new WpfSize(800, 600), new WpfRect(0, 0, 800, 600));
        return (router, manager);
    }

    private static readonly WasmWindowHandle Handle = new(1);

    private static void Down(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, true));

    private static void Hover(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(Handle, x, y, WasmPointerButton.Left, false)
        { Kind = WasmPointerKind.Move });

    private static TabControl TwoTabs(out TabItem first, out TabItem second)
    {
        var tabs = new TabControl { Width = 400, Height = 200 };
        first = new TabItem { Header = "One", Content = "First page" };
        second = new TabItem { Header = "Two", Content = "Second page" };
        tabs.AddChild(first);
        tabs.AddChild(second);
        return tabs;
    }

    [Fact]
    public void Tab_selection_switches_content_and_fires_changed()
    {
        var tabs = TwoTabs(out _, out _);
        // Adding pages auto-selects the first enabled one, like WPF.
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.Equal("First page", tabs.SelectedContent);

        var added = new List<object>();
        var removed = new List<object>();
        tabs.SelectionChanged += (_, e) =>
        {
            added.AddRange(e.AddedItems.Cast<object>());
            removed.AddRange(e.RemovedItems.Cast<object>());
        };

        tabs.SelectedIndex = 1;
        Assert.Same(tabs.Items[1], tabs.SelectedItem);
        Assert.Equal("Second page", tabs.SelectedContent);
        Assert.Equal([tabs.Items[1]], added);
        Assert.Equal([tabs.Items[0]], removed);
    }

    [Fact]
    public void TabItem_IsSelected_is_two_way_with_owner()
    {
        var tabs = TwoTabs(out var first, out var second);

        second.IsSelected = true;
        Assert.Equal(1, tabs.SelectedIndex);
        Assert.True(second.IsSelected);
        Assert.False(first.IsSelected);

        tabs.SelectedIndex = 0;
        Assert.True(first.IsSelected);
        Assert.False(second.IsSelected);

        first.IsSelected = false;
        Assert.Equal(-1, tabs.SelectedIndex);
        Assert.Null(tabs.SelectedContent);
    }

    [Fact]
    public void Tab_header_press_selects_page_and_paints_it()
    {
        var tabs = TwoTabs(out _, out _);
        var (router, _) = Harness(tabs);

        var second = tabs.HeaderRect(1);
        Assert.True(second.Width > 0);
        Down(router, second.X + second.Width / 2, second.Y + second.Height / 2);
        Assert.Equal(1, tabs.SelectedIndex);

        var commands = WasmMilCore.RenderHeadless(tabs, new WpfSize(400, 200));
        Assert.Contains(commands.OfType<DrawCommand.DrawText>(), t => t.Text == "Second page");
        Assert.Contains(commands.OfType<DrawCommand.DrawText>(), t => t.Text == "One");
        Assert.DoesNotContain(commands.OfType<DrawCommand.DrawText>(), t => t.Text == "First page");
        // The selected content lands below the 28px header strip.
        Assert.True(tabs.ContentRect.Y >= 28);
    }

    [Fact]
    public void Tab_disabled_page_keeps_selection()
    {
        var tabs = TwoTabs(out _, out var second);
        second.IsEnabled = false;
        var (router, _) = Harness(tabs);

        var slot = tabs.HeaderRect(1);
        Down(router, slot.X + slot.Width / 2, slot.Y + slot.Height / 2);
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.Equal("First page", tabs.SelectedContent);
    }

    [Fact]
    public void Tab_progress_tooltip_load_from_xaml()
    {
        const string xaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<TabControl TabStripPlacement=\"Bottom\" SelectedIndex=\"1\">" +
            "<TabItem Header=\"A\" Content=\"Alpha\" />" +
            "<TabItem Header=\"B\" Content=\"Beta\" IsSelected=\"True\" />" +
            "</TabControl>" +
            "<ProgressBar Minimum=\"0\" Maximum=\"200\" Value=\"50\" Orientation=\"Horizontal\" />" +
            "<TabPanel IsItemsHost=\"True\" />" +
            "<Button ToolTip=\"Hint\" ToolTipService.Placement=\"Top\" ToolTipService.HorizontalOffset=\"-5\" />" +
            "</Grid>";
        var root = XamlLoader.Load(xaml).Root;
        var panel = Assert.IsType<Grid>(root);

        var tabs = panel.LogicalChildren.OfType<TabControl>().Single();
        Assert.Equal(Dock.Bottom, tabs.TabStripPlacement);
        Assert.Equal(2, tabs.Items.Count);
        Assert.Equal(1, tabs.SelectedIndex);
        Assert.Equal("Beta", tabs.SelectedContent);
        Assert.True(((TabItem)tabs.Items[1]).IsSelected);

        var bar = panel.LogicalChildren.OfType<ProgressBar>().Single();
        Assert.Equal(0, bar.Minimum);
        Assert.Equal(200, bar.Maximum);
        Assert.Equal(50, bar.Value);

        var strip = panel.LogicalChildren.OfType<TabPanel>().Single();
        Assert.True(strip.IsItemsHost);

        var button = panel.LogicalChildren.OfType<Button>().Single();
        Assert.Equal("Hint", button.ToolTip);
        Assert.Equal(PlacementMode.Top, ToolTipService.GetPlacement(button));
        Assert.Equal(-5, ToolTipService.GetHorizontalOffset(button));
    }

    [Fact]
    public void ProgressBar_coerces_value_and_paints_fraction()
    {
        var bar = new ProgressBar { Width = 200, Height = 20, Minimum = 10, Maximum = 110 };
        Assert.Equal(10, bar.Value); // coerced into the range on the Minimum edit, like WPF
        Assert.Equal(100, new ProgressBar().Maximum); // WPF 0..100 default

        var changes = new List<(double Old, double New)>();
        bar.ValueChanged += (_, e) => changes.Add((e.OldValue, e.NewValue));

        bar.Value = 60;
        bar.Value = 500; // clamps to Maximum
        bar.Value = 110; // equal coerced set stays silent
        Assert.Equal(110, bar.Value);
        Assert.Equal([(10, 60), (60, 110)], changes);

        WasmMilCore.RenderHeadless(bar, new WpfSize(200, 20));
        // Full value fills the whole 198px track (200px slot minus border).
        Assert.Equal(198, bar.FillRect().Width, precision: 3);

        bar.Value = 60; // (60-10)/(110-10) = half of the 198px track
        WasmMilCore.RenderHeadless(bar, new WpfSize(200, 20));
        var commands = WasmMilCore.RenderHeadless(bar, new WpfSize(200, 20));
        var fill = Assert.Single(commands.OfType<DrawCommand.FillRect>(), f => f.Rgba == 0x0078D7FFu);
        Assert.Equal(99, fill.W, precision: 3);
    }

    [Fact]
    public void ProgressBar_indeterminate_paints_marquee_block()
    {
        var bar = new ProgressBar { Width = 200, Height = 20, IsIndeterminate = true };
        var commands = WasmMilCore.RenderHeadless(bar, new WpfSize(200, 20));
        var block = Assert.Single(commands.OfType<DrawCommand.FillRect>(), f => f.Rgba == 0x0078D7FFu);
        Assert.Equal(49.5, block.W, precision: 3); // quarter-track block, centered
        Assert.Equal(75.25, block.X, precision: 3);
    }

    [Fact]
    public void Tooltip_hover_opens_string_hint_and_closes_on_leave()
    {
        var button = new Button { Content = "Save", Width = 120, Height = 32 };
        var panel = new StackPanel();
        panel.AddChild(button);
        ToolTipService.SetToolTip(button, "Saves the file");
        var (router, _) = Harness(panel);

        // The shared card inflates on first hover, like real WPF.
        Assert.Empty(button.LogicalChildren.OfType<ToolTip>());
        var r = button.RenderRect;
        Hover(router, r.X + r.Width / 2, r.Y + r.Height / 2);
        var tip = Assert.Single(button.LogicalChildren.OfType<ToolTip>());
        Assert.True(tip.IsOpen);
        Assert.Equal("Saves the file", tip.Content);

        var opened = 0; var closed = 0;
        tip.Opened += (_, _) => opened++;
        tip.Closed += (_, _) => closed++;

        var commands = WasmMilCore.RenderHeadless(panel, new WpfSize(800, 600));
        Assert.Contains(commands.OfType<DrawCommand.FillRect>(), f => f.Rgba == 0xFFFFE1FFu);
        Assert.Contains(commands.OfType<DrawCommand.DrawText>(), t => t.Text == "Saves the file");

        Hover(router, 700, 500); // off the button, still on the panel
        Assert.False(tip.IsOpen);
        Assert.Equal(1, closed);

        Hover(router, r.X + r.Width / 2, r.Y + r.Height / 2);
        Assert.True(tip.IsOpen);
        Assert.Equal(1, opened);
    }

    [Fact]
    public void Tooltip_element_keeps_its_placement_and_reports_events()
    {
        var button = new Button { Content = "Save", Width = 120, Height = 32 };
        var panel = new StackPanel();
        panel.AddChild(button);
        var tip = new ToolTip
        {
            Content = "Card",
            Placement = PlacementMode.Top,
            HorizontalOffset = -5,
            VerticalOffset = -2,
        };
        var opened = 0; var closed = 0;
        tip.Opened += (_, _) => opened++;
        tip.Closed += (_, _) => closed++;
        ToolTipService.SetToolTip(button, tip);
        var (router, _) = Harness(panel);

        var r = button.RenderRect;
        Hover(router, r.X + 10, r.Y + 10);
        Assert.True(tip.IsOpen);
        Assert.Equal(1, opened);

        var commands = WasmMilCore.RenderHeadless(panel, new WpfSize(800, 600));
        Assert.Contains(commands.OfType<DrawCommand.DrawText>(), t => t.Text == "Card");
        // Top-anchored with the configured nudges.
        Assert.Equal(r.X - 5, tip.TipRect().X, precision: 3);
        Assert.Equal(r.Y - tip.TipRect().Height - 2, tip.TipRect().Y, precision: 3);

        Hover(router, 700, 500);
        Assert.False(tip.IsOpen);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void TooltipService_attached_roundtrip_matches_original_shapes()
    {
        var button = new Button { Width = 100, Height = 30 };

        Assert.Null(ToolTipService.GetToolTip(button));
        ToolTipService.SetToolTip(button, "Hint");
        Assert.Equal("Hint", ToolTipService.GetToolTip(button));

        ToolTipService.SetPlacement(button, PlacementMode.Right);
        Assert.Equal(PlacementMode.Right, ToolTipService.GetPlacement(button));
        ToolTipService.SetHorizontalOffset(button, -5);
        Assert.Equal(-5, ToolTipService.GetHorizontalOffset(button));
        ToolTipService.SetVerticalOffset(button, -2);
        Assert.Equal(-2, ToolTipService.GetVerticalOffset(button));

        Assert.Equal(5000, ToolTipService.GetShowDuration(new Button()));
        ToolTipService.SetShowDuration(button, 9000);
        Assert.Equal(9000, ToolTipService.GetShowDuration(button));

        // ScreenToGif App startup overrides the duration app-wide; the DP
        // must accept that OverrideMetadata call.
        ToolTipService.ShowDurationProperty.OverrideMetadata(
            typeof(DependencyObject), new FrameworkPropertyMetadata(int.MaxValue));
    }

    [Fact]
    public void Tabs_lab_builds_with_initial_readouts()
    {
        // Engine-native demo proof: the lab the browser proof tours reports
        // exact readout strings through the real pointer path.
        var first = DesktopWasm.BrowserHost.DemoCatalog.Find("tabs").Build();
        var second = DesktopWasm.BrowserHost.DemoCatalog.Find("tabs").Build();
        Assert.NotSame(first, second);
        Assert.Equal("Tabs lab", first.Title);

        var lab = DesktopWasm.BrowserHost.TabsLab.Current;
        Assert.NotNull(lab);
        var (router, _) = Harness(second);
        Assert.Equal("Tab: 0/Home", lab.TabReadout.Text);

        var second2 = lab.Tabs.HeaderRect(1);
        Down(router, second2.X + second2.Width / 2, second2.Y + second2.Height / 2);
        Assert.Equal("Tab: 1/Profile", lab.TabReadout.Text);
        _ = router;
    }

    [Fact]
    public void TabPanel_stacks_children_horizontally()
    {
        var strip = new TabPanel { IsItemsHost = true };
        var first = new Border { Width = 50, Height = 20 };
        var second = new Border { Width = 70, Height = 20 };
        strip.AddChild(first);
        strip.AddChild(second);

        WasmMilCore.RenderHeadless(strip, new WpfSize(300, 100));
        Assert.Equal(50, first.RenderRect.Width, precision: 3);
        Assert.Equal(0, first.RenderRect.X, precision: 3);
        Assert.Equal(50, second.RenderRect.X, precision: 3);
        Assert.Equal(70, second.RenderRect.Width, precision: 3);
    }
}
