// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-124 HandyControl theme.baml Trigger-list + TargetArea run.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the 'SnapRect4ScreenshotStyle' style over well-known Rectangle -514
/// gains its Trigger list (eight TipElement.Placement qualified Triggers
/// with enum values plus two IsSelecting/IsDrawing DataTriggers with
/// constructor-path Bindings, inline "True" values and setters) and the
/// 'TargetArea4ScreenshotStyle' style over Border (with the qualified
/// UIElement.IsHitTestVisible setter plus short-form BorderThickness
/// (-6)/BorderBrush (-5)/Background (-4) setters plus two DataTriggers)
/// lands as a live engine style; the run stops precise at the implicit
/// SimpleItemsControl style whose ControlTemplate carries a Border visual.
///
/// Parser-traced entry map (key consumption order, offsets validated):
/// 'RectangleCircular' at BAML+0x16DB4 (46-byte Rectangle style with the
/// converted RectangleAttach.Circular setter, landed slice-123),
/// 'SnapRect4ScreenshotStyle' at BAML+0x16DE2 (flat run plus the Trigger
/// list at BAML+0x16E7C, landed this slice), 'TargetArea4ScreenshotStyle'
/// at BAML+0x172D1 (Border style plus two DataTriggers, landed this
/// slice), implicit SimpleItemsControl at BAML+0x17425 (precise stop at
/// the Border visual, BAML+0x1744C).
///
/// Byte-exact decode against the staged 466350-byte theme.baml,
/// triangulated three ways (the staged bytes, the per-file
/// TypeInfo/AttributeInfo/StringInfo tables, and a real net10 BAML fixture
/// compiling Trigger/DataTrigger/Margin/Border styles, byte-identical in
/// every staged shape: Trigger -688 with qualified owners, DataTrigger
/// -122, Trigger.Setters content -256, DataTrigger.Setters content -159,
/// Property (0x05) as a sized inline value with no converter id, the
/// 0x0B list as a bare record with a U16 AttributeInfo id, Margin
/// Thickness text with converter -143, Border short-forms -6/-5/-4).
/// Three mid-decode corrections: the 0x05 "flag" byte is the record size
/// (sized record, no converter); the entry roots are Styles (-620), not
/// ControlTemplates (an arithmetic slip reading FD 94 as -108); and the
/// key map runs RectangleCircular/SnapRect/TargetArea (a bare 0x26 scan
/// misled on key order - the parser keytrace is authoritative; the
/// slice-123 report's entry numbering is corrected here).
/// The staged trigger regions contain no 0x37/0x38 records at any record
/// boundary (pinned by the byte spans below): those values live behind
/// the new stop with the richer template surface.
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched;
/// staged-oracle status-gate bypass stays in-probe only).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice124BamlTriggersTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice124BamlTriggersTests()
    {
        WpfAnimationClock.ResetForTests();
    }

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
        WpfAnimationClock.ResetForTests();
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
        _ = entry;
    }

    [Fact]
    public void Staged_tip_element_carries_placement_condition_shape()
    {
        // The staged SnapRect Trigger conditions name the converted
        // HandyControl.Controls.TipElement.Placement attached DP: the owner
        // resolves through the converted image and the enum carries the
        // eight staged condition values.
        LoadStagedHandyControlEntry();
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var tip = handyControl.GetType("HandyControl.Controls.TipElement");
        Assert.NotNull(tip);
        var placement = Assert.IsType<DependencyProperty>(tip!.GetField("PlacementProperty")!.GetValue(null));
        Assert.True(placement.IsAttached);
        Assert.Equal("Placement", placement.Name);
        var values = Enum.GetNames(placement.PropertyType);
        foreach (var name in new[] { "LeftTop", "Top", "RightTop", "Right", "RightBottom", "Bottom", "LeftBottom", "Left" })
            Assert.Contains(name, values);
    }

    [Fact]
    public void Border_thickness_is_a_shared_control_dp_with_render_bridge()
    {
        // Real WPF parity: Border.BorderThicknessProperty exists (shared
        // with Control like BackgroundProperty); the CLR wrapper stays
        // store-backed with the uniform-Left render bridge.
        XamlLoader.CustomElementSource = null;
        Assert.Same(Control.BorderThicknessProperty, Border.BorderThicknessProperty);
        Assert.Same(Border.BorderThicknessProperty, DependencyProperty.FromName(typeof(Border), "BorderThickness"));
        Assert.Equal(new Thickness(0), Border.BorderThicknessProperty.DefaultMetadata?.DefaultValue);
        var border = new Border();
        Assert.Equal(new Thickness(0), border.BorderThickness);
        border.BorderThickness = new Thickness(5);
        Assert.Equal(new Thickness(5), border.BorderThickness);
        Assert.Equal(5.0, border.Thickness);
        Assert.Equal(new Thickness(5), border.GetValue(Border.BorderThicknessProperty));
    }

    [Fact]
    public void Theme_baml_lands_trigger_lists_then_stops_at_target_area()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        // Byte-verbatim SnapRect Trigger-list head: 0x0B list start with the
        // Style.Triggers AttributeInfo id, then the first Trigger with the
        // qualified owner-39 (TipElement) Placement PropertyCustom.
        Assert.Equal(
            new byte[] { 0x0B, 0x0C, 0x00, 0x03, 0x50, 0xFD, 0x00 },
            theme[0x16E7C..0x16E83]);
        Assert.Equal(
            new byte[] { 0x06, 0x11, 0x0D, 0x00, 0x89, 0x40, 0x27, 0x00, 0x09, 0x50, 0x6C, 0x61, 0x63, 0x65, 0x6D, 0x65, 0x6E, 0x74, 0x24 },
            theme[0x16E83..0x16E96]);
        // First Trigger Value plus Setters content: LeftTop/EnumConverter
        // with the Trigger.Setters content member -256.
        Assert.Equal(
            new byte[] { 0x24, 0x0D, 0x0E, 0x00, 0x07, 0x4C, 0x65, 0x66, 0x74, 0x54, 0x6F, 0x70, 0x3D, 0xFF, 0x2E, 0x00, 0xFF },
            theme[0x16E95..0x16EA6]);
        // Ninth trigger is the first DataTrigger: -122 with the complex
        // Binding attribute plus the Binding child with the IsSelecting
        // constructor path.
        Assert.Equal(
            new byte[] { 0x03, 0x86, 0xFF, 0x00, 0x07, 0x0F, 0x00, 0x03, 0xEC, 0xFF, 0x00, 0x2A, 0x10, 0x0D, 0x0B, 0x49 },
            theme[0x17247..0x17257]);
        // The inline (0x05, sized, no converter) Value "True" plus the
        // DataTrigger.Setters content member -159.
        Assert.Equal(
            new byte[] { 0x05, 0x08, 0x10, 0x00, 0x04, 0x54, 0x72, 0x75, 0x65, 0x2E, 0x61, 0xFF },
            theme[0x17264..0x17270]);
        // SnapRect close run: Visibility converter, setter/trigger ends, list
        // end, style end, then the TargetArea Style start with the Border
        // TargetType plus the Setters content.
        Assert.Equal(
            new byte[] { 0xFF, 0x04, 0x04, 0x0C, 0x04, 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00 },
            theme[0x172CC..0x172D8]);
        // TargetArea qualified UIElement.IsHitTestVisible PropertyCustom head.
        Assert.Equal(
            new byte[] { 0x06, 0x18, 0x01, 0x00, 0x89, 0x40, 0x4A, 0xFD, 0x10 },
            theme[0x172E1..0x172EA]);
        // TargetArea Trigger-list head: 0x0B with the Triggers row plus the
        // IsSelecting DataTrigger with the complex Binding attribute.
        Assert.Equal(
            new byte[] { 0x0B, 0x0C, 0x00, 0x03, 0x86, 0xFF, 0x00, 0x07, 0x0F, 0x00, 0x03 },
            theme[0x1737B..0x17386]);
        // TargetArea close run into the implicit SimpleItemsControl Style start: the BorderThickness "1"
        // value with converter -143, setter/trigger/list/style ends, then
        // the Style start at 0x17425 with the TargetType over TypeInfo 40
        // (SimpleItemsControl) plus the Setters content.
        Assert.Equal(
            new byte[] { 0x04, 0x04, 0x0C, 0x04, 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x28, 0x00, 0x2E, 0x12, 0xFF },
            theme[0x17421..0x17431]);
        // The 'RectangleCircular' key in the StringInfo table (id 101).
        Assert.Equal(
            new byte[] { 0x20, 0x15, 0x65, 0x00, 0x11, 0x52, 0x65 },
            theme[0x16E7..0x16EE]);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(theme));
        // Entries #87-109 materialize in index order (the entry machine
        // consumes keys strictly in order, so any earlier failure would
        // throw its own offset instead).
        Assert.DoesNotContain("BAML+0x166FF", error.Message);
        Assert.DoesNotContain("PathBaseStyle", error.Message);
        Assert.DoesNotContain("BAML+0x167B8", error.Message);
        Assert.DoesNotContain("x:Static", error.Message);
        Assert.DoesNotContain("VerticalScrollBarButtonHeightKey", error.Message);
        Assert.DoesNotContain("BAML+0x167C8", error.Message);
        Assert.DoesNotContain("BehaviorXY200", error.Message);
        Assert.DoesNotContain("FluidMoveBehavior", error.Message);
        Assert.DoesNotContain("BAML+0x168D2", error.Message);
        Assert.DoesNotContain("BlendEffectBox", error.Message);
        Assert.DoesNotContain("BAML+0x16926", error.Message);
        Assert.DoesNotContain("BorderCircular", error.Message);
        Assert.DoesNotContain("converted type", error.Message);
        Assert.DoesNotContain("BAML+0x16954", error.Message);
        Assert.DoesNotContain("HandyControl.Controls.Col", error.Message);
        Assert.DoesNotContain("well-known member id -132", error.Message);
        Assert.DoesNotContain("BAML+0x16990", error.Message);
        Assert.DoesNotContain("BAML+0x169D0", error.Message);
        Assert.DoesNotContain("HandyControl.Controls.FlipClock", error.Message);
        Assert.DoesNotContain("templates take a ContentPresenter visual", error.Message);
        Assert.DoesNotContain("BAML+0x16BBB", error.Message);
        Assert.DoesNotContain("HandyControl.Controls.FloatingBlock", error.Message);
        Assert.DoesNotContain("(implicit style key 'GridSplitter')", error.Message);
        Assert.DoesNotContain("BAML+0x16C96", error.Message);
        Assert.DoesNotContain("setter property owner Window", error.Message);
        Assert.DoesNotContain("well-known member id -24", error.Message);
        Assert.DoesNotContain("BAML+0x16E7C", error.Message);
        Assert.DoesNotContain("RectangleCircular", error.Message);
        Assert.DoesNotContain("SnapRect4ScreenshotStyle", error.Message);
        Assert.DoesNotContain("TargetArea4ScreenshotStyle", error.Message);
        Assert.DoesNotContain("holds a Trigger list", error.Message);
        Assert.DoesNotContain("BAML+0x1760B", error.Message);
        Assert.DoesNotContain("BAML+0x17C06", error.Message);
        Assert.DoesNotContain("Right2LeftTransition", error.Message);
        Assert.DoesNotContain("SliderRepeatButtonHorizontalStyle", error.Message);
        Assert.DoesNotContain("SliderRepeatButtonVerticalStyle", error.Message);
        Assert.DoesNotContain("CompareSliderRepeatButtonTransparent", error.Message);
        Assert.DoesNotContain("ScrollBarBaseRepeatButton", error.Message);
        Assert.DoesNotContain("BAML+0x17F4C", error.Message);
        Assert.DoesNotContain("ToggleBlockBaseStyle", error.Message);
        Assert.DoesNotContain("well-known member id -38", error.Message);
        Assert.DoesNotContain("BAML+0x1835D", error.Message);
        Assert.DoesNotContain("GroupBoxBaseTopTemplate", error.Message);
        Assert.DoesNotContain("BAML+0x1859F", error.Message);
        Assert.DoesNotContain("GroupBoxBaseLeftTemplate", error.Message);
        Assert.DoesNotContain("holds ControlTemplate", error.Message);
        Assert.DoesNotContain("BAML+0x1888C", error.Message);
        Assert.DoesNotContain("GroupBoxTabTopTemplate", error.Message);
        Assert.DoesNotContain("GroupBoxTabLeftTemplate", error.Message);
        Assert.DoesNotContain("template Binding holds record 0x38", error.Message);
        Assert.DoesNotContain("BAML+0x18D3F", error.Message);
        Assert.DoesNotContain("ButtonGroupHorizontalUniformItemsPanelTemplate", error.Message);
        Assert.DoesNotContain("holds well-known type id -330", error.Message);
        Assert.DoesNotContain("BAML+0x18DBD", error.Message);
        Assert.DoesNotContain("ProgressBarIndicatorAnimatedFill", error.Message);
        Assert.DoesNotContain("holds well-known type id -358", error.Message);
        Assert.DoesNotContain("BAML+0x18E5D", error.Message);
        Assert.DoesNotContain("BAML+0x19029", error.Message);
        Assert.DoesNotContain("CircleProgressBarTemplate", error.Message);
        Assert.DoesNotContain("VirtualizingStackHorizontalItemsPanelTemplate", error.Message);
        Assert.DoesNotContain("WrapHorizontalItemsPanelTemplate", error.Message);
        Assert.DoesNotContain("WrapVerticalItemsPanelTemplate", error.Message);
        Assert.DoesNotContain("DockItemsPanelTemplate", error.Message);
        Assert.DoesNotContain("ResizeGripperForeground", error.Message);
        Assert.DoesNotContain("SineEaseOut", error.Message);
        Assert.DoesNotContain("BAML+0x19150", error.Message);
        Assert.DoesNotContain("BadgeDotTemplate", error.Message);
        Assert.DoesNotContain("element ContentPresenter on the template root panel", error.Message);
        Assert.DoesNotContain("root panels take ContentPresenter, Border, converted Shape and TextBlock children", error.Message);
        Assert.DoesNotContain("BAML+0x1927D", error.Message);
        Assert.DoesNotContain("ColorPickerSliderThumbTemplate", error.Message);
        Assert.DoesNotContain("BAML+0x192B9", error.Message);
        Assert.DoesNotContain("ColorPickerRainbowBrush", error.Message);
        Assert.DoesNotContain("has no Offset", error.Message);
        Assert.DoesNotContain("BAML+0x19383", error.Message);
        Assert.DoesNotContain("ColorPickerOpacityBrush", error.Message);
        Assert.DoesNotContain("well-known type id -180", error.Message);
        Assert.DoesNotContain("BAML+0x19497", error.Message);
        Assert.DoesNotContain("CoverFlowBaseStyle", error.Message);
        Assert.DoesNotContain("well-known type id -729", error.Message);
        Assert.DoesNotContain("BAML+0x195FD", error.Message);
        Assert.DoesNotContain("GridViewColumnHeaderGripper", error.Message);
        Assert.DoesNotContain("well-known type id -66", error.Message);
        Assert.DoesNotContain("Canvas.Right", error.Message);
        Assert.DoesNotContain("BAML+0x196C6", error.Message);
        Assert.DoesNotContain("'Padding' is not a dependency property of Border", error.Message);
        Assert.DoesNotContain("SortByCategoryDrawingBrush", error.Message);
        Assert.DoesNotContain("BAML+0x196F6", error.Message);
        Assert.DoesNotContain("SortByNameDrawingBrush", error.Message);
        Assert.DoesNotContain("TabItemMenu", error.Message);
        Assert.DoesNotContain("BAML+0x19D99", error.Message);
        Assert.DoesNotContain("BAML+0x19DCE", error.Message);
        Assert.DoesNotContain("well-known type id -105", error.Message);
        Assert.DoesNotContain("Lang.Close", error.Message);
        Assert.DoesNotContain("throws reading the static property", error.Message);
        Assert.DoesNotContain("XmlnsDefinitionAttribute", error.Message);
        Assert.DoesNotContain("ToolBarTrayBaseStyle", error.Message);
        Assert.DoesNotContain("BAML+0x19E4F", error.Message);
        Assert.DoesNotContain("well-known type id -672", error.Message);
        Assert.DoesNotContain("TreeArrow", error.Message);
        Assert.DoesNotContain("BAML+0x19E7B", error.Message);
        Assert.DoesNotContain("well-known type id -446", error.Message);
        Assert.DoesNotContain("RowHeaderGripperStyle", error.Message);
        Assert.DoesNotContain("BAML+0x1A034", error.Message);
        Assert.DoesNotContain("'Cursor' is not a dependency property of FrameworkElement", error.Message);
        Assert.DoesNotContain("DataGridComboBoxColumnStyle", error.Message);
        Assert.DoesNotContain("DataGridTextColumnStyle", error.Message);        
        Assert.DoesNotContain("CarouselItemBaseStyle", error.Message);
        Assert.DoesNotContain("BAML+0x1A113", error.Message);
        Assert.DoesNotContain("BAML+0x1A186", error.Message);        
        Assert.DoesNotContain("BAML+0x1A21B", error.Message);
        Assert.DoesNotContain("well-known type id -92", error.Message);
        Assert.DoesNotContain("targets TextBlock", error.Message);        
        Assert.DoesNotContain("template property id -21", error.Message);
        Assert.DoesNotContain("GrowlWindow", error.Message);
        Assert.DoesNotContain("BAML+0x1A2CA", error.Message);
        Assert.DoesNotContain("well-known member id -51", error.Message);
        Assert.DoesNotContain("ListBoxItemCustom", error.Message);
        Assert.DoesNotContain("BAML+0x1A368", error.Message);
        Assert.DoesNotContain("well-known type id -374", error.Message);
        Assert.DoesNotContain("BAML+0x1A92D", error.Message);
        Assert.DoesNotContain("well-known member id -81", error.Message);
        Assert.DoesNotContain("RibbonTab", error.Message);
        Assert.DoesNotContain("BAML+0x1A990", error.Message);
        Assert.DoesNotContain("template root panel property 'Visibility' with converter -195", error.Message);
        Assert.DoesNotContain("BAML+0x1A9DD", error.Message);
        Assert.DoesNotContain("element well-known type id -550 on the template root panel", error.Message);
        Assert.DoesNotContain("BAML+0x1AE91", error.Message);
        Assert.DoesNotContain("'EffectShadow1'", error.Message);
        Assert.DoesNotContain("record 0x34 (converted-element values", error.Message);
        Assert.DoesNotContain("BAML+0x1B001", error.Message);
        Assert.DoesNotContain("BAML+0x1B22E", error.Message);
        Assert.DoesNotContain("'SearchPathStyle'", error.Message);
        Assert.DoesNotContain("'RightPathStyle'", error.Message);
        Assert.DoesNotContain("record 0x38 in a Style", error.Message);
        Assert.DoesNotContain("'RenderTransformOrigin' is not a dependency property of UIElement", error.Message);
        Assert.DoesNotContain("BAML+0x1B260", error.Message);
        Assert.DoesNotContain("well-known member id -134", error.Message);
        Assert.Contains("'BorderRegion'", error.Message);
        Assert.Contains("BAML+0x1B2F9", error.Message);
        Assert.Contains("setter property owner Border in style entry 'BorderRegion'", error.Message);
        Assert.DoesNotContain("BAML+0x1AA35", error.Message);
        Assert.DoesNotContain("'PrimaryBrush'", error.Message);
        Assert.DoesNotContain("record 0x34 in a LinearGradientBrush", error.Message);
        Assert.DoesNotContain("BAML+0x1AA1C", error.Message);
        Assert.DoesNotContain("LightPrimaryBrush", error.Message);
        Assert.DoesNotContain("well-known type id -578", error.Message);
        Assert.DoesNotContain("on setter 'Template'", error.Message);
        Assert.DoesNotContain("well-known type id -658", error.Message);
        Assert.DoesNotContain("HandyControl.Controls.SimplePanel", error.Message);
        Assert.DoesNotContain("VirtualizingStackPanel", error.Message);
        Assert.DoesNotContain("templates take a ContentPresenter", error.Message);
        Assert.DoesNotContain("items panels take a UniformGrid/StackPanel/VirtualizingStackPanel/WrapPanel/DockPanel visual", error.Message);

        Assert.DoesNotContain("well-known type id -608", error.Message);
        Assert.DoesNotContain("well-known type id -522", error.Message);
        Assert.DoesNotContain("BAML+0x1744C", error.Message);
        Assert.DoesNotContain("element Border on the template", error.Message);
        Assert.DoesNotContain("SimpleItemsControl", error.Message);
        Assert.DoesNotContain("BAML+0x17483", error.Message);
        Assert.DoesNotContain("Sprite", error.Message);
        Assert.DoesNotContain("IsDragElement", error.Message);
        Assert.DoesNotContain("HandyControl.Controls.SimpleItemsControl", error.Message);
    }

    [Fact]
    public void Staged_boot_advances_past_target_area_to_simple_items_control()
    {
        var entry = LoadStagedHandyControlEntry();
        var error = Assert.Throws<InvalidOperationException>(() => WpfAppBoot.Boot(entry));
        Assert.Contains("startup failed in entry point", error.Message);
        // The line-17 SkinDefault stop, the line-23 Theme key/index plus
        // scalar run, PathBaseStyle, the x:Static-keyed Double, the six
        // converted Behavior entries, the implicit BlendEffectBox style, the
        // 'BorderCircular' entry, the implicit Col/ContentControl styles,
        // the implicit FlipClock style, the implicit FloatingBlock style,
        // the implicit GridSplitter style, the implicit Notification style,
        // the 'RectangleCircular' entry, the 'SnapRect4ScreenshotStyle' flat
        // run plus its Trigger list and the 'TargetArea4ScreenshotStyle'
        // entry now resolve; boot fails at the implicit SimpleItemsControl
        // style - the precise next blocker.
        Assert.DoesNotContain("SkinDefault", error.Message);
        var inner = Assert.IsType<XamlLoadException>(error.InnerException);
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
        Assert.Contains("Themes/Theme.xaml", inner.Message);
        Assert.Contains("line 23", inner.Message);
        Assert.DoesNotContain("BAML+0x16954", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.Col", inner.Message);
        Assert.DoesNotContain("well-known member id -132", inner.Message);
        Assert.DoesNotContain("BAML+0x16BBB", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.FloatingBlock", inner.Message);
        Assert.DoesNotContain("(implicit style key 'GridSplitter')", inner.Message);
        Assert.DoesNotContain("BAML+0x16C96", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.Notification", inner.Message);
        Assert.DoesNotContain("setter property owner Window", inner.Message);
        Assert.DoesNotContain("BAML+0x16E7C", inner.Message);
        Assert.DoesNotContain("RectangleCircular", inner.Message);
        Assert.DoesNotContain("SnapRect4ScreenshotStyle", inner.Message);
        Assert.DoesNotContain("TargetArea4ScreenshotStyle", inner.Message);
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
    public void Staged_trigger_list_lands_against_converted_tip_element()
    {
        // The staged SnapRect Trigger shape (qualified TipElement.Placement
        // condition with a raw enum value plus Margin/HorizontalAlignment/
        // VerticalAlignment setters) lands through the converted-owner
        // path against the staged HandyControl image.
        LoadStagedHandyControlEntry();
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(handyControl.GetType("HandyControl.Controls.TipElement"));
        var bytes = QualifiedTriggerDictionary("HandyControl.Controls.TipElement", "Placement", "LeftTop");
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary[typeof(Rectangle)]);
        Assert.Same(typeof(Rectangle), style.TargetType);
        var trigger = Assert.IsType<Trigger>(Assert.Single(style.Triggers));
        Assert.Equal("TipElement.Placement", trigger.Property);
        Assert.Equal("LeftTop", trigger.Value);
        Assert.Equal(3, trigger.Setters.Count);
        var margin = Assert.IsType<Setter>(trigger.Setters[0]);
        Assert.Same(FrameworkElement.MarginProperty, margin.Property);
        Assert.Equal(new Thickness(-3, -3, 0, 0), margin.Value);
        Assert.Equal(HorizontalAlignment.Left, Assert.IsType<Setter>(trigger.Setters[1]).Value);
        Assert.Equal(VerticalAlignment.Top, Assert.IsType<Setter>(trigger.Setters[2]).Value);
    }

    [Fact]
    public void Synthetic_trigger_list_lands_and_applies()
    {
        // The full synthetic trigger run (qualified attached condition
        // plus a DataTrigger with a constructor-path Binding and an inline
        // value) lands and evaluates like the XAML trigger path.
        XamlLoader.CustomElementSource = null;
        var bytes = FullTriggerDictionary();
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary[typeof(Border)]);
        Assert.Equal(2, style.Triggers.Count);
        var trigger = Assert.IsType<Trigger>(style.Triggers[0]);
        Assert.Equal("Slice124OwnerStub.Mode", trigger.Property);
        Assert.Equal("A", trigger.Value);
        var thickness = Assert.IsType<Setter>(Assert.Single(trigger.Setters));
        Assert.Same(Border.BorderThicknessProperty, thickness.Property);
        Assert.Equal(new Thickness(2), thickness.Value);
        var data = Assert.IsType<DataTrigger>(style.Triggers[1]);
        Assert.Equal("Flag", Assert.IsType<Binding>(data.Binding!).Path);
        Assert.Equal("True", data.Value);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<Setter>(Assert.Single(data.Setters)).Value);
        // Applying with a matching attached condition fires the trigger
        // setters; with a non-matching condition the base setters stand.
        var plain = new Border { BorderThickness = new Thickness(1) };
        plain.SetAttached("Slice124OwnerStub.Mode", Slice124Mode.B);
        style.ApplyTo(plain);
        Assert.Equal(new Thickness(1), plain.BorderThickness);
        var active = new Border { BorderThickness = new Thickness(1) };
        active.SetAttached("Slice124OwnerStub.Mode", Slice124Mode.A);
        style.ApplyTo(active);
        Assert.Equal(new Thickness(2), active.BorderThickness);
        Assert.Equal(2.0, active.Thickness);
        // The DataTrigger fires off the data context and stays quiet
        // without one.
        var bound = new Border { Visibility = Visibility.Visible };
        bound.SetAttached("Slice124OwnerStub.Mode", Slice124Mode.B);
        bound.DataContext = new Slice124ViewModel { Flag = true };
        style.ApplyTo(bound);
        Assert.Equal(Visibility.Collapsed, bound.Visibility);
        var unbound = new Border { Visibility = Visibility.Visible };
        unbound.SetAttached("Slice124OwnerStub.Mode", Slice124Mode.B);
        style.ApplyTo(unbound);
        Assert.Equal(Visibility.Visible, unbound.Visibility);
    }

    [Fact]
    public void Synthetic_target_area_entry_lands_and_applies()
    {
        // The staged 'TargetArea4ScreenshotStyle' entry shape (qualified
        // UIElement.IsHitTestVisible plus short-form BorderThickness/
        // BorderBrush/Background plus two DataTriggers) lands and applies
        // through the CLR-first setter path with the uniform-Left render
        // bridge intact.
        XamlLoader.CustomElementSource = null;
        var bytes = KeyedTargetAreaDictionary();
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary["TargetArea4ScreenshotStyle"]);
        Assert.Same(typeof(Border), style.TargetType);
        Assert.Equal(6, style.Setters.Count);
        var hitTest = Assert.IsType<Setter>(style.Setters[0]);
        Assert.Same(UIElement.IsHitTestVisibleProperty, hitTest.Property);
        Assert.Equal(false, hitTest.Value);
        Assert.Equal(new Thickness(5), Assert.IsType<Setter>(style.Setters[1]).Value);
        Assert.Same(Border.BorderThicknessProperty, Assert.IsType<Setter>(style.Setters[1]).Property);
        Assert.Equal(Color.FromArgb(255, 30, 144, 255), Assert.IsType<SolidColorBrush>(Assert.IsType<Setter>(style.Setters[2]).Value).Color);
        Assert.Equal(Color.FromArgb(0, 255, 255, 255), Assert.IsType<SolidColorBrush>(Assert.IsType<Setter>(style.Setters[3]).Value).Color);
        Assert.Equal(HorizontalAlignment.Left, Assert.IsType<Setter>(style.Setters[4]).Value);
        Assert.Equal(VerticalAlignment.Top, Assert.IsType<Setter>(style.Setters[5]).Value);
        Assert.Equal(2, style.Triggers.Count);
        var selecting = Assert.IsType<DataTrigger>(style.Triggers[0]);
        Assert.Equal("IsSelecting", Assert.IsType<Binding>(selecting.Binding!).Path);
        var drawing = Assert.IsType<DataTrigger>(style.Triggers[1]);
        Assert.Equal("IsDrawing", Assert.IsType<Binding>(drawing.Binding!).Path);
        Assert.Equal(2, drawing.Setters.Count);
        var border = new Border();
        style.ApplyTo(border);
        Assert.False(border.IsHitTestVisible);
        Assert.Equal(new Thickness(5), border.BorderThickness);
        Assert.Equal(5.0, border.Thickness);
        Assert.Equal(HorizontalAlignment.Left, border.HorizontalAlignment);
    }

    [Fact]
    public void Trigger_static_value_fails_precise()
    {
        // A StaticResource (0x38) trigger value is a precise follow-up,
        // never a silently dropped setter.
        XamlLoader.CustomElementSource = null;
        var bytes = TriggerValueDictionary(0x38, q => q.U16(2).U16(0));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("StaticResource value on the trigger", error.Message);
        Assert.Contains("trigger static values are a style-surface follow-up", error.Message);
    }

    [Fact]
    public void Trigger_markup_value_fails_precise()
    {
        // A markup-extension (0x23) trigger value is a precise follow-up.
        XamlLoader.CustomElementSource = null;
        var bytes = TriggerValueDictionary(0x23, q => q.I16(-49).U16(634).I16(-35));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("markup-extension value on the trigger", error.Message);
        Assert.Contains("trigger markup values are a style-surface follow-up", error.Message);
    }

    [Fact]
    public void Trigger_list_rejects_non_trigger_elements()
    {
        // Only Trigger/DataTrigger elements belong in a trigger list.
        XamlLoader.CustomElementSource = null;
        var bytes = TriggerListElementDictionary(-100);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("element ContentControl in the trigger list", error.Message);
        Assert.Contains("trigger lists take Trigger/DataTrigger", error.Message);
    }

    [Fact]
    public void Trigger_short_form_beyond_set_fails_precise()
    {
        // Short-form trigger properties resolve against the style
        // TargetType; members outside the staged set fail naming the set.
        XamlLoader.CustomElementSource = null;
        var bytes = ShortTriggerDictionary(-110, "x", -195);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("trigger property well-known member id -110", error.Message);
        Assert.Contains("short-form trigger properties beyond", error.Message);
    }

    [Fact]
    public void Data_trigger_simple_binding_value_fails_precise()
    {
        // DataTrigger Bindings take a constructor path only; simples are
        // a precise follow-up.
        XamlLoader.CustomElementSource = null;
        var bytes = SimpleBindingTriggerDictionary();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("holds a simple value", error.Message);
        Assert.Contains("bindings take a path", error.Message);
    }

    [Fact]
    public void Data_trigger_value_before_binding_fails_precise()
    {
        // BAML emits Binding before Value; the order is enforced.
        XamlLoader.CustomElementSource = null;
        var bytes = ValueBeforeBindingDictionary();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("Value before Binding on the data trigger", error.Message);
    }

    [Fact]
    public void Data_trigger_wrong_content_member_fails_precise()
    {
        // DataTrigger setters travel under -159, not the Style -238.
        XamlLoader.CustomElementSource = null;
        var bytes = WrongContentTriggerDictionary();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("content member -238 on the data trigger", error.Message);
        Assert.Contains("data triggers take the Setters content", error.Message);
    }

    [Fact]
    public void Trigger_list_without_target_type_fails_precise()
    {
        // The list needs the style TargetType for short-form resolution.
        XamlLoader.CustomElementSource = null;
        var bytes = TargetlessTriggerListDictionary();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("precedes its style TargetType", error.Message);
    }

    /// <summary>Builds a single-entry implicit-style dictionary over
    /// well-known Rectangle with one qualified Trigger (Margin plus
    /// HorizontalAlignment/VerticalAlignment setters).</summary>
    private static byte[] QualifiedTriggerDictionary(string ownerFullName, string property, string value)
    {
        var entry = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-514)
            .Raw(0x2E).I16(-238)
            .Raw(0x0B).U16(5)
            .Raw(0x03).I16(-688).Raw(0x00)
            .Sized(0x06, q => q.I16(3).U16(16521).I16(0).Str(property))
            .Sized(0x24, q => q.I16(4).Str(value).I16(-195))
            .Raw(0x2E).I16(-256);
        Setter(entry, -49, q => q.Sized(0x24, r => r.I16(2).Str("-3, -3, 0, 0").I16(-143)));
        Setter(entry, -48, q => q.Sized(0x24, r => r.I16(2).Str("Left").I16(-195)));
        Setter(entry, -56, q => q.Sized(0x24, r => r.I16(2).Str("Top").I16(-195)));
        entry.Raw(0x04).Raw(0x0C).Raw(0x04);
        static void Setter(B entry, short member, Action<B> value)
        {
            entry
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, q => q.I16(1).U16(137).I16(member));
            value(entry);
            entry.Raw(0x04);
        }
        return ImplicitDictionary(entry.ToArray(), -514,
            [(ownerFullName, (ushort)0)],
            [(0, "TargetType", -620), (1, "Property", -556), (2, "Value", -556),
                (3, "Property", -688), (4, "Value", -688), (5, "Triggers", -620)]);
    }

    /// <summary>Builds a single-entry implicit-style dictionary over Border
    /// with one qualified Trigger (BorderThickness) plus one DataTrigger
    /// (constructor-path Binding, inline value, Visibility setter).</summary>
    private static byte[] FullTriggerDictionary()
    {
        var entry = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-50)
            .Raw(0x2E).I16(-238);
        Setter(entry, -6, q => q.Sized(0x24, r => r.I16(2).Str("1").I16(-143)));
        entry
            .Raw(0x0B).U16(3)
            .Raw(0x03).I16(-688).Raw(0x00)
            .Sized(0x06, q => q.I16(4).U16(16521).I16(0).Str("Mode"))
            .Sized(0x24, q => q.I16(5).Str("A").I16(-195))
            .Raw(0x2E).I16(-256);
        Setter(entry, -6, q => q.Sized(0x24, r => r.I16(2).Str("2").I16(-143)));
        entry
            .Raw(0x04)
            .Raw(0x03).I16(-122).Raw(0x00)
            .Raw(0x07).I16(6)
            .Raw(0x03).I16(-20).Raw(0x00)
            .Raw(0x2A).Sized(0x10, q => q.Str("Flag")).Raw(0x2B)
            .Raw(0x04)
            .Raw(0x08)
            .Sized(0x05, q => q.I16(7).Str("True"))
            .Raw(0x2E).I16(-159);
        Setter(entry, -135, q => q.Sized(0x24, r => r.I16(2).Str("Collapsed").I16(-195)));
        entry.Raw(0x04).Raw(0x0C).Raw(0x04);
        static void Setter(B entry, short member, Action<B> value)
        {
            entry
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, q => q.I16(1).U16(137).I16(member));
            value(entry);
            entry.Raw(0x04);
        }
        return ImplicitDictionary(entry.ToArray(), -50,
            [(typeof(Slice124OwnerStub).FullName!, (ushort)0)],
            [(0, "TargetType", -620), (1, "Property", -556), (2, "Value", -556), (3, "Triggers", -620),
                (4, "Property", -688), (5, "Value", -688), (6, "Binding", -122), (7, "Value", -122)]);
    }

    /// <summary>Builds the staged 'TargetArea4ScreenshotStyle' entry shape:
    /// a keyed style over Border with the qualified UIElement.IsHitTestVisible
    /// setter, short-form BorderThickness/BorderBrush/Background setters,
    /// HorizontalAlignment/VerticalAlignment setters and two DataTriggers
    /// (IsSelecting, IsDrawing).</summary>
    private static byte[] KeyedTargetAreaDictionary()
    {
        var content = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-50)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(16521).I16(-694).Str("IsHitTestVisible"))
            .Sized(0x24, q => q.I16(2).Str("False").I16(-46))
            .Raw(0x04);
        void Short(short member, Action<B> value)
        {
            content
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, q => q.I16(1).U16(137).I16(member));
            value(content);
            content.Raw(0x04);
        }
        Short(-6, q => q.Sized(0x24, r => r.I16(2).Str("5").I16(-143)));
        Short(-5, q => q.Sized(0x06, r => r.I16(2).U16(744).Raw(0x01, 0xFF, 0x90, 0x1E, 0xFF)));
        Short(-4, q => q.Sized(0x06, r => r.I16(2).U16(744).Raw(0x01, 0xFF, 0xFF, 0xFF, 0x00)));
        Short(-48, q => q.Sized(0x24, r => r.I16(2).Str("Left").I16(-195)));
        Short(-56, q => q.Sized(0x24, r => r.I16(2).Str("Top").I16(-195)));
        content.Raw(0x0B).U16(3);
        void DataTrigger(string path)
        {
            content
                .Raw(0x03).I16(-122).Raw(0x00)
                .Raw(0x07).I16(4)
                .Raw(0x03).I16(-20).Raw(0x00)
                .Raw(0x2A).Sized(0x10, q => q.Str(path)).Raw(0x2B)
                .Raw(0x04)
                .Raw(0x08)
                .Sized(0x05, q => q.I16(5).Str("True"))
                .Raw(0x2E).I16(-159);
        }
        DataTrigger("IsSelecting");
        Short(-6, q => q.Sized(0x24, r => r.I16(2).Str("1").I16(-143)));
        content.Raw(0x04);
        DataTrigger("IsDrawing");
        content
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(16521).I16(-694).Str("IsHitTestVisible"))
            .Sized(0x24, q => q.I16(2).Str("True").I16(-46))
            .Raw(0x04);
        Short(-6, q => q.Sized(0x24, r => r.I16(2).Str("1").I16(-143)));
        content.Raw(0x04).Raw(0x0C).Raw(0x04);
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16((short)0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16((short)1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16((short)2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x1F, p => p.I16((short)3).I16(-620).Raw(0x00).Str("Triggers"))
            .Sized(0x1F, p => p.I16((short)4).I16(-122).Raw(0x00).Str("Binding"))
            .Sized(0x1F, p => p.I16((short)5).I16(-122).Raw(0x00).Str("Value"))
            .Sized(0x20, p => p.U16(0).Str("TargetArea4ScreenshotStyle"))
            .Deferable()
            .Sized(0x26, p => p.U16(0).U32(0).U16(0));
        return head.Raw(content.ToArray()).FinishDictionary().ToArray();
    }

    /// <summary>Builds an implicit Border style with one short-form Trigger
    /// whose value travels as the given record (StaticResource 0x38 and
    /// markup-extension 0x23 shapes).</summary>
    private static byte[] TriggerValueDictionary(byte record, Action<B> payload)
    {
        var entry = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-50)
            .Raw(0x2E).I16(-238)
            .Raw(0x0B).U16(5)
            .Raw(0x03).I16(-688).Raw(0x00)
            .Sized(0x06, q => q.I16(3).U16(137).I16(-132))
            .Raw(record);
        payload(entry);
        return ImplicitDictionary(entry.ToArray(), -50, null,
            [(0, "TargetType", -620), (1, "Property", -556), (2, "Value", -556), (3, "Property", -688), (5, "Triggers", -620)]);
    }

    /// <summary>Builds an implicit Border style whose trigger list holds a
    /// non-trigger element.</summary>
    private static byte[] TriggerListElementDictionary(short elementType)
    {
        var entry = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-50)
            .Raw(0x2E).I16(-238)
            .Raw(0x0B).U16(3)
            .Raw(0x03).I16(elementType).Raw(0x00);
        return ImplicitDictionary(entry.ToArray(), -50, null,
            [(0, "TargetType", -620), (3, "Triggers", -620)]);
    }

    /// <summary>Builds an implicit Border style with one short-form Trigger
    /// property.</summary>
    private static byte[] ShortTriggerDictionary(short member, string text, short converter)
    {
        var entry = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-50)
            .Raw(0x2E).I16(-238)
            .Raw(0x0B).U16(5)
            .Raw(0x03).I16(-688).Raw(0x00)
            .Sized(0x06, q => q.I16(3).U16(137).I16(member))
            .Sized(0x24, q => q.I16(4).Str(text).I16(converter));
        return ImplicitDictionary(entry.ToArray(), -50, null,
            [(0, "TargetType", -620), (3, "Property", -688), (4, "Value", -688), (5, "Triggers", -620)]);
    }

    /// <summary>Builds an implicit Border style with a DataTrigger whose
    /// Binding carries a simple (0x24) value.</summary>
    private static byte[] SimpleBindingTriggerDictionary()
    {
        var entry = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-50)
            .Raw(0x2E).I16(-238)
            .Raw(0x0B).U16(6)
            .Raw(0x03).I16(-122).Raw(0x00)
            .Raw(0x07).I16(3)
            .Raw(0x03).I16(-20).Raw(0x00)
            .Sized(0x24, q => q.I16(5).Str("Flag").I16(-615));
        return ImplicitDictionary(entry.ToArray(), -50, null,
            [(0, "TargetType", -620), (3, "Binding", -122), (6, "Triggers", -620)]);
    }

    /// <summary>Builds an implicit Border style with a DataTrigger whose
    /// inline Value precedes the Binding.</summary>
    private static byte[] ValueBeforeBindingDictionary()
    {
        var entry = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-50)
            .Raw(0x2E).I16(-238)
            .Raw(0x0B).U16(6)
            .Raw(0x03).I16(-122).Raw(0x00)
            .Sized(0x05, q => q.I16(4).Str("True"));
        return ImplicitDictionary(entry.ToArray(), -50, null,
            [(0, "TargetType", -620), (4, "Value", -122), (6, "Triggers", -620)]);
    }

    /// <summary>Builds an implicit Border style with a DataTrigger whose
    /// Setters content uses the Style member id.</summary>
    private static byte[] WrongContentTriggerDictionary()
    {
        var entry = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-50)
            .Raw(0x2E).I16(-238)
            .Raw(0x0B).U16(6)
            .Raw(0x03).I16(-122).Raw(0x00)
            .Raw(0x07).I16(3)
            .Raw(0x03).I16(-20).Raw(0x00)
            .Raw(0x2A).Sized(0x10, q => q.Str("Flag")).Raw(0x2B)
            .Raw(0x04)
            .Raw(0x08)
            .Sized(0x05, q => q.I16(4).Str("True"))
            .Raw(0x2E).I16(-238);
        return ImplicitDictionary(entry.ToArray(), -50, null,
            [(0, "TargetType", -620), (3, "Binding", -122), (4, "Value", -122), (6, "Triggers", -620)]);
    }

    /// <summary>Builds a style entry whose trigger list precedes TargetType.</summary>
    private static byte[] TargetlessTriggerListDictionary()
    {
        var entry = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x2E).I16(-238)
            .Raw(0x0B).U16(1);
        return ImplicitDictionary(entry.ToArray(), -50, null,
            [(0, "TargetType", -620), (1, "Triggers", -620)]);
    }

    /// <summary>Builds a single-entry implicit-style dictionary (TypeKey
    /// 0x27) over the given TypeInfo/AttributeInfo tables.</summary>
    private static byte[] ImplicitDictionary(byte[] entry, short keyType, (string FullName, ushort Id)[]? types, (int Id, string Name, short Owner)[] attrs)
    {
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"));
        if (types is not null)
            foreach (var (fullName, id) in types)
                head.Sized(0x1D, p => p.U16(id).U16(0).Str(fullName));
        foreach (var (id, name, owner) in attrs)
            head.Sized(0x1F, p => p.I16((short)id).I16(owner).Raw(0x00).Str(name));
        return head
            .Raw(0x25).U32((uint)(entry.Length + 10))
            .Raw(0x27).I16(keyType).Raw(0x00).U32(0).Raw(0x00, 0x00)
            .Raw(entry)
            .Raw(0x04, 0x02).ToArray();
    }

    public enum Slice124Mode { A, B }

    public sealed class Slice124OwnerStub
    {
        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.RegisterAttached("Mode", typeof(Slice124Mode), typeof(Slice124OwnerStub), new PropertyMetadata(Slice124Mode.B));
    }

    public sealed class Slice124ViewModel
    {
        public bool Flag { get; set; }
    }

    private sealed class B
    {
        private readonly List<byte> _b = new();
        private int _deferPos = -1;
        public B Raw(params byte[] b) { _b.AddRange(b); return this; }
        public B U16(ushort v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
        public B I16(short v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
        public B U32(uint v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
        public B Str(string s)
        {
            var ascii = Encoding.ASCII.GetBytes(s);
            Seven(ascii.Length);
            _b.AddRange(ascii);
            return this;
        }
        public B Seven(int v)
        {
            uint u = (uint)v;
            while (u >= 0x80) { _b.Add((byte)(u | 0x80)); u >>= 7; }
            _b.Add((byte)u);
            return this;
        }
        public B Sized(byte type, Action<B> payload)
        {
            var inner = new B();
            payload(inner);
            var p = inner._b;
            if (p.Count + 1 >= 128) throw new InvalidOperationException("test record too large");
            _b.Add(type);
            _b.Add((byte)(p.Count + 1));
            _b.AddRange(p);
            return this;
        }
        public B Deferable()
        {
            _b.Add(0x25);
            _deferPos = _b.Count;
            _b.AddRange(new byte[4]);
            return this;
        }
        public B FinishDictionary()
        {
            if (_deferPos < 0) throw new InvalidOperationException("no open deferable");
            var endPos = _b.Count;
            _b.Add(0x04);
            var skip = (uint)(endPos - (_deferPos + 4));
            var patch = BitConverter.GetBytes(skip);
            for (var i = 0; i < 4; i++) _b[_deferPos + i] = patch[i];
            _b.Add(0x02);
            _deferPos = -1;
            return this;
        }
        public byte[] ToArray() => _b.ToArray();
    }

    private static byte[] Header() => new B()
        .Raw(0x0C, 0x00, 0x00, 0x00)
        .Raw(0x4D, 0x00, 0x53, 0x00, 0x42, 0x00, 0x41, 0x00, 0x4D, 0x00, 0x4C, 0x00)
        .U16(0).U16(0x60)
        .Raw(0x00, 0x00, 0x60, 0x00, 0x00, 0x00, 0x60, 0x00,
             0x01, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00)
        .ToArray();

    private static byte[] ReadStagedGResource(string assemblyShortName, string key)
    {
        var asm = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, assemblyShortName, StringComparison.OrdinalIgnoreCase));
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new ResourceReader(stream);
            foreach (System.Collections.DictionaryEntry entry in reader)
            {
                if (entry.Key is string k && k.Replace('\\', '/').Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    if (entry.Value is byte[] raw) return raw;
                    if (entry.Value is Stream s) { using var copy = new MemoryStream(); s.CopyTo(copy); return copy.ToArray(); }
                }
            }
        }
        throw new InvalidOperationException("resource not found: " + key);
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
                images[assembly.GetProperty("name").GetString()!] = File.ReadAllBytes(System.IO.Path.Combine(apps, file));
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
}
