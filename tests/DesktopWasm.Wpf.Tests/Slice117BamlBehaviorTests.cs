// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-117 HandyControl theme.baml converted Behavior run.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// entries #94-99 land as live converted FluidMoveBehavior values
/// (AppliesTo/Duration simple properties plus empty PowerEase EaseX/EaseY
/// complex children, keyed x:Shared="False" so every lookup materializes a
/// fresh instance) and the run stops precise at the implicit style entry
/// #100 (BlendEffectBox). StaticResource/markup-extension values, richer
/// complex properties and the rest stay pinned.
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice117BamlBehaviorTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice117BamlBehaviorTests()
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
    public void Theme_baml_resolves_behaviors_then_stops_at_implicit_style()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(theme));
        // Entries #87-103 materialize in index order with validated offsets
        // (the entry machine consumes keys strictly in order, so any earlier
        // failure would throw its own offset instead).
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
    public void Staged_boot_advances_past_behaviors_to_implicit_style_stop()
    {
        var entry = LoadStagedHandyControlEntry();
        var error = Assert.Throws<InvalidOperationException>(() => WpfAppBoot.Boot(entry));
        Assert.Contains("startup failed in entry point", error.Message);
        var inner = Assert.IsType<XamlLoadException>(error.InnerException);
        // The line-17 SkinDefault stop, the line-23 Theme key/index +
        // scalar run, PathBaseStyle, the x:Static-keyed Double, the six
        // converted Behavior entries, the implicit BlendEffectBox style, the
        // 'BorderCircular' entry, the implicit Col/ContentControl styles,
        // the FlipClock style, the FloatingBlock style and the GridSplitter
        // style now resolve; boot fails at the implicit Notification style
        // entry #107 - the precise next blocker.
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
    public void Converted_behavior_XY200_materializes_with_easing()
    {
        // The staged #94 entry shape (FluidMoveBehavior with AppliesTo +
        // Duration PropertyWithConverter content and two empty PowerEase
        // complex children) lands as a live converted value.
        LoadStagedHandyControlEntry();
        var bytes = BehaviorDictionary(
            [("BehaviorXY200", 0, 0, XyBehaviorContent("0:0:.2"))],
            BehaviorTypes(), BehaviorAttrs());
        var dictionary = BamlReader.LoadDictionary(bytes);
        Assert.Equal(1, dictionary.EntryCount());
        var value = dictionary["BehaviorXY200"];
        Assert.NotNull(value);
        Assert.Equal("HandyControl.Interactivity.FluidMoveBehavior", value.GetType().FullName);
        Assert.Equal("Children", value.GetType().GetProperty("AppliesTo")!.GetValue(value)!.ToString());
        var duration = Assert.IsType<Duration>(value.GetType().GetProperty("Duration")!.GetValue(value));
        Assert.Equal(TimeSpan.FromSeconds(0.2), duration.TimeSpan);
        var easeX = Assert.IsType<PowerEase>(value.GetType().GetProperty("EaseX")!.GetValue(value));
        Assert.Equal(2.0, easeX.Power);
        var easeY = Assert.IsType<PowerEase>(value.GetType().GetProperty("EaseY")!.GetValue(value));
        Assert.Equal(2.0, easeY.Power);
        Assert.Same(value, dictionary["BehaviorXY200"]);
    }

    [Fact]
    public void Converted_behavior_X400_materializes_duration_and_easex_only()
    {
        // The staged #98 entry shape (Duration "0:0:.4", EaseX only):
        // EaseY stays null, proving each complex child maps by attribute.
        LoadStagedHandyControlEntry();
        var content = new B().Raw(0x03).I16(0).Raw(0x00)
            .Sized(0x24, q => q.I16(0).Str("Children").I16(-195))
            .Sized(0x24, q => q.I16(1).Str("0:0:.4").I16(-188))
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(1).Raw(0x00).Raw(0x04).Raw(0x08)
            .Raw(0x04);
        var bytes = BehaviorDictionary(
            [("BehaviorX400", 0, 0, content)],
            BehaviorTypes(), BehaviorAttrs());
        var dictionary = BamlReader.LoadDictionary(bytes);
        var value = dictionary["BehaviorX400"];
        Assert.NotNull(value);
        Assert.Equal("HandyControl.Interactivity.FluidMoveBehavior", value.GetType().FullName);
        var duration = Assert.IsType<Duration>(value.GetType().GetProperty("Duration")!.GetValue(value));
        Assert.Equal(TimeSpan.FromSeconds(0.4), duration.TimeSpan);
        Assert.IsType<PowerEase>(value.GetType().GetProperty("EaseX")!.GetValue(value));
        Assert.Null(value.GetType().GetProperty("EaseY")!.GetValue(value));
    }

    [Fact]
    public void Non_shared_converted_entry_materializes_fresh_instances()
    {
        // The staged #94-99 key shape (shared=0, sharedSet=1): every lookup
        // materializes a fresh live value with identical state.
        LoadStagedHandyControlEntry();
        var bytes = BehaviorDictionary(
            [("BehaviorXY200", 0, 1, XyBehaviorContent("0:0:.2"))],
            BehaviorTypes(), BehaviorAttrs());
        var dictionary = BamlReader.LoadDictionary(bytes);
        Assert.Equal(1, dictionary.EntryCount());
        Assert.Equal("BehaviorXY200", dictionary.Keys.Cast<object>().Single());
        var first = dictionary["BehaviorXY200"];
        var second = dictionary["BehaviorXY200"];
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal("Children", first.GetType().GetProperty("AppliesTo")!.GetValue(first)!.ToString());
        Assert.Equal("Children", second.GetType().GetProperty("AppliesTo")!.GetValue(second)!.ToString());
        Assert.Equal(
            ((Duration)first.GetType().GetProperty("Duration")!.GetValue(first)!).TimeSpan,
            ((Duration)second.GetType().GetProperty("Duration")!.GetValue(second)!).TimeSpan);
        Assert.IsType<PowerEase>(first.GetType().GetProperty("EaseX")!.GetValue(first));
        Assert.IsType<PowerEase>(second.GetType().GetProperty("EaseX")!.GetValue(second));
    }

    [Fact]
    public void Converted_empty_non_shared_entry_materializes_fresh()
    {
        // Empty converted elements (the converter/provider shape) honor the
        // shared flag too: the empty-instantiation path defers like content.
        LoadStagedHandyControlEntry();
        var content = new B().Raw(0x03).I16(0).Raw(0x00).Raw(0x04);
        var bytes = BehaviorDictionary(
            [("BehaviorEmpty", 0, 1, content)],
            BehaviorTypes(), BehaviorAttrs());
        var dictionary = BamlReader.LoadDictionary(bytes);
        var first = dictionary["BehaviorEmpty"];
        var second = dictionary["BehaviorEmpty"];
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal("HandyControl.Interactivity.FluidMoveBehavior", first.GetType().FullName);
    }

    [Fact]
    public void Duplicate_non_shared_key_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var bytes = BehaviorDictionary(
            [("BehaviorXY200", 0, 1, XyBehaviorContent("0:0:.2")),
             ("BehaviorXY200", 0, 1, XyBehaviorContent("0:0:.2"))],
            BehaviorTypes(), BehaviorAttrs());
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("duplicate dictionary key", error.Message);
        Assert.Contains("'BehaviorXY200'", error.Message);
    }

    [Fact]
    public void Converted_unknown_type_id_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var retyped = RetypedEntry(XyBehaviorContent("0:0:.2"), 7);
        var bytes = BehaviorDictionary(
            [("K", 0, 0, retyped)],
            BehaviorTypes(), BehaviorAttrs());
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("unknown type id 7", error.Message);
        Assert.Contains("'K'", error.Message);
    }

    [Fact]
    public void Converted_unloadable_type_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var content = new B().Raw(0x03).I16(0).Raw(0x00).Raw(0x04);
        var bytes = BehaviorDictionary(
            [("K", 0, 0, content)],
            [("Nope.MissingWidget", (ushort)0)], BehaviorAttrs());
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("Nope.MissingWidget", error.Message);
        Assert.Contains("no loadable converted type", error.Message);
    }

    [Fact]
    public void Converted_unknown_property_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var content = new B().Raw(0x03).I16(0).Raw(0x00)
            .Sized(0x24, q => q.I16(4).Str("x").I16(-615));
        var bytes = BehaviorDictionary(
            [("K", 0, 0, content)],
            BehaviorTypes(),
            [((ushort)0, (short)0, "AppliesTo"), ((ushort)4, (short)0, "Nope")]);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("'Nope'", error.Message);
        Assert.Contains("not a settable CLR property", error.Message);
    }

    [Fact]
    public void Converted_unknown_attribute_id_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var content = new B().Raw(0x03).I16(0).Raw(0x00)
            .Sized(0x24, q => q.I16(9).Str("Children").I16(-195));
        var bytes = BehaviorDictionary(
            [("K", 0, 0, content)],
            BehaviorTypes(), BehaviorAttrs());
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("no AttributeInfo record", error.Message);
    }

    [Fact]
    public void Converted_bad_enum_value_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var content = new B().Raw(0x03).I16(0).Raw(0x00)
            .Sized(0x24, q => q.I16(0).Str("Childrenn").I16(-195));
        var bytes = BehaviorDictionary(
            [("K", 0, 0, content)],
            BehaviorTypes(), BehaviorAttrs());
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("'AppliesTo'", error.Message);
        Assert.Contains("'Childrenn'", error.Message);
        Assert.Contains("does not convert", error.Message);
    }

    [Fact]
    public void Converted_bad_duration_text_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var content = new B().Raw(0x03).I16(0).Raw(0x00)
            .Sized(0x24, q => q.I16(1).Str("not-a-duration").I16(-188));
        var bytes = BehaviorDictionary(
            [("K", 0, 0, content)],
            BehaviorTypes(), BehaviorAttrs());
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("'Duration'", error.Message);
        Assert.Contains("does not convert", error.Message);
    }

    [Fact]
    public void Converted_wrong_converter_for_type_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var content = new B().Raw(0x03).I16(0).Raw(0x00)
            .Sized(0x24, q => q.I16(0).Str("Children").I16(-615));
        var bytes = BehaviorDictionary(
            [("K", 0, 0, content)],
            BehaviorTypes(), BehaviorAttrs());
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("'AppliesTo'", error.Message);
        Assert.Contains("does not convert", error.Message);
    }

    [Fact]
    public void Converted_non_empty_complex_child_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var content = new B().Raw(0x03).I16(0).Raw(0x00)
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(1).Raw(0x00)
            .Sized(0x24, q => q.I16(0).Str("x").I16(-615))
            .Raw(0x04).Raw(0x08)
            .Raw(0x04);
        var bytes = BehaviorDictionary(
            [("K", 0, 0, content)],
            BehaviorTypes(), BehaviorAttrs());
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("empty elements", error.Message);
        Assert.Contains("'EaseX'", error.Message);
    }

    [Fact]
    public void Converted_non_powerEase_complex_child_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var content = new B().Raw(0x03).I16(0).Raw(0x00)
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(0).Raw(0x00).Raw(0x04).Raw(0x08)
            .Raw(0x04);
        var bytes = BehaviorDictionary(
            [("K", 0, 0, content)],
            BehaviorTypes(), BehaviorAttrs());
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("FluidMoveBehavior", error.Message);
        Assert.Contains("only empty PowerEase children land", error.Message);
    }

    [Fact]
    public void Converted_static_and_markup_values_fail_precise()
    {
        LoadStagedHandyControlEntry();
        var staticContent = new B().Raw(0x03).I16(0).Raw(0x00)
            .Raw(0x38, 0x00, 0x00, 0x00);
        var staticBytes = BehaviorDictionary(
            [("K", 0, 0, staticContent)],
            BehaviorTypes(), BehaviorAttrs());
        var staticError = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(staticBytes));
        Assert.Contains("StaticResource value", staticError.Message);

        var markupContent = new B().Raw(0x03).I16(0).Raw(0x00)
            .Sized(0x23, q => q.I16(0).U16(189));
        var markupBytes = BehaviorDictionary(
            [("K", 0, 0, markupContent)],
            BehaviorTypes(), BehaviorAttrs());
        var markupError = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(markupBytes));
        Assert.Contains("markup-extension value", markupError.Message);
    }

    [Fact]
    public void Converted_content_property_and_type_reference_fail_precise()
    {
        LoadStagedHandyControlEntry();
        var contentProp = new B().Raw(0x03).I16(0).Raw(0x00)
            .Raw(0x2E).I16(2);
        var contentBytes = BehaviorDictionary(
            [("K", 0, 0, contentProp)],
            BehaviorTypes(), BehaviorAttrs());
        var contentError = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(contentBytes));
        Assert.Contains("ContentProperty", contentError.Message);

        var typeRef = new B().Raw(0x03).I16(0).Raw(0x00)
            .Raw(0x22).I16(0).I16(-442);
        var typeBytes = BehaviorDictionary(
            [("K", 0, 0, typeRef)],
            BehaviorTypes(), BehaviorAttrs());
        var typeError = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(typeBytes));
        Assert.Contains("PropertyTypeReference", typeError.Message);

        var optimized = new B().Raw(0x03).I16(0).Raw(0x00)
            .Raw(0x37, 0x00, 0x00, 0x00);
        var optimizedBytes = BehaviorDictionary(
            [("K", 0, 0, optimized)],
            BehaviorTypes(), BehaviorAttrs());
        var optimizedError = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(optimizedBytes));
        Assert.Contains("optimized StaticResource value", optimizedError.Message);
    }

    [Fact]
    public void Converted_first_record_boundary_fails_precise()
    {
        LoadStagedHandyControlEntry();
        var content = new B().Raw(0x03).I16(0).Raw(0x00)
            .Sized(0x10, q => q.Str("stray"));
        var bytes = BehaviorDictionary(
            [("K", 0, 0, content)],
            BehaviorTypes(), BehaviorAttrs());
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("FluidMoveBehavior", error.Message);
        Assert.Contains("converted-element values are a follow-up", error.Message);
    }

    [Fact]
    public void Fake_converted_widget_covers_all_simple_branches()
    {
        XamlLoader.CustomElementSource = typeof(FakeConvertedWidget).Assembly;
        var content = new B().Raw(0x03).I16(0).Raw(0x00)
            .Sized(0x24, q => q.I16(0).Str("hello").I16(-615))
            .Sized(0x24, q => q.I16(1).Str("True").I16(-46))
            .Sized(0x24, q => q.I16(2).Str("0.5").I16(-175))
            .Sized(0x24, q => q.I16(3).Str("Second").I16(-195))
            .Sized(0x24, q => q.I16(4).Str("0:0:1").I16(-188))
            .Raw(0x07).I16(5)
            .Raw(0x03).I16(1).Raw(0x00).Raw(0x04).Raw(0x08)
            .Raw(0x04);
        var bytes = BehaviorDictionary(
            [("Widget", 0, 0, content)],
            [("DesktopWasm.Wpf.Tests.FakeConvertedWidget", (ushort)0),
             ("System.Windows.Media.Animation.PowerEase", (ushort)1)],
            [((ushort)0, (short)0, "Title"),
             ((ushort)1, (short)0, "Enabled"),
             ((ushort)2, (short)0, "Ratio"),
             ((ushort)3, (short)0, "Kind"),
             ((ushort)4, (short)0, "Span"),
             ((ushort)5, (short)0, "Ease")]);
        var dictionary = BamlReader.LoadDictionary(bytes);
        var widget = Assert.IsType<FakeConvertedWidget>(dictionary["Widget"]);
        Assert.Equal("hello", widget.Title);
        Assert.True(widget.Enabled);
        Assert.Equal(0.5, widget.Ratio);
        Assert.Equal(FakeConvertedKind.Second, widget.Kind);
        Assert.Equal(TimeSpan.FromSeconds(1), widget.Span.TimeSpan);
        Assert.IsType<PowerEase>(widget.Ease);
    }

    private static B RetypedEntry(B content, short typeId)
    {
        var raw = content.ToArray();
        Assert.Equal((byte)0x03, raw[0]);
        var patched = (byte[])raw.Clone();
        Array.Copy(BitConverter.GetBytes(typeId), 0, patched, 1, 2);
        var b = new B();
        return b.Raw(patched);
    }

    private static B XyBehaviorContent(string duration) =>
        new B().Raw(0x03).I16(0).Raw(0x00)
            .Sized(0x24, q => q.I16(0).Str("Children").I16(-195))
            .Sized(0x24, q => q.I16(1).Str(duration).I16(-188))
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(1).Raw(0x00).Raw(0x04).Raw(0x08)
            .Raw(0x07).I16(3)
            .Raw(0x03).I16(1).Raw(0x00).Raw(0x04).Raw(0x08)
            .Raw(0x04);

    private static (string FullName, ushort Id)[] BehaviorTypes() =>
        [("HandyControl.Interactivity.FluidMoveBehavior", (ushort)0),
         ("System.Windows.Media.Animation.PowerEase", (ushort)1)];

    private static (ushort Id, short Owner, string Name)[] BehaviorAttrs() =>
        [((ushort)0, (short)28, "AppliesTo"),
         ((ushort)1, (short)27, "Duration"),
         ((ushort)2, (short)27, "EaseX"),
         ((ushort)3, (short)27, "EaseY")];

    private static byte[] BehaviorDictionary(
        (string Key, byte Shared, byte SharedSet, B Content)[] entries,
        (string FullName, ushort Id)[] types,
        (ushort Id, short Owner, string Name)[] attrs)
    {
        var content = new B();
        var positions = new List<int>();
        foreach (var (_, _, _, body) in entries)
        {
            positions.Add(content.Position);
            content.Raw(body.ToArray());
        }
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"));
        foreach (var (fullName, id) in types)
            head.Sized(0x1D, p => p.U16(id).U16(0).Str(fullName));
        foreach (var (id, owner, name) in attrs)
            head.Sized(0x1F, p => p.I16((short)id).I16(owner).Raw(0x00).Str(name));
        head.Sized(0x20, p => p.U16(0).Str("unused"));
        for (ushort i = 0; i < entries.Length; i++)
            head.Sized(0x20, p => p.U16((ushort)(i + 1)).Str(entries[i].Key));
        head.Deferable();
        for (var i = 0; i < entries.Length; i++)
            head.Raw(0x26).Seven(9).U16((ushort)(i + 1)).U32((uint)positions[i])
                .Raw(entries[i].Shared, entries[i].SharedSet);
        return head.Raw(content.ToArray()).FinishDictionary().ToArray();
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
}

/// <summary>Synthetic converted element covering every simple-conversion
/// branch (string/bool/double/enum/Duration plus a PowerEase complex child).
/// Lives in the test assembly so the loader resolves it as a converted type
/// without any staged dependency.</summary>
public sealed class FakeConvertedWidget
{
    public string Title { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public double Ratio { get; set; }
    public FakeConvertedKind Kind { get; set; }
    public Duration Span { get; set; } = Duration.Automatic;
    public IEasingFunction? Ease { get; set; }
}

/// <summary>Enum domain for the synthetic converted widget.</summary>
public enum FakeConvertedKind
{
    None,
    First,
    Second,
}
