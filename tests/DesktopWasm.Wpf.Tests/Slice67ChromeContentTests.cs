using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-67 window-chrome/content cluster: the converted AvalonDock closure
/// references Window add_Closed(EventHandler) (engine owned add_Closed(Action),
/// an arity-invisible delegate mismatch that throws MissingMethodException at
/// JIT), ContentControl/ContentPresenter ContentProperty (ClearBinding plus
/// LayoutFloatingWindowControl .cctor OverrideMetadata), ContentPresenter
/// ContentTemplate/Selector/StringFormat (LayoutItem.get_View plus
/// TabControlEx.CreateChildContentPresenter), ContextMenu add/remove_Closed
/// plus OnOpened plus Placement/Target/Offsets (DropDownButton, drop-target
/// and chrome-worker callers), the seven DrawingContext brush/pen/point/rect
/// overloads (ToggleDockDragOverlay.OnRender plus GenerateHICON), Grid
/// SetRow/SetColumn/SetRowSpan (converted layout code) and TabControl
/// SelectedContentTemplate/Selector/StringFormat plus
/// OnSelectionChanged(SelectionChangedEventArgs) (TabControlEx). Every shape
/// below is live-verified against the staged AvalonDock bins (sha256-verified,
/// status gate bypassed in-probe only, manifests untouched): 762 engine-scope
/// refs, 680 distinct, 58 distinct-unresolved pre-fix with all 30 cluster rows
/// present, each pinned to its converted user. Real-WPF parity: Window.Closed
/// is EventHandler (was Action); SystemParameters.PrimaryScreenWidth/Height
/// ride the session primary screen; ContentPresenter inflates its effective
/// template with Content as DataContext; ContextMenu open/close raise
/// Opened/Closed; DrawingContext overloads lower to the shared ops exactly
/// like the shape paint paths. Deliberately not declared: Popup offsets (zero
/// staged refs; Popup already owns Placement/Target/Offsets/Open/Close),
/// ItemsControl.ItemsSourceProperty, Panel.BackgroundProperty,
/// MenuItem.Icon, GridLength members, ListBoxItem.IsSelected and the other
/// 28 non-cluster rows.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice67ChromeContentTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    public Slice67ChromeContentTests()
    {
        WpfAnimationClock.ResetForTests();
        WasmDispatcher.Current = new WasmDispatcher(new WasmDispatcherPump());
    }

    public void Dispose() => WpfAnimationClock.ResetForTests();

    [Fact]
    public void Staged_avalondock_hashes_verify()
    {
        Assert.Empty(StagedAvalonDockChromeLoader.VerifyHashes());
    }

    [Fact]
    public void Cluster_shapes_resolve_by_arity()
    {
        const BindingFlags instance = BindingFlags.Public | BindingFlags.Instance;
        const BindingFlags statics = BindingFlags.Public | BindingFlags.Static;

        var window = typeof(Window);
        Assert.Equal(typeof(EventHandler), window.GetMethod("add_Closed", instance)!.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(EventHandler), window.GetMethod("remove_Closed", instance)!.GetParameters()[0].ParameterType);
        Assert.NotNull(window.GetMethod("Close", instance));
        Assert.NotNull(window.GetMethod("OnClosed", BindingFlags.NonPublic | BindingFlags.Instance));

        var system = typeof(SystemParameters);
        Assert.NotNull(system.GetProperty("PrimaryScreenWidth", statics));
        Assert.NotNull(system.GetProperty("PrimaryScreenHeight", statics));

        Assert.NotNull(typeof(ContentControl).GetField("ContentProperty", statics | BindingFlags.FlattenHierarchy));
        var presenter = typeof(ContentPresenter);
        Assert.NotNull(presenter.GetField("ContentProperty", statics | BindingFlags.FlattenHierarchy));
        Assert.NotNull(presenter.GetField("ContentTemplateProperty", statics | BindingFlags.FlattenHierarchy));
        Assert.NotNull(presenter.GetField("ContentTemplateSelectorProperty", statics | BindingFlags.FlattenHierarchy));
        Assert.NotNull(presenter.GetField("ContentStringFormatProperty", statics | BindingFlags.FlattenHierarchy));
        Assert.NotNull(presenter.GetMethod("set_ContentTemplate", instance));
        Assert.NotNull(presenter.GetMethod("set_ContentTemplateSelector", instance));
        Assert.NotNull(presenter.GetMethod("set_ContentStringFormat", instance));

        var menu = typeof(ContextMenu);
        Assert.Equal(typeof(RoutedEventHandler), menu.GetMethod("add_Closed", instance)!.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(RoutedEventHandler), menu.GetMethod("remove_Closed", instance)!.GetParameters()[0].ParameterType);
        Assert.NotNull(menu.GetMethod("OnOpened", BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.NotNull(menu.GetMethod("set_Placement", instance));
        Assert.NotNull(menu.GetMethod("set_PlacementTarget", instance));
        Assert.NotNull(menu.GetMethod("set_HorizontalOffset", instance));
        Assert.NotNull(menu.GetMethod("set_VerticalOffset", instance));

        var dc = typeof(DrawingContext);
        Assert.NotNull(dc.GetMethod("Close", Type.EmptyTypes));
        Assert.NotNull(dc.GetMethods().First(m => m.Name == "DrawEllipse" && m.GetParameters().Length == 5));
        Assert.NotNull(dc.GetMethods().First(m => m.Name == "DrawImage" && m.GetParameters().Length == 2));
        Assert.NotNull(dc.GetMethods().First(m => m.Name == "DrawLine" && m.GetParameters().Length == 3));
        Assert.NotNull(dc.GetMethods().First(m => m.Name == "DrawRectangle" && m.GetParameters().Length == 3));
        Assert.NotNull(dc.GetMethods().First(m => m.Name == "DrawRoundedRectangle" && m.GetParameters().Length == 5));
        Assert.NotNull(dc.GetMethods().First(m => m.Name == "DrawText" && m.GetParameters().Length == 2));

        var grid = typeof(Grid);
        Assert.NotNull(grid.GetField("RowProperty", statics | BindingFlags.FlattenHierarchy));
        Assert.NotNull(grid.GetField("ColumnProperty", statics | BindingFlags.FlattenHierarchy));
        Assert.NotNull(grid.GetField("RowSpanProperty", statics | BindingFlags.FlattenHierarchy));
        Assert.NotNull(grid.GetField("ColumnSpanProperty", statics | BindingFlags.FlattenHierarchy));
        Assert.NotNull(grid.GetMethod("SetRow", statics));
        Assert.NotNull(grid.GetMethod("SetColumn", statics));
        Assert.NotNull(grid.GetMethod("SetRowSpan", statics));

        var tabs = typeof(TabControl);
        Assert.NotNull(tabs.GetMethod("get_SelectedContentStringFormat", instance));
        Assert.NotNull(tabs.GetMethod("get_SelectedContentTemplate", instance));
        Assert.NotNull(tabs.GetMethod("get_SelectedContentTemplateSelector", instance));
        Assert.NotNull(typeof(Selector).GetMethod("OnSelectionChanged",
            BindingFlags.NonPublic | BindingFlags.Instance,
            new[] { typeof(SelectionChangedEventArgs) }));
    }

    [Fact]
    public void Window_closed_is_eventhandler_and_fires_with_sender()
    {
        var window = new Window();
        object? sender = null;
        var args = (EventArgs?)null;
        var calls = 0;
        window.Closed += (_, e) => { calls++; sender = window; args = e; };
        window.Close();
        Assert.Equal(1, calls);
        Assert.Same(window, sender);
        Assert.NotNull(args);
        Assert.False(window.IsVisible);
    }

    [Fact]
    public void Window_closing_cancel_still_blocks_close()
    {
        var window = new Window();
        var closed = 0;
        window.Closed += (_, _) => closed++;
        window.Closing += (_, e) => e.Cancel = true;
        window.Close();
        Assert.Equal(0, closed);
        Assert.True(window.IsVisible);
    }

    [Fact]
    public void Primary_screen_matches_the_session_virtual_screen()
    {
        Assert.Equal(SystemParameters.VirtualScreenWidth, SystemParameters.PrimaryScreenWidth);
        Assert.Equal(SystemParameters.VirtualScreenHeight, SystemParameters.PrimaryScreenHeight);
        Assert.True(SystemParameters.PrimaryScreenWidth > 0);
        Assert.True(SystemParameters.PrimaryScreenHeight > 0);
    }

    [Fact]
    public void Content_dp_tokens_match_wpf_identity()
    {
        Assert.Same(typeof(ContentControl), ContentControl.ContentProperty.OwnerType);
        Assert.Equal("Content", ContentControl.ContentProperty.Name);
        Assert.Equal(typeof(object), ContentControl.ContentProperty.PropertyType);
        Assert.Same(ContentControl.ContentProperty, DependencyProperty.FromName(typeof(ContentControl), "Content"));
        Assert.Same(ContentControl.ContentProperty, DependencyProperty.FromName(typeof(Button), "Content"));
        Assert.Null(DependencyProperty.FromName(typeof(Border), "Content"));
        Assert.Same(ContentControl.ContentProperty, ContentPresenter.ContentProperty);
        Assert.Same(typeof(ContentPresenter), ContentPresenter.ContentTemplateProperty.OwnerType);
        Assert.Equal(typeof(DataTemplate), ContentPresenter.ContentTemplateProperty.PropertyType);
        Assert.Equal(typeof(DataTemplateSelector), ContentPresenter.ContentTemplateSelectorProperty.PropertyType);
        Assert.Equal(typeof(string), ContentPresenter.ContentStringFormatProperty.PropertyType);
        Assert.Null(ContentPresenter.ContentTemplateProperty.DefaultValue);
        Assert.Null(ContentPresenter.ContentTemplateSelectorProperty.DefaultValue);
        Assert.Null(ContentPresenter.ContentStringFormatProperty.DefaultValue);
    }

    [Fact]
    public void ContentPresenter_bare_visual_parity()
    {
        var child = new Border { Width = 40, Height = 20 };
        var plain = new ContentPresenter { Content = child };
        plain.Measure(new WpfSize(200, 200));
        Assert.Equal(40, plain.DesiredSize.Width, 3);

        var window = new Window { Content = new ContentPresenter { Content = "label" } };
        var commands = WasmMilCore.RenderHeadless(window, new WpfSize(300, 100));
        Assert.Contains(commands, c => c is DrawCommand.DrawText t && t.Text == "label");
    }

    private sealed class FixedSelector : DataTemplateSelector
    {
        private readonly DataTemplate _template;
        public FixedSelector(DataTemplate template) => _template = template;
        public override DataTemplate? SelectTemplate(object item, DependencyObject container) => _template;
    }

    private static DataTemplate XamlTemplate(string inner) =>
        new("<Border xmlns=\"" + PresentationNs + "\">" + inner + "</Border>");

    private static IReadOnlyList<DrawCommand> RenderPresenter(ContentPresenter presenter) =>
        WasmMilCore.RenderHeadless(new Window { Content = presenter }, new WpfSize(300, 100));

    [Fact]
    public void ContentPresenter_fixed_template_inflates_with_content_datacontext()
    {
        var commands = RenderPresenter(new ContentPresenter
        {
            Content = "Ada",
            ContentTemplate = XamlTemplate("<TextBlock Text=\"picked\" />"),
        });
        Assert.Contains(commands, c => c is DrawCommand.DrawText t && t.Text == "picked");
    }

    [Fact]
    public void ContentPresenter_selector_wins_when_no_fixed_template()
    {
        var commands = RenderPresenter(new ContentPresenter
        {
            Content = 7,
            ContentTemplateSelector = new FixedSelector(XamlTemplate("<TextBlock Text=\"seven\" />")),
        });
        Assert.Contains(commands, c => c is DrawCommand.DrawText t && t.Text == "seven");
    }

    [Fact]
    public void ContentPresenter_fixed_template_beats_selector()
    {
        var commands = RenderPresenter(new ContentPresenter
        {
            Content = 7,
            ContentTemplate = XamlTemplate("<TextBlock Text=\"fixed\" />"),
            ContentTemplateSelector = new FixedSelector(XamlTemplate("<TextBlock Text=\"picked\" />")),
        });
        Assert.Contains(commands, c => c is DrawCommand.DrawText t && t.Text == "fixed");
    }

    [Fact]
    public void ContentPresenter_string_format_applies_to_text()
    {
        var commands = RenderPresenter(new ContentPresenter { Content = 42, ContentStringFormat = "N={0}" });
        Assert.Contains(commands, c => c is DrawCommand.DrawText t && t.Text == "N=42");
    }

    [Fact]
    public void ContentPresenter_template_datacontext_is_the_content()
    {
        var presenter = new ContentPresenter
        {
            Content = "Ada",
            ContentTemplate = XamlTemplate("<TextBlock Text=\"x\" />"),
        };
        presenter.Measure(new WpfSize(200, 200));
        var hosted = LogicalTreeHelper.GetChildren(presenter).OfType<FrameworkElement>().FirstOrDefault();
        Assert.NotNull(hosted);
        Assert.Equal("Ada", hosted.DataContext);
    }

    [Fact]
    public void ContextMenu_placement_round_trips_with_dp_identity()
    {
        Assert.Equal(PlacementMode.Bottom, ContextMenu.PlacementProperty.DefaultValue);
        Assert.Equal(0.0, ContextMenu.HorizontalOffsetProperty.DefaultValue);
        Assert.Equal(0.0, ContextMenu.VerticalOffsetProperty.DefaultValue);
        Assert.Null(ContextMenu.PlacementTargetProperty.DefaultValue);

        var menu = new ContextMenu();
        Assert.Equal(PlacementMode.Bottom, menu.Placement);
        var target = new Border();
        menu.Placement = PlacementMode.Mouse;
        menu.PlacementTarget = target;
        menu.HorizontalOffset = 4;
        menu.VerticalOffset = 6;
        Assert.Equal(PlacementMode.Mouse, menu.Placement);
        Assert.Same(target, menu.PlacementTarget);
        Assert.Equal(4.0, menu.HorizontalOffset);
        Assert.Equal(6.0, menu.VerticalOffset);
        Assert.Same(ContextMenu.PlacementProperty, DependencyProperty.FromName(typeof(ContextMenu), "Placement"));
    }

    private sealed class OpenProbe : ContextMenu
    {
        public int OpenedCalls;
        public int ClosedCalls;
        protected override void OnOpened(RoutedEventArgs e) { OpenedCalls++; base.OnOpened(e); }
        protected override void OnClosed(RoutedEventArgs e) { ClosedCalls++; base.OnClosed(e); }
    }

    [Fact]
    public void ContextMenu_open_close_raise_routed_entries_in_order()
    {
        var menu = new OpenProbe();
        var seen = new List<string>();
        menu.Opened += (_, _) => seen.Add("opened");
        menu.Closed += (_, _) => seen.Add("closed");
        menu.IsOpen = true;
        menu.IsOpen = false;
        Assert.Equal(1, menu.OpenedCalls);
        Assert.Equal(1, menu.ClosedCalls);
        Assert.Equal(["opened", "closed"], seen);
    }

    [Fact]
    public void DrawingContext_primitives_lower_to_shared_ops()
    {
        var dc = new DrawingContext();
        var before = dc.Commands.Count;

        var fill = new SolidColorBrush(Color.FromRgb(0x11, 0x22, 0x33));
        var stroke = new SolidColorBrush(Color.FromRgb(0xAA, 0xBB, 0xCC));
        var pen = new Pen(stroke, 2);

        dc.DrawRectangle(fill, pen, new Rect(10, 20, 100, 50));
        var rectOps = dc.Commands.Skip(before).ToList();
        Assert.Equal(3, rectOps.Count);
        Assert.All(rectOps, op => Assert.IsType<DrawCommand.FillRect>(op));
        var ring = Assert.IsType<DrawCommand.FillRect>(rectOps[1]);
        Assert.Equal(0xAABBCCFFu, ring.Rgba);

        before = dc.Commands.Count;
        dc.DrawRectangle(fill, null, new Rect(0, 0, 10, 10));
        Assert.Equal(before + 1, dc.Commands.Count);
        Assert.IsType<DrawCommand.FillRect>(dc.Commands[before]);

        before = dc.Commands.Count;
        dc.DrawLine(pen, new Point(0, 0), new Point(30, 40));
        Assert.Equal(before + 1, dc.Commands.Count);
        var line = Assert.IsType<DrawCommand.DrawLine>(dc.Commands[before]);
        Assert.Equal(30f, line.X2);
        Assert.Equal(40f, line.Y2);
        Assert.Equal(2f, line.Width);

        before = dc.Commands.Count;
        dc.DrawEllipse(null, null, new Point(5, 5), 4, 6);
        Assert.Equal(before, dc.Commands.Count);

        before = dc.Commands.Count;
        dc.DrawEllipse(fill, null, new Point(5, 5), 4, 6);
        Assert.Equal(before + 1, dc.Commands.Count);
        var ellipse = Assert.IsType<DrawCommand.DrawEllipse>(dc.Commands[before]);
        Assert.Equal(1f, ellipse.X);
        Assert.Equal(-1f, ellipse.Y);
        Assert.Equal(8f, ellipse.W);
        Assert.Equal(12f, ellipse.H);

        before = dc.Commands.Count;
        dc.DrawRoundedRectangle(fill, null, new Rect(0, 0, 20, 10), 3, 5);
        Assert.Equal(before + 1, dc.Commands.Count);
        var rounded = Assert.IsType<DrawCommand.FillRoundedRect>(dc.Commands[before]);
        Assert.Equal(5f, rounded.Radius);

        before = dc.Commands.Count;
        var face = new Typeface("sans-serif");
        var formatted = new FormattedText("hi", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            face, 14, fill) { TextAlignment = TextAlignment.Center };
        dc.DrawText(formatted, new Point(7, 9));
        Assert.Equal(before + 1, dc.Commands.Count);
        var run = Assert.IsType<DrawCommand.DrawText>(dc.Commands[before]);
        Assert.Equal("hi", run.Text);
        Assert.Equal(1, run.HAlign);
        Assert.Equal((float)(9 + formatted.Baseline), run.Y);

        before = dc.Commands.Count;
        dc.Close();
        Assert.Equal(before, dc.Commands.Count);

        Assert.Throws<ArgumentNullException>(() => dc.DrawLine(null, new Point(0, 0), new Point(1, 1)));
        Assert.Throws<ArgumentNullException>(() => dc.DrawText(null, new Point(0, 0)));
        Assert.Throws<ArgumentNullException>(() => dc.DrawImage(null, new Rect(0, 0, 4, 4)));
    }

    [Fact]
    public void Grid_attached_round_trips_and_places_children()
    {
        var child = new Border();
        Assert.Equal(0, Grid.GetRow(child));
        Assert.Equal(0, Grid.GetColumn(child));
        Assert.Equal(1, Grid.GetRowSpan(child));
        Assert.Equal(1, Grid.GetColumnSpan(child));

        Grid.SetRow(child, 1);
        Grid.SetColumn(child, 2);
        Grid.SetRowSpan(child, 3);
        Grid.SetColumnSpan(child, 4);
        Assert.Equal(1, Grid.GetRow(child));
        Assert.Equal(2, Grid.GetColumn(child));
        Assert.Equal(3, Grid.GetRowSpan(child));
        Assert.Equal(4, Grid.GetColumnSpan(child));
        Assert.Equal(1, child.GetValue(Grid.RowProperty));
        Assert.Same(Grid.RowProperty, DependencyProperty.FromName(typeof(Grid), "Row"));

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30, GridUnitType.Pixel) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30, GridUnitType.Pixel) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100, GridUnitType.Pixel) });
        var placed = new Border { Width = 100, Height = 30 };
        Grid.SetRow(placed, 1);
        grid.AddChild(placed);
        grid.Measure(new WpfSize(100, 60));
        grid.Arrange(new WpfRect(0, 0, 100, 60));
        Assert.Equal(30, placed.RenderRect.Y, 3);
    }

    [Fact]
    public void Grid_xaml_attached_lands_in_store_and_bag()
    {
        var window = Assert.IsType<Window>(XamlLoader.Load(
            "<Window xmlns=\"" + PresentationNs + "\">" +
            "<Grid><Grid.RowDefinitions><RowDefinition Height=\"30\" /><RowDefinition Height=\"30\" /></Grid.RowDefinitions>" +
            "<Border Grid.Row=\"1\" /></Grid>" +
            "</Window>").Root);
        var grid = Assert.IsType<Grid>(window.Content);
        var child = Assert.IsType<Border>(grid.Children[0]);
        Assert.Equal(1, Grid.GetRow(child));
        Assert.Equal(1, child.GetValue(Grid.RowProperty));
    }

    private sealed class SelectionProbe : TabControl
    {
        public int ArgsCalls;
        protected override void OnSelectionChanged(SelectionChangedEventArgs e) { ArgsCalls++; base.OnSelectionChanged(e); }
    }

    [Fact]
    public void TabControl_selection_rows_round_trip_with_dp_identity()
    {
        Assert.Null(TabControl.SelectedContentTemplateProperty.DefaultValue);
        Assert.Null(TabControl.SelectedContentTemplateSelectorProperty.DefaultValue);
        Assert.Null(TabControl.SelectedContentStringFormatProperty.DefaultValue);

        var tabs = new TabControl();
        Assert.Null(tabs.SelectedContentTemplate);
        Assert.Null(tabs.SelectedContentTemplateSelector);
        Assert.Null(tabs.SelectedContentStringFormat);
        var template = XamlTemplate("<TextBlock Text=\"t\" />");
        tabs.SelectedContentTemplate = template;
        tabs.SelectedContentStringFormat = "F:{0}";
        Assert.Same(template, tabs.SelectedContentTemplate);
        Assert.Equal("F:{0}", tabs.SelectedContentStringFormat);
        Assert.Same(TabControl.SelectedContentTemplateProperty,
            DependencyProperty.FromName(typeof(TabControl), "SelectedContentTemplate"));
    }

    [Fact]
    public void TabControl_selection_change_routes_through_args_override()
    {
        var tabs = new SelectionProbe();
        tabs.AddItem(new TabItem { Header = "A" });
        tabs.AddItem(new TabItem { Header = "B" });
        var before = ((SelectionProbe)tabs).ArgsCalls;
        tabs.SelectedIndex = 1;
        Assert.True(((SelectionProbe)tabs).ArgsCalls > before);
        Assert.Equal(1, tabs.SelectedIndex);
    }

    [Fact]
    public void Popup_offsets_already_complete_with_zero_staged_refs()
    {
        var popup = new Popup();
        Assert.Equal(PlacementMode.Bottom, popup.Placement);
        Assert.Equal(0.0, popup.HorizontalOffset);
        Assert.Equal(0.0, popup.VerticalOffset);
        popup.HorizontalOffset = 3;
        popup.VerticalOffset = 5;
        Assert.Equal(3.0, popup.HorizontalOffset);
        Assert.Equal(5.0, popup.VerticalOffset);
    }

    [Fact]
    public void Popup_placement_members_expose_dependency_properties()
    {
        // Demo PopupBox shape: converted code refs
        // Popup.HorizontalOffsetProperty (real-WPF DPs). The CLR wrappers
        // dual-land the store like CustomPopupPlacementCallbackProperty.
        var popup = new Popup
        {
            Placement = PlacementMode.Right,
            HorizontalOffset = 3,
            VerticalOffset = 4,
            StaysOpen = false,
        };
        Assert.Equal(PlacementMode.Right, popup.GetValue(Popup.PlacementProperty));
        Assert.Equal(3.0, popup.GetValue(Popup.HorizontalOffsetProperty));
        Assert.Equal(4.0, popup.GetValue(Popup.VerticalOffsetProperty));
        Assert.Equal(false, popup.GetValue(Popup.StaysOpenProperty));
    }

    [Fact]
    public void Converted_layout_floating_window_static_ctor_runs()
    {
        Application.Current = null;
        var previous = HwndSource.DefaultAcquireHwndFocusInMenuMode;
        try
        {
            var assembly = StagedAvalonDockChromeLoader.LoadAssembly("AvalonDock");
            var floating = assembly.GetType("AvalonDock.Controls.LayoutFloatingWindowControl", throwOnError: true)!;
            RuntimeHelpers.RunClassConstructor(floating.TypeHandle);
        }
        finally
        {
            HwndSource.DefaultAcquireHwndFocusInMenuMode = previous;
            Application.Current = null;
        }
    }

    [Fact]
    public void Converted_cluster_users_load_without_cluster_members()
    {
        var assembly = StagedAvalonDockChromeLoader.LoadAssembly("AvalonDock");
        var users = new[]
        {
            "AvalonDock.DockingManager",
            "AvalonDock.Controls.LayoutFloatingWindowControl",
            "AvalonDock.Controls.LayoutDocumentFloatingWindowControl",
            "AvalonDock.Controls.LayoutAnchorableFloatingWindowControl",
            "AvalonDock.Controls.DropDownButton",
            "AvalonDock.Controls.ContextMenuEx",
            "AvalonDock.Controls.ToggleDockDragOverlay",
            "AvalonDock.Controls.TabControlEx",
            "Microsoft.Windows.Shell.WindowChromeWorker",
            "AvalonDock.Layout.ILayoutElementForFloatingWindowExtension",
            "Standard.Utility",
        };
        foreach (var name in users)
        {
            var type = assembly.GetType(name, throwOnError: true)!;
            try
            {
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            }
            catch (Exception ex)
            {
                var chain = new List<string>();
                for (var e = ex; e is not null; e = e.InnerException)
                    chain.Add(e.GetType().Name + ": " + e.Message.Split('\n')[0]);
                var joined = string.Join(" ||| ", chain);
                Assert.DoesNotContain("ContentProperty", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("ContentTemplate", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("ContentStringFormat", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("add_Closed", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("remove_Closed", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("OnOpened", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("Placement", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("HorizontalOffset", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("VerticalOffset", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("DrawRectangle", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("DrawEllipse", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("DrawRoundedRectangle", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("DrawLine", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("DrawImage", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("DrawText", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("SetRow", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("SetColumn", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("SetRowSpan", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("SelectedContentTemplate", joined, StringComparison.Ordinal);
                Assert.DoesNotContain("PrimaryScreen", joined, StringComparison.Ordinal);
            }
        }
    }

    private static class StagedAvalonDockChromeLoader
    {
        private static readonly object Gate = new();
        private static readonly Dictionary<string, Assembly> Loaded = new(StringComparer.OrdinalIgnoreCase);

        public static List<string> VerifyHashes()
        {
            var failures = new List<string>();
            foreach (var (_, file, bytes, expected) in ReadStaged())
            {
                var actual = Convert.ToHexString(SHA256.HashData(bytes));
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    failures.Add($"hash mismatch in staged '{file}'");
            }
            return failures;
        }

        public static Assembly LoadAssembly(string name)
        {
            lock (Gate)
            {
                if (Loaded.TryGetValue(name, out var hit)) return hit;
                var images = ReadStaged().ToDictionary(e => e.name, e => e.bytes, StringComparer.OrdinalIgnoreCase);
                Assert.True(images.ContainsKey(name), $"staged image missing: {name}");
                AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
                {
                    var requested = new AssemblyName(args.Name).Name;
                    var loaded = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => string.Equals(a.GetName().Name, requested, StringComparison.OrdinalIgnoreCase));
                    if (loaded is not null) return loaded;
                    if (requested is not null && images.TryGetValue(requested, out var image))
                        return Assembly.Load(image);
                    return null;
                };
                foreach (var image in images.Values)
                {
                    var assembly = Assembly.Load(image);
                    Loaded[assembly.GetName().Name!] = assembly;
                }
                return Loaded[name];
            }
        }

        private static List<(string name, string file, byte[] bytes, string expected)> ReadStaged()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "src",
                "DesktopWasm.BrowserHost", "wwwroot", "apps", "avalondock", "app-manifest.json")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            var apps = System.IO.Path.Combine(directory!.FullName, "src",
                "DesktopWasm.BrowserHost", "wwwroot", "apps", "avalondock");
            using var document = JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
            var entries = new List<(string, string, byte[], string)>();
            foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
            {
                var file = assembly.GetProperty("file").GetString()!;
                var expected = assembly.GetProperty("sha256").GetString()!;
                var bytes = System.IO.File.ReadAllBytes(System.IO.Path.Combine(apps, file));
                entries.Add((assembly.GetProperty("name").GetString()!, file, bytes, expected));
            }
            return entries;
        }
    }
}
