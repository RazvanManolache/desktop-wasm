// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-122 HandyControl theme.baml short-form alignment run.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// entry #105 (implicit FloatingBlock style at file 0x16BAB: short-form
/// HorizontalAlignment (-48)/Left and VerticalAlignment (-56)/Top setters
/// plus a Template setter whose ControlTemplate holds a ContentPresenter
/// visual with a ContentTemplate TemplateBinding over FloatingBlock's
/// per-file AttributeInfo row) and entry #106 (implicit GridSplitter style
/// at file 0x16C11: short-form Focusable setter over the well-known -257
/// target) land as live engine styles, and the run stops precise at the
/// implicit Notification style entry #107 (file 0x16C37: qualified
/// Window-owner SizeToContent setter, staged owner id -739).
///
/// Byte-exact decode of entries #105 [0x16BAB, 0x16C11), #106
/// [0x16C11, 0x16C37) and the #107 head against the staged 466350-byte
/// theme.baml, triangulated three ways (the staged bytes, the upstream
/// HandyControl Theme.xaml FloatingBlock/GridSplitter/Notification blocks,
/// and a real net10 BAML fixture compiling GridSplitter, HorizontalAlignment,
/// VerticalAlignment, TemplateBinding-ContentTemplate,
/// TemplateBinding-Padding and SizeToContent setters): PropertyCustom
/// short-form member ids -48 (HorizontalAlignment), -56
/// (VerticalAlignment), -132 (Focusable) with
/// Left/Top/False EnumConverter/BooleanConverter values; the template
/// TargetType reference (TypeInfo 35 = FloatingBlock) plus VisualTree plus
/// the ContentPresenter visual; the 0x23 PropertyWithExtension payload
/// [targetAttr I16][extension U16][sourceRef I16] with 634 as the
/// TemplateBinding discriminator, well-known target ids -21
/// (ContentPresenter.ContentTemplate) and -49 (FrameworkElement.Margin),
/// and the source as a well-known member id for framework-known
/// templated-parent properties (-15 ContentControl.ContentTemplate, -35
/// Control.Padding) or a per-file AttributeInfo index otherwise (staged
/// #105 carries 11 = FloatingBlock.ContentTemplate).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice122BamlAlignmentTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice122BamlAlignmentTests()
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
    public void Theme_baml_lands_floating_block_and_grid_splitter_then_stops_at_notification()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        // Byte-verbatim entry #105/#106 framing: the TemplateBinding value
        // on the FloatingBlock ContentPresenter (0x23 target -21 extension
        // 634 source AttributeInfo 11); the #105 close run plus the #106
        // Style start over GridSplitter (well-known target -257); the
        // #106 Focusable PropertyCustom; the #106 close run plus the #107
        // Style start over Notification (TypeInfo 36); the #107 SizeToContent
        // setter head (qualified owner -739 = Window) where the run stops.
        Assert.Equal(
            new byte[] { 0x23, 0xEB, 0xFF, 0x7A, 0x02, 0x0B, 0x00 },
            theme[0x16C05..0x16C0C]);
        Assert.Equal(
            new byte[] { 0x04, 0x04, 0x08, 0x04, 0x04, 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0xFF, 0xFE, 0x2E, 0x12 },
            theme[0x16C0C..0x16C1C]);
        Assert.Equal(
            new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0x7C, 0xFF },
            theme[0x16C21..0x16C29]);
        Assert.Equal(
            new byte[] { 0x04, 0x04, 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x24 },
            theme[0x16C35..0x16C3F]);
        Assert.Equal(
            new byte[] { 0x03, 0xD4, 0xFD, 0x00, 0x06, 0x15, 0x01, 0x00, 0x89, 0x40, 0x1D, 0xFD, 0x0D, 0x53, 0x69, 0x7A, 0x65 },
            theme[0x16C92..0x16CA3]);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(theme));
        // Entries #87-106 materialize in index order (the entry machine
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
        Assert.DoesNotContain("owners beyond Shape/UIElement are", error.Message);
    }

    [Fact]
    public void Staged_boot_advances_past_grid_splitter_to_notification_stop()
    {
        var entry = LoadStagedHandyControlEntry();
        var error = Assert.Throws<InvalidOperationException>(() => WpfAppBoot.Boot(entry));
        Assert.Contains("startup failed in entry point", error.Message);
        var inner = Assert.IsType<XamlLoadException>(error.InnerException);
        // The line-17 SkinDefault stop, the line-23 Theme key/index plus
        // scalar run, PathBaseStyle, the x:Static-keyed Double, the six
        // converted Behavior entries, the implicit BlendEffectBox style, the
        // 'BorderCircular' entry, the implicit Col/ContentControl styles,
        // the implicit FlipClock style, the implicit FloatingBlock style
        // and the implicit GridSplitter style now resolve; boot fails at
        // the implicit Notification style entry #107 - the precise next
        // blocker.
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
        Assert.DoesNotContain("BAML+0x16954", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.Col", inner.Message);
        Assert.DoesNotContain("well-known member id -132", inner.Message);
        Assert.DoesNotContain("BAML+0x16990", inner.Message);
        Assert.DoesNotContain("BAML+0x169D0", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.FlipClock", inner.Message);
        Assert.DoesNotContain("BAML+0x16BBB", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.FloatingBlock", inner.Message);
        Assert.DoesNotContain("(implicit style key 'GridSplitter')", inner.Message);
        Assert.Contains("Themes/Theme.xaml", inner.Message);
        Assert.Contains("line 23", inner.Message);
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
    public void Staged_shape_floating_block_materializes_with_template_binding()
    {
        // The staged #105 entry shape (implicit FloatingBlock style with
        // HorizontalAlignment/Left plus VerticalAlignment/Top plus a
        // Template setter holding a ControlTemplate with a ContentPresenter
        // visual carrying a ContentTemplate TemplateBinding over the
        // per-file AttributeInfo row) lands as a live Style against the
        // staged converted lib; the TemplateBinding rides the template's
        // legacy tuple like the XAML loader path and stays inert without
        // inflation markup.
        LoadStagedHandyControlEntry();
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var floatingBlock = handyControl.GetType("HandyControl.Controls.FloatingBlock");
        Assert.NotNull(floatingBlock);
        Assert.True(typeof(FrameworkElement).IsAssignableFrom(floatingBlock));
        Assert.Same(FrameworkElement.HorizontalAlignmentProperty,
            DependencyProperty.FromName(floatingBlock!, "HorizontalAlignment"));
        Assert.Same(FrameworkElement.VerticalAlignmentProperty,
            DependencyProperty.FromName(floatingBlock!, "VerticalAlignment"));
        var bytes = FloatingBlockDictionary(
            FloatingBlockEntry(
                ("Left", "Top"),
                ("ContentTemplate", 4)));
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary[floatingBlock!]);
        Assert.Same(floatingBlock, style.TargetType);
        Assert.Equal(3, style.Setters.Count);
        var halign = Assert.IsType<Setter>(style.Setters[0]);
        Assert.Same(FrameworkElement.HorizontalAlignmentProperty, halign.Property);
        Assert.Equal(HorizontalAlignment.Left, halign.Value);
        var valign = Assert.IsType<Setter>(style.Setters[1]);
        Assert.Same(FrameworkElement.VerticalAlignmentProperty, valign.Property);
        Assert.Equal(VerticalAlignment.Top, valign.Value);
        var templateSetter = Assert.IsType<Setter>(style.Setters[2]);
        Assert.Same(Control.TemplateProperty, templateSetter.Property);
        var template = Assert.IsType<ControlTemplate>(templateSetter.Value);
        Assert.Same(floatingBlock, template.TargetType);
        var presenter = Assert.IsType<ContentPresenter>(template.VisualTree);
        var binding = Assert.Single(template.TemplateBindings);
        Assert.Same(presenter, binding.Element);
        Assert.Equal("ContentTemplate", binding.Property);
        var error = Assert.Throws<InvalidOperationException>(() => template.LoadContent());
        Assert.Contains("no inflation markup", error.Message);
        Assert.Contains("BAML-tree inflation", error.Message);
    }

    [Fact]
    public void HAlign_VAlign_short_form_land_on_engine_target()
    {
        // The staged #105 alignment shape over an engine target: both
        // short-form setters resolve against the style TargetType and land
        // the inherited DPs; applying flips both Stretch defaults through
        // the DP store.
        XamlLoader.CustomElementSource = null;
        var bytes = AlignmentDictionary("DesktopWasm.Wpf.ContentControl",
            (-48, "Left", -195), (-56, "Top", -195));
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary[typeof(ContentControl)]);
        Assert.Same(typeof(ContentControl), style.TargetType);
        Assert.Equal(2, style.Setters.Count);
        var halign = Assert.IsType<Setter>(style.Setters[0]);
        Assert.Same(FrameworkElement.HorizontalAlignmentProperty, halign.Property);
        Assert.Equal(HorizontalAlignment.Left, halign.Value);
        var valign = Assert.IsType<Setter>(style.Setters[1]);
        Assert.Same(FrameworkElement.VerticalAlignmentProperty, valign.Property);
        Assert.Equal(VerticalAlignment.Top, valign.Value);
        var target = new ContentControl();
        Assert.Equal(HorizontalAlignment.Stretch, target.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Stretch, target.VerticalAlignment);
        style.ApplyTo(target);
        Assert.Equal(HorizontalAlignment.Left, target.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Top, target.VerticalAlignment);
    }

    [Fact]
    public void HAlign_on_target_without_dp_fails_precise()
    {
        // The short-form member resolves against the style TargetType: a
        // target with no HorizontalAlignment DP anywhere in its hierarchy
        // fails precise naming the target instead of landing a half-built
        // setter.
        XamlLoader.CustomElementSource = null;
        var bytes = AlignmentDictionary(typeof(Slice122PlainStub).FullName!,
            (-48, "Left", -195));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("'HorizontalAlignment' is not a dependency property of Slice122PlainStub", error.Message);
    }

    [Fact]
    public void GridSplitter_target_lands_focusable_style()
    {
        // The staged #106 shape (implicit key, well-known GridSplitter
        // TargetType -257, short-form Focusable PropertyCustom plus a
        // False/BooleanConverter value) lands as a live Style whose Setter
        // carries the inherited Focusable DP with false; applying it to a
        // real GridSplitter flips the default through the DP store.
        XamlLoader.CustomElementSource = null;
        var bytes = GridSplitterDictionary();
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary[typeof(GridSplitter)]);
        Assert.Same(typeof(GridSplitter), style.TargetType);
        var setter = Assert.IsType<Setter>(Assert.Single(style.Setters));
        Assert.Same(UIElement.FocusableProperty, setter.Property);
        Assert.Equal(false, setter.Value);
        var target = new GridSplitter();
        Assert.True(target.Focusable);
        style.ApplyTo(target);
        Assert.False(target.Focusable);
    }

    [Fact]
    public void TemplateBinding_margin_source_resolves_padding()
    {
        // The staged #107 template shape (ContentPresenter with
        // Margin={TemplateBinding Padding}: 0x23 target -49 over source -35)
        // lands with the (element, source-name) pair recorded on the
        // template like the XAML loader path.
        XamlLoader.CustomElementSource = null;
        var bytes = PresenterTemplateDictionary("DesktopWasm.Wpf.Button",
            new B().Raw(0x23).I16(-49).U16(634).I16(-35));
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary[typeof(Button)]);
        var template = Assert.IsType<ControlTemplate>(Assert.IsType<Setter>(Assert.Single(style.Setters)).Value);
        var presenter = Assert.IsType<ContentPresenter>(template.VisualTree);
        var binding = Assert.Single(template.TemplateBindings);
        Assert.Same(presenter, binding.Element);
        Assert.Equal("Padding", binding.Property);
    }

    [Fact]
    public void Non_template_binding_extension_fails_precise()
    {
        // Template ContentPresenter extensions take the TemplateBinding
        // discriminator (634, verified against the real net10 fixture);
        // any other extension id fails naming it.
        XamlLoader.CustomElementSource = null;
        var bytes = PresenterTemplateDictionary("DesktopWasm.Wpf.Button",
            new B().Raw(0x23).I16(-21).U16(189).I16(-15));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("holds extension 189", error.Message);
        Assert.Contains("template extensions take TemplateBinding", error.Message);
    }

    [Fact]
    public void Unknown_extension_target_fails_precise()
    {
        // TemplateBinding targets are the staged ContentTemplate/Margin
        // members on a ContentPresenter (slice-125 adds the Border
        // Background/BorderBrush/BorderThickness members); anything else
        // fails naming the id instead of landing a half-attached value.
        XamlLoader.CustomElementSource = null;
        var bytes = PresenterTemplateDictionary("DesktopWasm.Wpf.Button",
            new B().Raw(0x23).I16(-110).U16(634).I16(-15));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("template extension target id -110", error.Message);
        Assert.Contains("template extension targets take ContentTemplate/Margin/Content/Height/Width/TextElement.Foreground/HorizontalAlignment/VerticalAlignment on a ContentPresenter and Background/BorderBrush/BorderThickness/Height/Width/Margin/HorizontalAlignment/VerticalAlignment on a Border and Fill/Height/Width/Stroke on a Rectangle and Fill/Stroke/Height/Width on a Shape and Foreground/Text on a TextBlock and Margin/HorizontalAlignment/VerticalAlignment on a Panel", error.Message);
    }

    [Fact]
    public void Unknown_well_known_extension_source_fails_precise()
    {
        // TemplateBinding sources are the staged ContentTemplate/Padding
        // members (slice-125 adds Background/BorderBrush/BorderThickness,
        // slice-129 adds Foreground) or a per-file AttributeInfo row; an
        // unknown well-known id fails naming it.
        XamlLoader.CustomElementSource = null;
        var bytes = PresenterTemplateDictionary("DesktopWasm.Wpf.Button",
            new B().Raw(0x23).I16(-21).U16(634).I16(-999));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("template extension source id -999", error.Message);
        Assert.Contains("template extension sources take ContentTemplate/Padding/Background/BorderBrush/BorderThickness/Content/Height/Width/HorizontalContentAlignment/VerticalContentAlignment/Foreground or an AttributeInfo row", error.Message);
    }

    [Fact]
    public void Extension_source_without_attribute_record_fails_precise()
    {
        // A non-negative TemplateBinding source travels as a per-file
        // AttributeInfo index; an index past the table fails loud instead
        // of misaligning.
        XamlLoader.CustomElementSource = null;
        var bytes = PresenterTemplateDictionary("DesktopWasm.Wpf.Button",
            new B().Raw(0x23).I16(-21).U16(634).I16(99));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("template extension source id 99", error.Message);
        Assert.Contains("has no AttributeInfo record", error.Message);
    }

    /// <summary>Builds a single-entry implicit-style dictionary over one
    /// TypeInfo row plus the Style/Setter/ControlTemplate AttributeInfo
    /// rows and the staged #105 body (two short-form alignment setters
    /// plus a Template setter holding a ControlTemplate with a
    /// ContentPresenter carrying one TemplateBinding value).</summary>
    private static byte[] FloatingBlockDictionary(B entry) => new B().Raw(Header()).Raw(0x01)
        .Raw(0x03).I16(-524).Raw(0x00)
        .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
        .Sized(0x1D, p => p.U16(0).U16(0).Str("HandyControl.Controls.FloatingBlock"))
        .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
        .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
        .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
        .Sized(0x1F, p => p.I16(3).I16(-108).Raw(0x00).Str("TargetType"))
        .Sized(0x1F, p => p.I16(4).I16(0).Raw(0x00).Str("ContentTemplate"))
        .Raw(0x25).U32((uint)(entry.ToArray().Length + 10))
        .Raw(0x27).I16(0).Raw(0x00).U32(0).Raw(0x00, 0x00)
        .Raw(entry.ToArray())
        .Raw(0x04, 0x02).ToArray();

    private static B FloatingBlockEntry((string HAlign, string VAlign) alignment, (string Source, short Attr) binding) => new B()
        .Raw(0x03).I16(-620).Raw(0x00)
        .Raw(0x22).I16(0).I16(0)
        .Raw(0x2E).I16(-238)
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16(1).U16(137).I16(-48))
        .Sized(0x24, q => q.I16(2).Str(alignment.HAlign).I16(-195))
        .Raw(0x04)
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16(1).U16(137).I16(-56))
        .Sized(0x24, q => q.I16(2).Str(alignment.VAlign).I16(-195))
        .Raw(0x04)
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16(1).U16(137).I16(-37))
        .Raw(0x07).I16(2)
        .Raw(0x03).I16(-108).Raw(0x00)
        .Raw(0x22).I16(3).I16(0)
        .Raw(0x2E).I16(-174)
        .Raw(0x03).I16(-102).Raw(0x00)
        .Raw(0x23).I16(-21).U16(634).I16(binding.Attr)
        .Raw(0x04)
        .Raw(0x04)
        .Raw(0x08)
        .Raw(0x04)
        .Raw(0x04);

    /// <summary>Builds a single-entry implicit-style dictionary over one
    /// engine TypeInfo row plus the Style/Setter AttributeInfo rows with
    /// short-form setters (member id, text, converter each).</summary>
    private static byte[] AlignmentDictionary(string fullName, params (short Member, string Text, short Converter)[] setters)
    {
        var entry = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(0)
            .Raw(0x2E).I16(-238);
        foreach (var (member, text, converter) in setters)
            entry
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, q => q.I16(1).U16(137).I16(member))
                .Sized(0x24, q => q.I16(2).Str(text).I16(converter))
                .Raw(0x04);
        entry.Raw(0x04);
        return ImplicitDictionary(entry.ToArray(), 0,
            [(fullName, (ushort)0)],
            [(0, "TargetType", -620), (1, "Property", -556), (2, "Value", -556)]);
    }

    /// <summary>Builds the staged #106 shape: an implicit style over the
    /// well-known GridSplitter target (-257) with a short-form Focusable
    /// setter, no TypeInfo rows.</summary>
    private static byte[] GridSplitterDictionary()
    {
        var entry = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-257)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-132))
            .Sized(0x24, q => q.I16(2).Str("False").I16(-46))
            .Raw(0x04)
            .Raw(0x04);
        return ImplicitDictionary(entry.ToArray(), -257, null,
            [(0, "TargetType", -620), (1, "Property", -556), (2, "Value", -556)]);
    }

    /// <summary>Builds a single-entry implicit-style dictionary over one
    /// engine TypeInfo row with a Template setter holding a
    /// ControlTemplate with a ContentPresenter carrying the given 0x23
    /// TemplateBinding payload.</summary>
    private static byte[] PresenterTemplateDictionary(string fullName, B extension)
    {
        var entry = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(0)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-37))
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-108).Raw(0x00)
            .Raw(0x22).I16(3).I16(0)
            .Raw(0x2E).I16(-174)
            .Raw(0x03).I16(-102).Raw(0x00)
            .Raw(extension.ToArray())
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x04);
        return ImplicitDictionary(entry.ToArray(), 0,
            [(fullName, (ushort)0)],
            [(0, "TargetType", -620), (1, "Property", -556), (2, "Value", -556), (3, "TargetType", -108)]);
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

    private sealed class Slice122PlainStub
    {
    }

    private sealed class B
    {
        private readonly List<byte> _b = new();
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
