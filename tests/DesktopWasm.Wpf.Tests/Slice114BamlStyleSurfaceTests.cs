// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-114 HandyControl theme.baml key/index + scalar-entry surface.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the deferable-dictionary key/index framing (DefAttributeKeyString,
/// implicit-style DefAttributeKeyType, element-form KeyElementStart/End for
/// {x:Static ...}, constructor framing, OptimizedStaticResource and
/// line-marker transparency, entry offset validation) plus the leading scalar
/// entry run (empty converter/provider elements, Color/UInt32/Double/
/// CornerRadius/Geometry text entries) is coherent in one slice: every record
/// kind in the 466350-byte theme.baml frames byte-exact (full walk to EOF,
/// balanced elements), the 950-key index parses with validated positions,
/// and 87 scalar entries materialize with real behavior. The first Style
/// entry (key 'PathBaseStyle' at BAML+0x166FF) stays a precise pinned
/// follow-up with the style/template/trigger/binding values, x:Static key
/// resolution, and complex properties.
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice114BamlStyleSurfaceTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice114BamlStyleSurfaceTests()
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
    public void Theme_baml_loads_style_run_then_stops_at_xstatic()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(theme));
        // The slice-119 stop (BAML+0x16954 implicit Col style entry) is
        // gone: reaching BAML+0x16E7C proves entries #87-107 plus the RectangleCircular/SnapRect/TargetArea entries (the first
        // keyed Style, the scalar tail, the x:Static-keyed Double, the
        // converted Behavior values, the implicit BlendEffectBox style, the
        // 'BorderCircular' entry with its converted attached setter, the
        // implicit Col/ContentControl styles with their short-form Focusable
        // setters, the FlipClock style with its StackPanel/Binding template,
        // the FloatingBlock style with its alignment setters plus
        // TemplateBinding template and the GridSplitter style with its
        // Focusable setter) materialized in index order with validated
        // offsets (see Slice122BamlAlignmentTests).
        Assert.DoesNotContain("BAML+0xB274", error.Message);
        Assert.DoesNotContain("unsupported BAML record 0x28", error.Message);
        Assert.DoesNotContain("BAML+0x166FF", error.Message);
        Assert.DoesNotContain("PathBaseStyle", error.Message);
        Assert.DoesNotContain("BAML+0x167B8", error.Message);
        Assert.DoesNotContain("x:Static key resolution", error.Message);
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
        Assert.DoesNotContain("BAML+0x16C96", error.Message);
        Assert.DoesNotContain("HandyControl.Controls.Notification", error.Message);
        Assert.DoesNotContain("setter property owner Window", error.Message);
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
    public void Staged_boot_advances_past_style_run_to_xstatic()
    {
        var entry = LoadStagedHandyControlEntry();
        var error = Assert.Throws<InvalidOperationException>(() => WpfAppBoot.Boot(entry));
        Assert.Contains("startup failed in entry point", error.Message);
        var inner = Assert.IsType<XamlLoadException>(error.InnerException);
        // The line-17 SkinDefault stop (plus its nested Colors merge), the
        // line-23 Theme key/index + scalar run, the PathBaseStyle entry,
        // the x:Static-keyed Double entry, the converted Behavior entries,
        // the implicit BlendEffectBox style, the 'BorderCircular' entry, the
        // implicit Col/ContentControl styles, the FlipClock style, the
        // FloatingBlock style and the GridSplitter style now resolve; boot
        // fails at the implicit Notification style entry #107 - the precise
        // next blocker.
        Assert.DoesNotContain("SkinDefault", inner.Message);
        Assert.DoesNotContain("line 17", inner.Message);
        Assert.DoesNotContain("BAML+0xB274", inner.Message);
        Assert.DoesNotContain("unsupported BAML record 0x28", inner.Message);
        Assert.DoesNotContain("BAML+0x166FF", inner.Message);
        Assert.DoesNotContain("PathBaseStyle", inner.Message);
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
    public void Empty_converter_entry_instantiates_engine_type()
    {
        // TypeInfo entry naming the engine BooleanToVisibilityConverter with
        // an empty element instantiates it (the engine-type branch of the
        // empty-element rule; the staged run uses converted lib types).
        var bytes = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1D, p => p.U16(0).U16(0).Str("DesktopWasm.Wpf.BooleanToVisibilityConverter"))
            .Sized(0x20, p => p.U16(0).Str("C"))
            .Deferable()
            .Sized(0x26, p => p.U16(0).U32(0).U16(0))
            .Raw(0x03).I16(0).Raw(0x00)
            .Raw(0x04)
            .FinishDictionary().ToArray();
        var dictionary = BamlReader.LoadDictionary(bytes);
        Assert.IsType<BooleanToVisibilityConverter>(dictionary["C"]);
    }

    [Fact]
    public void Wellknown_converter_entries_instantiate_framework_shapes()
    {
        // Well-known empty elements for the framework converters the theme
        // keys (-49 BooleanToVisibilityConverter, -404
        // MenuScrollingVisibilityConverter) instantiate engine parity types.
        var bytes = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x20, p => p.U16(0).Str("B"))
            .Sized(0x20, p => p.U16(1).Str("M"))
            .Deferable()
            .Sized(0x26, p => p.U16(0).U32(0).U16(0))
            .Sized(0x26, p => p.U16(1).U32(5).U16(0))
            .Raw(0x03).I16(-49).Raw(0x00)
            .Raw(0x04)
            .Raw(0x03).I16(-404).Raw(0x00)
            .Raw(0x04)
            .FinishDictionary().ToArray();
        var dictionary = BamlReader.LoadDictionary(bytes);
        Assert.IsType<BooleanToVisibilityConverter>(dictionary["B"]);
        Assert.IsType<MenuScrollingVisibilityConverter>(dictionary["M"]);
    }

    [Fact]
    public void Scalar_text_entries_parse_with_freeze()
    {
        var content = new B();
        var d = content.Position;
        content.Raw(0x03).I16(-168).Raw(0x00)
            .Sized(0x11, p => p.Str("24").I16(-175))
            .Raw(0x04);
        var r = content.Position;
        content.Raw(0x03).I16(-110).Raw(0x00)
            .Sized(0x11, p => p.Str("4").I16(-111))
            .Raw(0x04);
        var g = content.Position;
        content.Raw(0x03).I16(-237).Raw(0x00)
            .Sized(0x11, p => p.Str("M0,0L10,10z").I16(-240))
            .Raw(0x04);
        var gf = content.Position;
        content.Raw(0x03).I16(-237).Raw(0x00)
            .Sized(0x34, p => p.Str("True").U16(0))
            .Sized(0x11, p => p.Str("M0,0L28,0 28,14z").I16(-240))
            .Raw(0x04);
        var bytes = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x20, p => p.U16(0).Str("D"))
            .Sized(0x20, p => p.U16(1).Str("R"))
            .Sized(0x20, p => p.U16(2).Str("G"))
            .Sized(0x20, p => p.U16(3).Str("GF"))
            .Deferable()
            .Sized(0x26, p => p.U16(0).U32((uint)d).U16(0))
            .Sized(0x26, p => p.U16(1).U32((uint)r).U16(0))
            .Sized(0x26, p => p.U16(2).U32((uint)g).U16(0))
            .Sized(0x26, p => p.U16(3).U32((uint)gf).U16(0))
            .Raw(content.ToArray())
            .FinishDictionary().ToArray();
        var dictionary = BamlReader.LoadDictionary(bytes);
        Assert.Equal(24.0, Assert.IsType<double>(dictionary["D"]));
        Assert.Equal(new CornerRadius(4), Assert.IsType<CornerRadius>(dictionary["R"]));
        var plain = Assert.IsType<PathGeometry>(dictionary["G"]);
        Assert.False(plain.IsFrozen);
        Assert.Single(plain.Figures);
        var frozen = Assert.IsType<PathGeometry>(dictionary["GF"]);
        Assert.True(frozen.IsFrozen);
        Assert.Single(frozen.Figures);
    }

    [Fact]
    public void Geometry_exponent_parses_like_upstream()
    {
        // Real theme data (BubbleTailGeometry) carries exponents the old
        // tokenizer read as a command letter; the value must survive exactly.
        var geometry = Assert.IsType<PathGeometry>(Geometry.Parse(
            "M0,0L28,0 28,14.0000006258488 2.0535663480814E-07,19.0279375612736z"));
        Assert.Equal(3, geometry.Figures[0].Segments.Count);
        var segment = Assert.IsType<LineSegment>(geometry.Figures[0].Segments[2]);
        Assert.Equal(2.0535663480814E-07, segment.Point.X, 15);
        Assert.Equal(19.0279375612736, segment.Point.Y, 10);
    }

    [Fact]
    public void Type_key_entry_fails_precise_and_static_key_entry_loads()
    {
        // Implicit-style type keys parse (index order and offsets validate)
        // but stay follow-ups; element-form x:Static keys now resolve - the
        // staged #93 shape (Double 30) lands under the engine singleton
        // (see Slice116BamlStaticKeyTests for the framed-member pins). The
        // unframed member text below exercises the DecodeStaticMember
        // pass-through branch.
        var typed = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1D, p => p.U16(0).U16(0).Str("DesktopWasm.Wpf.Button"))
            .Deferable()
            .Raw(0x27).I16(0).Raw(0x00).U32(0).Raw(0x00, 0x00)
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x04)
            .FinishDictionary().ToArray();
        var typedError = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(typed));
        Assert.Contains("implicit style", typedError.Message);
        Assert.Contains("DesktopWasm.Wpf.Button", typedError.Message);
        Assert.Contains("style-surface follow-up", typedError.Message);

        var keyed = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x20, p => p.U16(0).Str("SystemParameters.VerticalScrollBarButtonHeightKey"))
            .Deferable()
            .Raw(0x28).I16(-602).Raw(0x00).U32(0).Raw(0x00, 0x00)
            .Raw(0x2A)
            .Sized(0x10, p => p.Str("SystemParameters.VerticalScrollBarButtonHeightKey"))
            .Raw(0x2B, 0x29)
            .Raw(0x03).I16(-168).Raw(0x00)
            .Sized(0x11, p => p.Str("30").I16(-175))
            .Raw(0x04)
            .FinishDictionary().ToArray();
        var dictionary = BamlReader.LoadDictionary(keyed);
        Assert.Equal(30.0, Assert.IsType<double>(
            dictionary[SystemParameters.VerticalScrollBarButtonHeightKey]));
    }

    [Fact]
    public void Empty_style_with_target_loads()
    {
        // Slice-115: keyed Style entries now parse - a TargetType-only
        // entry (real fixed PropertyTypeReference framing) lands as an
        // empty live Style (see Slice115BamlPathStyleTests for setters).
        var content = new B();
        var s = content.Position;
        content.Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-442)
            .Raw(0x2E).I16(-238)
            .Raw(0x04);
        var bytes = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x20, p => p.U16(0).Str("EmptyPathStyle"))
            .Deferable()
            .Sized(0x26, p => p.U16(0).U32((uint)s).U16(0))
            .Raw(content.ToArray())
            .FinishDictionary().ToArray();
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary["EmptyPathStyle"]);
        Assert.Same(typeof(Path), style.TargetType);
        Assert.Empty(style.Setters);
    }

    [Fact]
    public void Keyless_entry_fails_loud()
    {
        var content = new B();
        content.Raw(0x03).I16(-81).Raw(0x00)
            .Sized(0x11, p => p.Str("#FF0000").I16(-87))
            .Raw(0x04);
        var bytes = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Deferable()
            .Raw(content.ToArray())
            .FinishDictionary().ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("keyless BAML dictionary entry", error.Message);
    }

    [Fact]
    public void Entry_offset_drift_fails_loud()
    {
        var content = new B();
        var first = content.Position;
        content.Raw(0x03).I16(-168).Raw(0x00)
            .Sized(0x11, p => p.Str("24").I16(-175))
            .Raw(0x04);
        var second = content.Position;
        content.Raw(0x03).I16(-168).Raw(0x00)
            .Sized(0x11, p => p.Str("20").I16(-175))
            .Raw(0x04);
        var bytes = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x20, p => p.U16(0).Str("A"))
            .Sized(0x20, p => p.U16(1).Str("B"))
            .Deferable()
            .Sized(0x26, p => p.U16(0).U32((uint)first).U16(0))
            .Sized(0x26, p => p.U16(1).U32((uint)(second + 1)).U16(0))
            .Raw(content.ToArray())
            .FinishDictionary().ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("points at", error.Message);
        Assert.Contains("'B'", error.Message);
    }

    [Fact]
    public void Duplicate_dictionary_key_fails_loud()
    {
        var content = new B();
        var first = content.Position;
        content.Raw(0x03).I16(-168).Raw(0x00)
            .Sized(0x11, p => p.Str("24").I16(-175))
            .Raw(0x04);
        var second = content.Position;
        content.Raw(0x03).I16(-168).Raw(0x00)
            .Sized(0x11, p => p.Str("20").I16(-175))
            .Raw(0x04);
        var bytes = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x20, p => p.U16(0).Str("K"))
            .Deferable()
            .Sized(0x26, p => p.U16(0).U32((uint)first).U16(0))
            .Sized(0x26, p => p.U16(0).U32((uint)second).U16(0))
            .Raw(content.ToArray())
            .FinishDictionary().ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("duplicate dictionary key 'K'", error.Message);
    }

    [Fact]
    public void Unclaimed_key_fails_at_close()
    {
        var bytes = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x20, p => p.U16(0).Str("K"))
            .Deferable()
            .Sized(0x26, p => p.U16(0).U32(0).U16(0))
            .FinishDictionary().ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("unclaimed key", error.Message);
        Assert.Contains("'K'", error.Message);
    }

    [Fact]
    public void Key_record_outside_index_fails_loud()
    {
        var bytes = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-739).Raw(0x00)
            .Sized(0x26, p => p.U16(0).U32(0).U16(0))
            .Raw(0x04, 0x02).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadRoot(bytes));
        Assert.Contains("outside a deferable dictionary index", error.Message);
    }

    [Fact]
    public void Static_value_outside_entry_fails_loud()
    {
        var bytes = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-739).Raw(0x00)
            .Raw(0x37, 0x00, 0x71, 0x00)
            .Raw(0x04, 0x02).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadRoot(bytes));
        Assert.Contains("static resource values need the style-surface follow-up", error.Message);
    }

    [Fact]
    public void Malformed_key_element_fails_loud()
    {
        // Two text runs inside one key element: not a single-text key.
        var content = new B();
        content.Raw(0x03).I16(-168).Raw(0x00)
            .Sized(0x11, p => p.Str("30").I16(-175))
            .Raw(0x04);
        var bytes = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x20, p => p.U16(0).Str("Member.One"))
            .Sized(0x20, p => p.U16(1).Str("Member.Two"))
            .Deferable()
            .Raw(0x28).I16(-602).Raw(0x00).U32(0).Raw(0x00, 0x00)
            .Raw(0x2A)
            .Sized(0x10, p => p.Str("Member.One"))
            .Sized(0x10, p => p.Str("Member.Two"))
            .Raw(0x2B, 0x29)
            .Raw(content.ToArray())
            .FinishDictionary().ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("single-text keys", error.Message);
    }

    [Fact]
    public void MenuScrollingVisibilityConverter_matches_upstream()
    {
        var converter = new MenuScrollingVisibilityConverter();
        var culture = CultureInfo.InvariantCulture;
        // Malformed inputs yield UnsetValue (mirrors the upstream guards).
        Assert.Same(DependencyProperty.UnsetValue,
            converter.Convert(null, typeof(Visibility), "0", culture));
        Assert.Same(DependencyProperty.UnsetValue,
            converter.Convert(new object?[] { Visibility.Visible }, typeof(Visibility), "0", culture));
        Assert.Same(DependencyProperty.UnsetValue,
            converter.Convert(new object?[] { Visibility.Visible, 0.0, 100.0, 10.0 }, typeof(Visibility), null, culture));
        Assert.Same(DependencyProperty.UnsetValue,
            converter.Convert(new object?[] { Visibility.Visible, 0.0, 100.0, 10.0 }, typeof(Visibility), 7, culture));
        Assert.Same(DependencyProperty.UnsetValue,
            converter.Convert(new object?[] { "Visible", 0.0, 100.0, 10.0 }, typeof(Visibility), "0", culture));
        // Hidden scrollbar collapses the buttons.
        Assert.Equal(Visibility.Collapsed,
            converter.Convert(new object?[] { Visibility.Hidden, 50.0, 100.0, 10.0 }, typeof(Visibility), "0", culture));
        // At the target percent the button hides; elsewhere it shows.
        Assert.Equal(Visibility.Collapsed,
            converter.Convert(new object?[] { Visibility.Visible, 0.0, 100.0, 10.0 }, typeof(Visibility), "0", culture));
        Assert.Equal(Visibility.Visible,
            converter.Convert(new object?[] { Visibility.Visible, 45.0, 100.0, 10.0 }, typeof(Visibility), "0", culture));
        Assert.Equal(Visibility.Collapsed,
            converter.Convert(new object?[] { Visibility.Visible, 90.0, 100.0, 10.0 }, typeof(Visibility), "100", culture));
        // Degenerate range (extent == viewport) never divides: stays visible.
        Assert.Equal(Visibility.Visible,
            converter.Convert(new object?[] { Visibility.Visible, 0.0, 10.0, 10.0 }, typeof(Visibility), "0", culture));
        // A double parameter behaves like its string form.
        Assert.Equal(Visibility.Collapsed,
            converter.Convert(new object?[] { Visibility.Visible, 0.0, 100.0, 10.0 }, typeof(Visibility), 0.0, culture));
        // ConvertBack yields DoNothing (mirrors upstream).
        var back = converter.ConvertBack(Visibility.Visible, new[] { typeof(object) }, null, culture);
        Assert.Single(back);
        Assert.Same(Binding.DoNothing, back[0]);
    }

    private sealed class B
    {
        private readonly List<byte> _b = new();
        private int _deferPos = -1;
        public int Position => _b.Count;
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

