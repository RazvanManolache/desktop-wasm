// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-111 HandyControl converted panel layouts.
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched).
///
/// Advance, all reproduced against live staged bins via the engine-native
/// staged-bin boot oracle (status gate bypassed in-probe only) then pinned
/// by the new staged-oracle set: the staged HoneycombPanelDemo ListBox rows
/// parent into the converted HoneycombPanel and arrange in honeycomb rings
/// through converted MeasureOverride/ArrangeOverride (pre-fix they stacked
/// full-width vertically and the converted layout never ran), and the staged
/// ElementGroupDemo/ButtonGroup groups parent into their upstream
/// style-mapped panels (UniformGrid/StackPanel per Orientation/Layout) with
/// converted children painting through the visual-tree backstop (pre-fix
/// converted ElementGroup/ButtonGroup OnRender painted nothing, so group
/// children were invisible as well as stacked).
///
/// Remaining blockers pinned by the same set: the 2 absent binaries (no
/// browser path), the unchanged line-17 SkinDefault BAML-only stop, engine
/// (non-converted) panels in legacy ListBox ItemsPanel keeping the legacy
/// stack, grouped legacy lists ignoring the custom host, and margin folding
/// inside converted arrange keeping the engine slot semantic.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice111HandyPanelsTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private readonly Application? _previous = Application.Current;

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    [Theory]
    [InlineData("Horizontal", "Stack", "StackHorizontal")]
    [InlineData("Vertical", "Stack", "StackVertical")]
    [InlineData("Horizontal", "Uniform", "UniformRows")]
    [InlineData("Vertical", "Uniform", "UniformColumns")]
    [InlineData("horizontal", "stack", "StackHorizontal")]
    [InlineData("VERTICAL", "UNIFORM", "UniformColumns")]
    public void Group_panel_kind_maps_orientation_and_layout(string orientation, string layout, string expected)
    {
        Assert.Equal(expected, Slice111HandyPanels.MapGroupPanelKind(orientation, layout));
    }

    [Fact]
    public void Default_group_panel_is_null_for_engine_types()
    {
        Assert.Null(Slice111HandyPanels.DefaultGroupItemsPanel(new TestItemsControl()));
        Assert.False(Slice111HandyPanels.IsConvertedGroupPanel(typeof(ItemsControl)));
    }

    private sealed class TestItemsControl : ItemsControl
    {
    }

    [Fact]
    public void Engine_items_control_keeps_null_default_panel()
    {
        Assert.Null(new TestItemsControl().ItemsPanel);
    }

    [Fact]
    public void PanelElement_knob_round_trips_and_rejects_null_elements()
    {
        var element = new Border();
        Assert.Null(PanelElement.GetFluidMoveBehavior(element));
        var marker = new object();
        PanelElement.SetFluidMoveBehavior(element, marker);
        Assert.Same(marker, PanelElement.GetFluidMoveBehavior(element));
        Assert.Throws<ArgumentNullException>(() => PanelElement.SetFluidMoveBehavior(null!, marker));
        Assert.Throws<ArgumentNullException>(() => PanelElement.GetFluidMoveBehavior(null!));
    }

    [Fact]
    public void BehaviorXY200_resolves_to_the_shared_inert_marker()
    {
        Assert.True(Slice111HandyPanels.TryResolveBehaviorXY200("BehaviorXY200", out var value));
        Assert.Same(Slice111HandyPanels.BehaviorXY200Marker, value);
        Assert.False(Slice111HandyPanels.TryResolveBehaviorXY200("NoSuchBehavior111", out _));
        Assert.False(Slice111HandyPanels.TryResolveBehaviorXY200(42, out _));
    }

    [Fact]
    public void BehaviorXY200_resolves_at_application_scope()
    {
        Application.Current = new Application();
        Assert.True(Application.Current.TryFindResource("BehaviorXY200", out var value));
        Assert.Same(Slice111HandyPanels.BehaviorXY200Marker, value);
        Assert.False(Application.Current.TryFindResource("NoSuchBehavior111", out _));
    }

    [Fact]
    public void PanelElement_literal_attribute_lands_retained()
    {
        var root = XamlLoader.Load(
            "<Border xmlns=\"" + PresentationNs + "\" PanelElement.FluidMoveBehavior=\"literal\" />").Root;
        var border = Assert.IsType<Border>(root);
        Assert.Equal("literal", PanelElement.GetFluidMoveBehavior(border));
    }

    [Fact]
    public void Panel_children_carry_the_non_generic_enumerator_converted_loops_bind()
    {
        var panel = new StackPanel();
        var child = new Border();
        panel.AddChild(child);

        // The converted callvirt spells System.Collections.IEnumerator
        // UIElementCollection.GetEnumerator() (the real-WPF shape; C# cannot
        // overload by return type, so the generic enumerator is explicit).
        // Resolve it exactly like the runtime does and run it: the loop
        // converted HoneycombPanel.ArrangeOverride JITs must enumerate, not
        // throw MissingMethodException.
        var method = typeof(UIElementCollection).GetMethods()
            .Single(m => m.Name == "GetEnumerator"
                && m.GetParameters().Length == 0
                && m.ReturnType == typeof(System.Collections.IEnumerator));
        var enumerator = (System.Collections.IEnumerator)method.Invoke(panel.Children, null)!;
        Assert.True(enumerator.MoveNext());
        Assert.Same(child, enumerator.Current);
        Assert.False(enumerator.MoveNext());
    }

    [Fact]
    public void Engine_uniform_grid_host_keeps_the_legacy_stack()
    {
        var legacy = new ListBox { Width = 400 };
        legacy.ItemsSource = new[] { "a", "b" };
        var legacyFrame = WasmMilCore.RenderHeadless(legacy, new WpfSize(400, 600));
        Assert.NotEmpty(legacyFrame);

        var hosted = new ListBox { Width = 400 };
        hosted.ItemsPanel = new ItemsPanelTemplate("<UniformGrid Rows=\"1\" />");
        hosted.ItemsSource = new[] { "a", "b" };
        var hostedFrame = WasmMilCore.RenderHeadless(hosted, new WpfSize(400, 600));

        // Engine (non-converted) panels keep the exact legacy stack: both rows
        // full-width at x=1 behind the default 1px frame, one below the other.
        var rows = hosted.Items;
        Assert.Equal(2, rows.Count);
        Assert.Equal(rows[0].RenderRect.X, rows[1].RenderRect.X, precision: 6);
        Assert.Equal(1.0, rows[0].RenderRect.X, precision: 6);
        Assert.True(rows[1].RenderRect.Y > rows[0].RenderRect.Y);
        Assert.Equal(legacy.Items[0].RenderRect.Height, rows[0].RenderRect.Height, precision: 6);
        Assert.NotEmpty(hostedFrame);
    }

    [Fact]
    public void Grouped_rows_resolve_their_owner_up_the_chain()
    {
        var examples = new[]
        {
            new GroupProbe("A", "a1"),
            new GroupProbe("A", "a2"),
            new GroupProbe("B", "b1"),
        };
        var view = CollectionViewSource.GetDefaultView(examples)!;
        view.GroupDescriptions.Add(new PropertyGroupDescription("Category"));

        var list = new ListBox();
        list.GroupStyle.Add(new GroupStyle());
        list.ItemsSource = view;

        Assert.Equal(3, list.Items.Count);
        // Pre-fix (Parent as ListBox) every grouped row read null here because
        // grouped rows parent into GroupItem containers; the walk-up finds the
        // owning list through the container.
        list.SelectedIndex = 0;
        Assert.True(list.Items[0].IsSelected);
        Assert.False(list.Items[1].IsSelected);
        list.Items[2].IsSelected = true;
        Assert.Equal(2, list.SelectedIndex);
        list.Items[2].IsSelected = false;
        Assert.Equal(-1, list.SelectedIndex);
    }

    private sealed record GroupProbe(string Category, string Name)
    {
        public override string ToString() => Name;
    }

    [Fact]
    public void Staged_manifest_keeps_six_staged_two_absent()
    {
        var entry = LoadStagedHandyControlEntry();
        var apps = StagedHandyAppsDirectory();
        using var document = JsonDocument.Parse(
            File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
        var root = document.RootElement;
        Assert.Equal("conversion-failed", root.GetProperty("status").GetString());
        var assemblies = root.GetProperty("assemblies").EnumerateArray().ToList();
        Assert.Equal(6, assemblies.Count);
        foreach (var assembly in assemblies)
        {
            var file = assembly.GetProperty("file").GetString()!;
            var expected = assembly.GetProperty("sha256").GetString()!;
            var actual = Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(System.IO.Path.Combine(apps, file))));
            Assert.True(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                $"hash mismatch in staged '{file}': {actual} != {expected}");
        }
        var gaps = root.GetProperty("gaps").EnumerateArray().ToList();
        Assert.Equal(2, gaps.Count);
        var details = gaps.Select(g => g.GetProperty("detail").GetString()!).ToList();
        Assert.Contains(details, d => d.Contains("System.Security.Permissions"));
        Assert.Contains(details, d => d.Contains("System.Windows.Extensions"));
        foreach (var name in new[]
        {
            "FluidMoveBehavior", "BehaviorXY200", "HoneycombPanel",
            "ElementGroup", "ButtonGroup", "UniformGrid",
        })
            Assert.DoesNotContain(details, d => d.Contains(name));
        _ = entry;
    }

    [Fact]
    public void Staged_entry_boot_advances_past_skin_default_to_theme_stop()
    {
        // Slice-120 re-pin: entries #102 (implicit Col style, short-form
        // Focusable setter) and #103 (implicit ContentControl style, same
        // shape) now resolve too; boot advances past FloatingBlock (#105) and
        // GridSplitter (#106) past Notification (#107) past the SnapRect4ScreenshotStyle Trigger list and the TargetArea4ScreenshotStyle entry past the implicit SimpleItemsControl/Sprite styles, the nine transition Storyboards (keyidx 113-121) and the implicit TransitioningContentControl style through the keyed RepeatButton styles (keyidx 123-126) through the keyed 'ToggleBlockBaseStyle' style (keyidx 127) to the 'GroupBoxBaseTopTemplate' entry at BAML+0x1835D
        // (see Slice122BamlAlignmentTests).
        var entry = LoadStagedHandyControlEntry();
        var error = Assert.Throws<InvalidOperationException>(() => WpfAppBoot.Boot(entry));
        Assert.Contains("startup failed in entry point", error.Message);
        var inner = Assert.IsType<XamlLoadException>(error.InnerException);
        Assert.DoesNotContain("SkinDefault", inner.Message);
        Assert.DoesNotContain("PathBaseStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x166FF", inner.Message);
        Assert.DoesNotContain("BAML+0x167B8", inner.Message);
        Assert.DoesNotContain("x:Static", inner.Message);
        Assert.DoesNotContain("VerticalScrollBarButtonHeightKey", inner.Message);
        Assert.DoesNotContain("BAML+0x167C8", inner.Message);
        Assert.DoesNotContain("BehaviorXY200", inner.Message);
        Assert.DoesNotContain("FluidMoveBehavior", inner.Message);
        Assert.DoesNotContain("BAML+0x168D2", inner.Message);
        Assert.DoesNotContain("BlendEffectBox", inner.Message);
        Assert.DoesNotContain("BAML+0x16926", inner.Message);
        Assert.DoesNotContain("BorderCircular", inner.Message);
        Assert.Contains("HandyControl;component/Themes/Theme.xaml", inner.Message);
        Assert.Contains("line 23", inner.Message);
        Assert.DoesNotContain("BAML+0x16954", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.Col", inner.Message);
        Assert.DoesNotContain("well-known member id -132", inner.Message);
        Assert.DoesNotContain("BAML+0x16C96", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.Notification", inner.Message);
        Assert.DoesNotContain("setter property owner Window", inner.Message);
        Assert.DoesNotContain("BAML+0x16E7C", inner.Message);
        Assert.DoesNotContain("RectangleCircular", inner.Message);
        Assert.DoesNotContain("SnapRect4ScreenshotStyle", inner.Message);
        Assert.DoesNotContain("TargetArea4ScreenshotStyle", inner.Message);
        Assert.DoesNotContain("holds a Trigger list", inner.Message);
        Assert.DoesNotContain("BAML+0x1760B", inner.Message);
        Assert.DoesNotContain("BAML+0x17C06", inner.Message);
        Assert.DoesNotContain("Right2LeftTransition", inner.Message);
        Assert.DoesNotContain("SliderRepeatButtonHorizontalStyle", inner.Message);
        Assert.DoesNotContain("SliderRepeatButtonVerticalStyle", inner.Message);
        Assert.DoesNotContain("CompareSliderRepeatButtonTransparent", inner.Message);
        Assert.DoesNotContain("ScrollBarBaseRepeatButton", inner.Message);
        Assert.DoesNotContain("BAML+0x17F4C", inner.Message);
        Assert.DoesNotContain("ToggleBlockBaseStyle", inner.Message);
        Assert.DoesNotContain("well-known member id -38", inner.Message);
        Assert.DoesNotContain("BAML+0x1835D", inner.Message);
        Assert.DoesNotContain("GroupBoxBaseTopTemplate", inner.Message);
        Assert.DoesNotContain("BAML+0x1859F", inner.Message);
        Assert.DoesNotContain("GroupBoxBaseLeftTemplate", inner.Message);
        Assert.DoesNotContain("holds ControlTemplate", inner.Message);
        Assert.DoesNotContain("BAML+0x1888C", inner.Message);
        Assert.DoesNotContain("GroupBoxTabTopTemplate", inner.Message);
        Assert.DoesNotContain("GroupBoxTabLeftTemplate", inner.Message);
        Assert.DoesNotContain("template Binding holds record 0x38", inner.Message);
        Assert.DoesNotContain("BAML+0x18D3F", inner.Message);
        Assert.DoesNotContain("ButtonGroupHorizontalUniformItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("holds well-known type id -330", inner.Message);
        Assert.DoesNotContain("BAML+0x18DBD", inner.Message);
        Assert.DoesNotContain("ProgressBarIndicatorAnimatedFill", inner.Message);
        Assert.DoesNotContain("holds well-known type id -358", inner.Message);
        Assert.DoesNotContain("BAML+0x18E5D", inner.Message);
        Assert.DoesNotContain("BAML+0x19029", inner.Message);
        Assert.DoesNotContain("CircleProgressBarTemplate", inner.Message);
        Assert.DoesNotContain("VirtualizingStackHorizontalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("WrapHorizontalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("WrapVerticalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("DockItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("ResizeGripperForeground", inner.Message);
        Assert.DoesNotContain("SineEaseOut", inner.Message);
        Assert.DoesNotContain("BAML+0x19150", inner.Message);
        Assert.DoesNotContain("BadgeDotTemplate", inner.Message);
        Assert.DoesNotContain("element ContentPresenter on the template root panel", inner.Message);
        Assert.DoesNotContain("root panels take ContentPresenter, Border, converted Shape and TextBlock children", inner.Message);
        Assert.DoesNotContain("BAML+0x1927D", inner.Message);
        Assert.DoesNotContain("ColorPickerSliderThumbTemplate", inner.Message);
        Assert.DoesNotContain("BAML+0x192B9", inner.Message);
        Assert.DoesNotContain("ColorPickerRainbowBrush", inner.Message);
        Assert.DoesNotContain("has no Offset", inner.Message);
        Assert.DoesNotContain("BAML+0x19383", inner.Message);
        Assert.DoesNotContain("ColorPickerOpacityBrush", inner.Message);
        Assert.DoesNotContain("well-known type id -180", inner.Message);
        Assert.DoesNotContain("BAML+0x19497", inner.Message);
        Assert.DoesNotContain("CoverFlowBaseStyle", inner.Message);
        Assert.DoesNotContain("well-known type id -729", inner.Message);
        Assert.DoesNotContain("BAML+0x195FD", inner.Message);
        Assert.DoesNotContain("GridViewColumnHeaderGripper", inner.Message);
        Assert.DoesNotContain("well-known type id -66", inner.Message);
        Assert.DoesNotContain("Canvas.Right", inner.Message);
        Assert.DoesNotContain("BAML+0x196C6", inner.Message);
        Assert.DoesNotContain("'Padding' is not a dependency property of Border", inner.Message);
        Assert.DoesNotContain("SortByCategoryDrawingBrush", inner.Message);
        Assert.DoesNotContain("BAML+0x196F6", inner.Message);
        Assert.DoesNotContain("SortByNameDrawingBrush", inner.Message);
        Assert.DoesNotContain("TabItemMenu", inner.Message);
        Assert.DoesNotContain("BAML+0x19D99", inner.Message);
        Assert.DoesNotContain("BAML+0x19DCE", inner.Message);
        Assert.DoesNotContain("well-known type id -105", inner.Message);
        Assert.DoesNotContain("Lang.Close", inner.Message);
        Assert.DoesNotContain("throws reading the static property", inner.Message);
        Assert.DoesNotContain("XmlnsDefinitionAttribute", inner.Message);
        Assert.DoesNotContain("ToolBarTrayBaseStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x19E4F", inner.Message);
        Assert.DoesNotContain("well-known type id -672", inner.Message);
        Assert.DoesNotContain("TreeArrow", inner.Message);
        Assert.DoesNotContain("BAML+0x19E7B", inner.Message);
        Assert.DoesNotContain("well-known type id -446", inner.Message);
        Assert.DoesNotContain("RowHeaderGripperStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x1A034", inner.Message);
        Assert.DoesNotContain("'Cursor' is not a dependency property of FrameworkElement", inner.Message);
        Assert.DoesNotContain("DataGridComboBoxColumnStyle", inner.Message);
        Assert.DoesNotContain("DataGridTextColumnStyle", inner.Message);        
        Assert.DoesNotContain("CarouselItemBaseStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x1A113", inner.Message);
        Assert.DoesNotContain("BAML+0x1A186", inner.Message);        
        Assert.DoesNotContain("BAML+0x1A21B", inner.Message);
        Assert.DoesNotContain("well-known type id -92", inner.Message);
        Assert.DoesNotContain("targets TextBlock", inner.Message);        
        Assert.DoesNotContain("template property id -21", inner.Message);
        Assert.DoesNotContain("GrowlWindow", inner.Message);
        Assert.DoesNotContain("BAML+0x1A2CA", inner.Message);
        Assert.DoesNotContain("well-known member id -51", inner.Message);
        Assert.DoesNotContain("ListBoxItemCustom", inner.Message);
        Assert.DoesNotContain("BAML+0x1A368", inner.Message);
        Assert.DoesNotContain("well-known type id -374", inner.Message);
        Assert.DoesNotContain("BAML+0x1A92D", inner.Message);
        Assert.DoesNotContain("well-known member id -81", inner.Message);
        Assert.DoesNotContain("RibbonTab", inner.Message);
        Assert.DoesNotContain("BAML+0x1A990", inner.Message);
        Assert.DoesNotContain("template root panel property 'Visibility' with converter -195", inner.Message);
        Assert.DoesNotContain("BAML+0x1A9DD", inner.Message);
        Assert.DoesNotContain("element well-known type id -550 on the template root panel", inner.Message);
        Assert.DoesNotContain("BAML+0x1AE91", inner.Message);
        Assert.DoesNotContain("'EffectShadow1'", inner.Message);
        Assert.DoesNotContain("record 0x34 (converted-element values", inner.Message);
        Assert.DoesNotContain("BAML+0x1B001", inner.Message);
        Assert.DoesNotContain("BAML+0x1B22E", inner.Message);
        Assert.DoesNotContain("'SearchPathStyle'", inner.Message);
        Assert.DoesNotContain("'RightPathStyle'", inner.Message);
        Assert.DoesNotContain("record 0x38 in a Style", inner.Message);
        Assert.DoesNotContain("'RenderTransformOrigin' is not a dependency property of UIElement", inner.Message);
        Assert.DoesNotContain("BAML+0x1B260", inner.Message);
        Assert.DoesNotContain("well-known member id -134", inner.Message);
        Assert.Contains("'BorderRegion'", inner.Message);
        Assert.Contains("BAML+0x1B2F9", inner.Message);
        Assert.Contains("setter property owner Border in style entry 'BorderRegion'", inner.Message);
        Assert.DoesNotContain("BAML+0x1AA35", inner.Message);
        Assert.DoesNotContain("'PrimaryBrush'", inner.Message);
        Assert.DoesNotContain("record 0x34 in a LinearGradientBrush", inner.Message);
        Assert.DoesNotContain("BAML+0x1AA1C", inner.Message);
        Assert.DoesNotContain("LightPrimaryBrush", inner.Message);
        Assert.DoesNotContain("well-known type id -578", inner.Message);
        Assert.DoesNotContain("on setter 'Template'", inner.Message);
        Assert.DoesNotContain("well-known type id -658", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.SimplePanel", inner.Message);
        Assert.DoesNotContain("VirtualizingStackPanel", inner.Message);
        Assert.DoesNotContain("templates take a ContentPresenter", inner.Message);
        Assert.DoesNotContain("items panels take a UniformGrid/StackPanel/VirtualizingStackPanel/WrapPanel/DockPanel visual", inner.Message);

        Assert.DoesNotContain("well-known type id -608", inner.Message);
        Assert.DoesNotContain("well-known type id -522", inner.Message);
        Assert.DoesNotContain("BAML+0x1744C", inner.Message);
        Assert.DoesNotContain("element Border on the template", inner.Message);
        Assert.DoesNotContain("SimpleItemsControl", inner.Message);
        Assert.DoesNotContain("BAML+0x17483", inner.Message);
        Assert.DoesNotContain("Sprite", inner.Message);
        Assert.DoesNotContain("IsDragElement", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.SimpleItemsControl", inner.Message);
    }

    [Fact]
    public void Staged_honeycomb_rows_run_converted_layout()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var type = entry.GetType("HandyControlDemo.UserControl.HoneycombPanelDemo", throwOnError: true)!;
        var view = Assert.IsAssignableFrom<FrameworkElement>(Activator.CreateInstance(type));
        var frame = WasmMilCore.RenderHeadless(view, new WpfSize(800, 600));

        var list = Assert.Single(Descendants(view).OfType<ListBox>());
        Assert.NotNull(list.ItemsPanel);
        var template = list.ItemsPanel!;
        Assert.Same(template, list.ItemsPanel);

        var rows = list.Items;
        Assert.Equal(10, rows.Count);

        // Pre-fix the rows stacked full-width vertically (one shared x, widths
        // equal to the inner width). The converted HoneycombPanel centers
        // ring 0 and spirals outward, so rows land on several columns and
        // several bands with the first row strictly interior (legacy row 0
        // sits at the top-left corner of the stack).
        var xs = rows.Select(r => Math.Round(r.RenderRect.X, 3)).Distinct().ToList();
        var ys = rows.Select(r => Math.Round(r.RenderRect.Y, 3)).Distinct().ToList();
        Assert.True(xs.Count >= 3, $"honeycomb needs 3+ columns, have {xs.Count}: {string.Join(",", xs)}");
        Assert.True(ys.Count >= 3, $"honeycomb needs 3+ bands, have {ys.Count}: {string.Join(",", ys)}");
        var row0 = rows[0].RenderRect;
        var allX = rows.Select(r => r.RenderRect.X).ToList();
        var allY = rows.Select(r => r.RenderRect.Y).ToList();
        Assert.True(row0.X > allX.Min() && row0.X < allX.Max(),
            $"ring-0 row not interior in x: {row0.X} in {allX.Min()}..{allX.Max()}");
        Assert.True(row0.Y > allY.Min() && row0.Y < allY.Max(),
            $"ring-0 row not interior in y: {row0.Y} in {allY.Min()}..{allY.Max()}");

        // The host is the converted panel running its own layout, carrying
        // the retained FluidMoveBehavior marker resolved at application scope.
        var host = list.LogicalChildren.OfType<Panel>().Single();
        Assert.Equal("HandyControl.Controls.HoneycombPanel", host.GetType().FullName);
        Assert.NotSame(list, host);
        Assert.True(Application.Current!.TryFindResource("BehaviorXY200", out var marker));
        Assert.Same(Slice111HandyPanels.BehaviorXY200Marker, marker);
        Assert.Same(marker, PanelElement.GetFluidMoveBehavior(host));
        Assert.NotEmpty(frame);
    }

    [Fact]
    public void Staged_element_groups_use_the_upstream_panel_mapping()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var type = entry.GetType("HandyControlDemo.UserControl.ElementGroupDemo", throwOnError: true)!;
        var view = Assert.IsAssignableFrom<FrameworkElement>(Activator.CreateInstance(type));
        WasmMilCore.RenderHeadless(view, new WpfSize(800, 600));

        var groups = Descendants(view)
            .Where(n => n.GetType().FullName == "HandyControl.Controls.ElementGroup")
            .Cast<ItemsControl>()
            .ToList();
        Assert.Equal(4, groups.Count);

        // Group 1 (Horizontal + Stack) and group 3 (same): side-by-side rows
        // sharing one Y band with strictly increasing X.
        foreach (var group in new[] { groups[0], groups[2] })
        {
            var host = Assert.IsType<StackPanel>(Assert.Single(group.LogicalChildren.OfType<Panel>()));
            Assert.Equal(Orientation.Horizontal, host.Orientation);
            var rows = group.LogicalChildren.OfType<FrameworkElement>().SelectMany(RowVisuals).ToList();
            Assert.True(rows.Count >= 4, $"expected stacked children, have {rows.Count}");
            for (var i = 1; i < rows.Count; i++)
                Assert.True(rows[i].RenderRect.X > rows[i - 1].RenderRect.X,
                    $"row {i} x={rows[i].RenderRect.X} not right of row {i - 1} x={rows[i - 1].RenderRect.X}");
            Assert.True(rows.Max(r => r.RenderRect.Y) - rows.Min(r => r.RenderRect.Y) < 5,
                "horizontal-stack rows leave their Y band");
        }

        // Group 2 (Horizontal + Uniform default): one UniformGrid row of four
        // equal cells.
        {
            var host = Assert.IsType<UniformGrid>(Assert.Single(groups[1].LogicalChildren.OfType<Panel>()));
            Assert.Equal(1, host.Rows);
            var cells = groups[1].LogicalChildren.OfType<FrameworkElement>().SelectMany(RowVisuals).ToList();
            Assert.True(cells.Count >= 4, $"expected uniform children, have {cells.Count}");
            var widths = cells.Select(c => c.RenderRect.Width).ToList();
            Assert.True(widths.Max() - widths.Min() < 2,
                $"uniform cells differ: {string.Join(",", widths.Select(w => w.ToString("F1")))}");
        }

        // Group 4 keeps its explicit Grid ItemsPanel (local beats the style
        // mapping, like WPF): two columns, TextBox left and Button right.
        {
            var host = Assert.IsType<Grid>(Assert.Single(groups[3].LogicalChildren.OfType<Panel>()));
            Assert.Equal(2, host.ColumnDefinitions.Count);
        }
    }

    private static IEnumerable<FrameworkElement> RowVisuals(FrameworkElement node)
    {
        if (node is Panel panel)
        {
            foreach (var child in panel.LogicalChildren.OfType<FrameworkElement>())
                foreach (var inner in RowVisuals(child))
                    yield return inner;
            yield break;
        }
        yield return node;
    }

    [Fact]
    public void Staged_button_group_uses_uniform_rows()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var type = entry.GetType("HandyControlDemo.UserControl.HoneycombPanelDemo", throwOnError: true)!;
        var view = Assert.IsAssignableFrom<FrameworkElement>(Activator.CreateInstance(type));
        WasmMilCore.RenderHeadless(view, new WpfSize(800, 600));

        var group = Assert.Single(Descendants(view)
            .Where(n => n.GetType().FullName == "HandyControl.Controls.ButtonGroup")
            .Cast<ItemsControl>());
        var host = Assert.IsType<UniformGrid>(Assert.Single(group.LogicalChildren.OfType<Panel>()));
        Assert.Equal(1, host.Rows);
        var buttons = group.LogicalChildren.OfType<FrameworkElement>().SelectMany(RowVisuals).ToList();
        Assert.Equal(2, buttons.Count);
        Assert.True(buttons[1].RenderRect.X > buttons[0].RenderRect.X,
            $"buttons not side by side: {buttons[0].RenderRect.X} vs {buttons[1].RenderRect.X}");
    }

    [Fact]
    public void Staged_group_children_paint_through_the_backstop()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var type = entry.GetType("HandyControlDemo.UserControl.ElementGroupDemo", throwOnError: true)!;
        var view = Assert.IsAssignableFrom<FrameworkElement>(Activator.CreateInstance(type));
        var frame = WasmMilCore.RenderHeadless(view, new WpfSize(800, 600));

        // Pre-fix converted ElementGroup.OnRender painted nothing (corner
        // radii only), so group children left zero text ink. The RenderVisible
        // backstop replays the visual-tree composition real WPF performs.
        var texts = frame.OfType<DrawCommand.DrawText>().ToList();
        Assert.NotEmpty(texts);
    }

    private static string StagedHandyAppsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(
            directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol", "app-manifest.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return System.IO.Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol");
    }

    private static readonly object StagedGate = new();
    private static Assembly? _stagedEntry;

    private static Assembly LoadStagedHandyControlEntry()
    {
        lock (StagedGate)
        {
            var hit = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "HandyControlDemo", StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                VerifyStagedHashes();
                XamlLoader.CustomElementSource = hit;
                return hit;
            }
            if (_stagedEntry is not null)
            {
                XamlLoader.CustomElementSource = _stagedEntry;
                return _stagedEntry;
            }
            var apps = StagedHandyAppsDirectory();
            using var document = JsonDocument.Parse(
                File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
            var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
            {
                var file = assembly.GetProperty("file").GetString()!;
                var expected = assembly.GetProperty("sha256").GetString()!;
                var bytes = File.ReadAllBytes(System.IO.Path.Combine(apps, file));
                var actual = Convert.ToHexString(SHA256.HashData(bytes));
                Assert.True(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                    $"hash mismatch in staged '{file}': {actual} != {expected}");
                images[assembly.GetProperty("name").GetString()!] = bytes;
            }
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
                Assembly.Load(image);
            _stagedEntry = AppDomain.CurrentDomain.GetAssemblies()
                .First(a => string.Equals(a.GetName().Name, "HandyControlDemo", StringComparison.OrdinalIgnoreCase));
            XamlLoader.CustomElementSource = _stagedEntry;
            return _stagedEntry;
        }
    }

    private static void VerifyStagedHashes()
    {
        var apps = StagedHandyAppsDirectory();
        using var document = JsonDocument.Parse(
            File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
        foreach (var assembly in document.RootElement.GetProperty("assemblies").EnumerateArray())
        {
            var file = assembly.GetProperty("file").GetString()!;
            var expected = assembly.GetProperty("sha256").GetString()!;
            var actual = Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(System.IO.Path.Combine(apps, file))));
            Assert.True(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                $"hash mismatch in staged '{file}': {actual} != {expected}");
        }
    }

    private static List<DependencyObject> Descendants(DependencyObject root)
    {
        var found = new List<DependencyObject> { root };
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is FrameworkElement element)
                foreach (var child in element.LogicalChildren)
                {
                    found.Add(child);
                    queue.Enqueue(child);
                }
        }
        return found;
    }
}
