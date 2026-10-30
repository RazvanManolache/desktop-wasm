// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-115 HandyControl theme.baml first-style + scalar-tail surface.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// keyed entry #87 'PathBaseStyle' (Style with TargetType Path plus three
/// simple setters) plus the scalar tail #88-92 (Double/Thickness/
/// CornerRadius, incl. the new Thickness entry form) is coherent in one
/// slice: every record in those entries frames byte-exact and the values
/// land as live engine objects with real WPF behavior. Entry #93 (the
/// x:Static-keyed Double) stays a precise pinned follow-up with the
/// implicit styles, converted Behavior entries and named
/// styles/templates behind it.
///
/// Setter.Property travels as PropertyCustom: plain serializer 137 carries
/// a well-known member id (FlowDirection = -46, verified against the
/// upstream PathBaseStyle XAML plus a second staged RightToLeft style and
/// live net10 ref-assembly IL), 0x4000-flagged 16521 carries an owner type
/// id plus the property name (Shape = -558 for Stretch,
/// UIElement = -470 for SnapsToDevicePixels, both declaring-type-verified
/// against the net10 ref assemblies).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice115BamlPathStyleTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice115BamlPathStyleTests()
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
    public void Theme_baml_resolves_path_style_then_stops_at_xstatic()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(theme));
        // The slice-119 stop (BAML+0x16954 implicit Col style entry) is
        // gone: reaching BAML+0x16E7C proves entries #87-107 plus the RectangleCircular/SnapRect/TargetArea entries materialized in
        // index order with validated offsets (the entry machine consumes
        // keys strictly in order, so any earlier failure would throw its
        // own offset instead).
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
    public void Staged_boot_advances_past_path_style_to_xstatic_stop()
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
    public void Path_style_shape_materializes_with_three_setters()
    {
        // The staged PathBaseStyle entry shape with real framing (fixed
        // PropertyTypeReference, ContentProperty, PropertyCustom 137/16521
        // plus PropertyWithConverter values) lands as a live Style.
        var bytes = StyleDictionary([
            ("PathBaseStyle", new B().Raw(0x03).I16(-620).Raw(0x00)
                .Raw(0x22).I16(0).I16(-442)
                .Raw(0x2E).I16(-238)
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, p => p.I16(1).U16(16521).I16(-558).Str("Stretch"))
                .Sized(0x24, p => p.I16(2).Str("Uniform").I16(-195))
                .Raw(0x04)
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, p => p.I16(1).U16(16521).I16(-694).Str("SnapsToDevicePixels"))
                .Sized(0x24, p => p.I16(2).Str("True").I16(-46))
                .Raw(0x04)
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, p => p.I16(1).U16(137).I16(-46))
                .Sized(0x24, p => p.I16(2).Str("LeftToRight").I16(-195))
                .Raw(0x04)
                .Raw(0x04))]);
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary["PathBaseStyle"]);
        Assert.Same(typeof(Path), style.TargetType);
        Assert.Equal(3, style.Setters.Count);
        var stretch = Assert.IsType<Setter>(style.Setters[0]);
        Assert.Same(Shape.StretchProperty, stretch.Property);
        Assert.Equal(Stretch.Uniform, stretch.Value);
        var snaps = Assert.IsType<Setter>(style.Setters[1]);
        Assert.Same(UIElement.SnapsToDevicePixelsProperty, snaps.Property);
        Assert.Equal(true, snaps.Value);
        var flow = Assert.IsType<Setter>(style.Setters[2]);
        Assert.Same(FrameworkElement.FlowDirectionProperty, flow.Property);
        Assert.Equal(FlowDirection.LeftToRight, flow.Value);
    }

    [Fact]
    public void Path_style_applies_to_a_live_path()
    {
        // The materialized style is live, not retained bytes: applying it
        // to a real Path lands all three values through the CLR mirrors.
        var bytes = StyleDictionary([
            ("S", new B().Raw(0x03).I16(-620).Raw(0x00)
                .Raw(0x22).I16(0).I16(-442)
                .Raw(0x2E).I16(-238)
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, p => p.I16(1).U16(16521).I16(-558).Str("Stretch"))
                .Sized(0x24, p => p.I16(2).Str("Uniform").I16(-195))
                .Raw(0x04)
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, p => p.I16(1).U16(16521).I16(-694).Str("SnapsToDevicePixels"))
                .Sized(0x24, p => p.I16(2).Str("True").I16(-46))
                .Raw(0x04)
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, p => p.I16(1).U16(137).I16(-46))
                .Sized(0x24, p => p.I16(2).Str("LeftToRight").I16(-195))
                .Raw(0x04)
                .Raw(0x04))]);
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary["S"]);
        var path = new Path();
        Assert.Equal(Stretch.None, path.Stretch);
        style.ApplyTo(path);
        Assert.Equal(Stretch.Uniform, path.Stretch);
        Assert.True(path.SnapsToDevicePixels);
        Assert.Equal(FlowDirection.LeftToRight, path.FlowDirection);
    }

    [Fact]
    public void Thickness_scalar_entry_parses()
    {
        // The staged DefaultControlPadding/DefaultInputPadding entry form
        // (type -651 with converter -655) lands as a Thickness.
        var content = new B();
        var p = content.Position;
        content.Raw(0x03).I16(-651).Raw(0x00)
            .Sized(0x11, q => q.Str("10,5").I16(-655))
            .Raw(0x04);
        var bytes = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x20, q => q.U16(0).Str("P"))
            .Deferable()
            .Sized(0x26, q => q.U16(0).U32((uint)p).U16(0))
            .Raw(content.ToArray())
            .FinishDictionary().ToArray();
        var dictionary = BamlReader.LoadDictionary(bytes);
        Assert.Equal(new Thickness(10, 5, 10, 5), Assert.IsType<Thickness>(dictionary["P"]));
    }

    [Fact]
    public void Qualified_engine_type_owner_resolves()
    {
        // A per-file TypeInfo owner naming an engine type resolves through
        // the CLR name (the staged file uses this for framework owners
        // that travel by name, e.g. DataGrid-length columns).
        var bytes = StyleDictionary([
            ("S", new B().Raw(0x03).I16(-620).Raw(0x00)
                .Raw(0x22).I16(0).I16(-442)
                .Raw(0x2E).I16(-238)
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, q => q.I16(1).U16(16521).I16(0).Str("Stretch"))
                .Sized(0x24, q => q.I16(2).Str("Fill").I16(-195))
                .Raw(0x04)
                .Raw(0x04))],
            [("System.Windows.Shapes.Shape", (ushort)0)]);
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary["S"]);
        var setter = Assert.IsType<Setter>(Assert.Single(style.Setters));
        Assert.Same(Shape.StretchProperty, setter.Property);
        Assert.Equal(Stretch.Fill, setter.Value);
    }

    [Fact]
    public void Converted_owner_lands_attached_dp()
    {
        // Slice-119 rewrite: a per-file TypeInfo owner naming a converted
        // HandyControl type now resolves OwnerType + PropertyName against
        // the staged converted lib (see Slice119BamlAttachedOwnerTests for
        // the staged #101 entry); BorderElement.CornerRadius lands as a
        // live attached setter with the converted CornerRadius value.
        // Unloadable owners keep the converted-type precise boundary
        // (pinned there).
        LoadStagedHandyControlEntry();
        var bytes = StyleDictionary([
            ("S", new B().Raw(0x03).I16(-620).Raw(0x00)
                .Raw(0x22).I16(0).I16(-442)
                .Raw(0x2E).I16(-238)
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, q => q.I16(1).U16(16521).I16(0).Str("CornerRadius"))
                .Sized(0x24, q => q.I16(2).Str("4").I16(-111))
                .Raw(0x04)
                .Raw(0x04))],
            [("HandyControl.Controls.BorderElement", (ushort)0)]);
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary["S"]);
        var setter = Assert.IsType<Setter>(Assert.Single(style.Setters));
        var lib = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var owner = lib.GetType("HandyControl.Controls.BorderElement");
        Assert.NotNull(owner);
        Assert.Same(owner!.GetField("CornerRadiusProperty")!.GetValue(null), setter.Property);
        Assert.True(setter.Property.IsAttached);
        Assert.Equal(new CornerRadius(4), setter.Value);
    }

    [Fact]
    public void Unknown_style_target_fails_precise()
    {
        // Slice-122 rewrite: -739 names Window (a real net10 fixture
        // compiles a Window-targeted style to it, and the staged #107
        // owner travels as -739 too), but Window targets stay a follow-up,
        // so the unknown-target pin moves to an id with no mapping.
        var bytes = StyleDictionary([
            ("S", new B().Raw(0x03).I16(-620).Raw(0x00)
                .Raw(0x22).I16(0).I16(-777)
                .Raw(0x04))]);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("well-known type id -777", error.Message);
        Assert.Contains("style targets beyond Path/Border/ContentControl/GridSplitter/Rectangle/RepeatButton/GroupBox/Thumb", error.Message);
    }

    [Fact]
    public void Short_form_template_resolves_against_style_target()
    {
        // Short-form (137) Template (member -37, the staged BlendEffectBox
        // shape) resolves against the style TargetType like WPF: a Path
        // target declares no Template property and fails naming both (see
        // Slice118BamlBlendEffectTests for the landing Control-target shape).
        var bytes = StyleDictionary([
            ("S", new B().Raw(0x03).I16(-620).Raw(0x00)
                .Raw(0x22).I16(0).I16(-442)
                .Raw(0x2E).I16(-238)
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, q => q.I16(1).U16(137).I16(-37))
                .Sized(0x24, q => q.I16(2).Str("Uniform").I16(-195))
                .Raw(0x04)
                .Raw(0x04))]);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("'Template' is not a dependency property of Path", error.Message);
    }

    [Fact]
    public void StaticResource_setter_value_wrong_attribute_stays_precise()
    {
        // Setter StaticResource values (0x38) route through the engine
        // deferred RawValue path since slice-160; a static over any attribute
        // but the Setter.Value row (here the Style.TargetType row) stays a
        // precise follow-up naming the attribute.
        var bytes = StyleDictionary([
            ("S", new B().Raw(0x03).I16(-620).Raw(0x00)
                .Raw(0x22).I16(0).I16(-442)
                .Raw(0x2E).I16(-238)
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, q => q.I16(1).U16(137).I16(-46))
                .Raw(0x38).U16(0).U16(1)
                .Raw(0x04)
                .Raw(0x04))]);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("attribute 'TargetType'", error.Message);
        Assert.Contains("styles take Value", error.Message);
    }

    [Fact]
    public void Complex_setter_value_fails_precise()
    {
        // Setter values as complex content (0x07, e.g. ControlTemplate)
        // stay follow-ups with the setter named.
        var bytes = StyleDictionary([
            ("S", new B().Raw(0x03).I16(-620).Raw(0x00)
                .Raw(0x22).I16(0).I16(-442)
                .Raw(0x2E).I16(-238)
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, q => q.I16(1).U16(137).I16(-46))
                .Raw(0x07).I16(2)
                .Raw(0x04)
                .Raw(0x04))]);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("complex value", error.Message);
        Assert.Contains("style-surface follow-up", error.Message);
    }

    [Fact]
    public void Xstatic_member_decode_strips_length_framing()
    {
        // Real element-form x:Static keys store the member as a U8 name
        // length, the "Type.Member" text and a '+' terminator (byte-verified
        // on all 11 staged key elements); the resolution error shows the
        // decoded member, not the framing bytes.
        var content = new B();
        content.Raw(0x03).I16(-168).Raw(0x00)
            .Sized(0x11, q => q.Str("30").I16(-175))
            .Raw(0x04);
        var member = "\u0003A.B+";
        var bytes = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Deferable()
            .Raw(0x28).I16(-602).Raw(0x00).U32(0).Raw(0x00, 0x00)
            .Raw(0x2A)
            .Sized(0x10, q => q.Str(member))
            .Raw(0x2B, 0x29)
            .Raw(content.ToArray())
            .FinishDictionary().ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("{x:Static A.B}", error.Message);
        Assert.Contains("unsupported x:Static type 'A'", error.Message);
    }

    /// <summary>Builds a single-deferable dictionary with the Style
    /// AttributeInfo rows (TargetType/Property/Value) plus optional
    /// TypeInfo rows. Entry offsets are content-relative like the staged
    /// file (the reader validates them against the defer positions).</summary>
    private static byte[] StyleDictionary(
        (string Key, B Content)[] entries, (string FullName, ushort Id)[]? types = null)
    {
        var content = new B();
        var positions = new List<int>();
        foreach (var (_, body) in entries)
        {
            positions.Add(content.Position);
            content.Raw(body.ToArray());
        }
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"));
        if (types is not null)
            foreach (var (fullName, id) in types)
                head.Sized(0x1D, p => p.U16(id).U16(0).Str(fullName));
        for (var i = 0; i < entries.Length; i++)
            head.Sized(0x20, p => p.U16((ushort)i).Str(entries[i].Key));
        head.Deferable();
        for (var i = 0; i < entries.Length; i++)
            head.Sized(0x26, p => p.U16((ushort)i).U32((uint)positions[i]).U16(0));
        return head.Raw(content.ToArray()).FinishDictionary().ToArray();
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
