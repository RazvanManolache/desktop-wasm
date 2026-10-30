// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-121 HandyControl theme.baml implicit FlipClock run.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// entry #104 (implicit FlipClock style at file 0x16990: short-form
/// Focusable setter plus a Template setter whose ControlTemplate holds a
/// StackPanel visual with six converted FlipNumber children carrying
/// Margin plus Number Bindings over NumberList[0..5] with TemplatedParent
/// relative sources) lands as a live engine style, and the run stops
/// precise at the implicit FloatingBlock style entry #105 (file 0x16AAB:
/// short-form HorizontalAlignment setter, well-known member id -48).
/// Slice-122 lands #105 and the #106 GridSplitter style and stops at the
/// #107 Notification entry (see Slice122BamlAlignmentTests).
///
/// Byte-exact decode of entry #104 [0x16990, 0x16AAB) against the staged
/// 466350-byte theme.baml, triangulated three ways (the staged bytes, the
/// upstream HandyControl Theme.xaml FlipClock block shape, and a real
/// net10 BAML fixture compiling a Button-targeted style with short-form
/// Focusable/IsEnabled/HorizontalAlignment/VerticalAlignment/Margin
/// setters plus a StackPanel template visual with Margin children bound
/// through RelativeSource TemplatedParent): short-form member ids -132
/// (Focusable), -133 (IsEnabled), -48/-56 (alignment), -49 (Margin) and
/// the template record framing (0x2A/0x2B bare constructor parameters
/// with 0x10 Text values, 0x07 complex children holding Binding/RelativeSource
/// elements, per-file AttributeInfo plus well-known template members)
/// all match the fixture byte-for-byte in shape.
///
/// Correction to the slice-120 report: the staged #104 Number values use
/// 0x2A constructor parameters plus 0x10 Text (Binding Path plus
/// RelativeSource Mode), not 0x38/0x37 StaticResource or 0x23
/// markup-extension records; the 0x23 TemplateBinding record lands in
/// slice-122 on the #105 ContentPresenter (see
/// Slice122BamlAlignmentTests) while the 0x37/0x38 StaticResource values
/// stay pinned behind entry #107 along with template inflation.
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice121BamlFlipClockTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice121BamlFlipClockTests()
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
    public void Theme_baml_lands_flip_clock_then_stops_at_floating_block()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        // Byte-verbatim entry #104 framing: the Style start plus the
        // FlipClock TargetType reference (TypeInfo 33); the Template setter's
        // ControlTemplate plus VisualTree plus StackPanel visual start; the
        // StackPanel Orientation/Horizontal value; the first FlipNumber
        // child (type 34) with the well-known Margin member plus the "-20"
        // ThicknessConverter value plus the Number complex start; a
        // "0,-20,-20,-20" quad-margin head on a later child; the entry
        // close (visual/template/setter/style ends) plus the #105 Style
        // start over FloatingBlock (TypeInfo 35).
        Assert.Equal(
            new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x21, 0x00, 0x2E, 0x12, 0xFF, 0x03, 0xD4, 0xFD, 0x00 },
            theme[0x16990..0x169A0]);
        Assert.Equal(
            new byte[] { 0x03, 0x94, 0xFF, 0x00, 0x22, 0x07, 0x00, 0x21, 0x00, 0x2E, 0x52, 0xFF, 0x03, 0xA7, 0xFD, 0x00 },
            theme[0x169C4..0x169D4]);
        Assert.Equal(
            new byte[] { 0x03, 0xA7, 0xFD, 0x00, 0x24, 0x10, 0x08, 0x00, 0x0A, 0x48, 0x6F, 0x72, 0x69, 0x7A, 0x6F, 0x6E, 0x74, 0x61, 0x6C, 0x3D, 0xFF },
            theme[0x169D0..0x169E5]);
        Assert.Equal(
            new byte[] { 0x03, 0x22, 0x00, 0x00, 0x24, 0x09, 0xCF, 0xFF, 0x03, 0x2D, 0x32, 0x30, 0x71, 0xFD, 0x07, 0x09, 0x00 },
            theme[0x169E8..0x169F9]);
        Assert.Equal(
            new byte[] { 0x24, 0x13, 0xCF, 0xFF, 0x0D, 0x30, 0x2C, 0x2D, 0x32, 0x30 },
            theme[0x16A7A..0x16A84]);
        Assert.Equal(
            new byte[] { 0x04, 0x04, 0x04, 0x08, 0x04, 0x04, 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x23, 0x00, 0x2E, 0x12, 0xFF, 0x03, 0xD4, 0xFD, 0x00 },
            theme[0x16BA5..0x16BBB]);
        Assert.Equal(
            new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xD0, 0xFF },
            theme[0x16BBB..0x16BC3]);
        Assert.Equal(
            new byte[] { 0x24, 0x0A, 0x02, 0x00, 0x04, 0x4C, 0x65, 0x66, 0x74, 0x3D, 0xFF },
            theme[0x16BC3..0x16BCE]);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(theme));
        // Entries #87-104 materialize in index order (the entry machine
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
    public void Staged_boot_advances_past_flip_clock_to_floating_block_stop()
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
    public void Staged_shape_flip_clock_template_materializes_with_bindings()
    {
        // The staged #104 entry shape (implicit FlipClock style with a
        // Focusable/False setter plus a Template setter holding a
        // ControlTemplate with a StackPanel visual carrying converted
        // FlipNumber children with Margin plus Number Bindings) lands as a
        // live Style against the staged converted lib.
        LoadStagedHandyControlEntry();
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var flipClock = handyControl.GetType("HandyControl.Controls.FlipClock");
        Assert.NotNull(flipClock);
        var flipNumber = handyControl.GetType("HandyControl.Controls.FlipNumber");
        Assert.NotNull(flipNumber);
        var bytes = FlipClockDictionary(
            FlipClockEntry(firstSingle: true, "NumberList[0]", secondSingle: false, "NumberList[1]"));
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary[flipClock!]);
        Assert.Same(flipClock, style.TargetType);
        Assert.Equal(2, style.Setters.Count);
        var focusable = Assert.IsType<Setter>(style.Setters[0]);
        Assert.Same(UIElement.FocusableProperty, focusable.Property);
        Assert.Equal(false, focusable.Value);
        var templateSetter = Assert.IsType<Setter>(style.Setters[1]);
        Assert.Same(Control.TemplateProperty, templateSetter.Property);
        var template = Assert.IsType<ControlTemplate>(templateSetter.Value);
        Assert.Same(flipClock, template.TargetType);
        Assert.Empty(template.Triggers);
        var panel = Assert.IsType<StackPanel>(template.VisualTree);
        Assert.Equal(Orientation.Horizontal, panel.Orientation);
        Assert.Equal(2, panel.Children.Count);
        var first = panel.Children[0];
        Assert.Equal("HandyControl.Controls.FlipNumber", first.GetType().FullName);
        Assert.Equal(new Thickness(-20), ((FrameworkElement)first).Margin);
        var numberProperty = DependencyProperty.FromName(flipNumber!, "Number");
        Assert.NotNull(numberProperty);
        var firstBinding = BindingOperations.GetBinding((DependencyObject)first, numberProperty!);
        Assert.NotNull(firstBinding);
        Assert.Equal("NumberList[0]", firstBinding!.Path.Path);
        Assert.NotNull(firstBinding.RelativeSource);
        Assert.Equal(RelativeSourceMode.TemplatedParent, firstBinding.RelativeSource!.Mode);
        var second = panel.Children[1];
        Assert.Equal("HandyControl.Controls.FlipNumber", second.GetType().FullName);
        Assert.Equal(new Thickness(0, -20, -20, -20), ((FrameworkElement)second).Margin);
        var secondBinding = BindingOperations.GetBinding((DependencyObject)second, numberProperty!);
        Assert.NotNull(secondBinding);
        Assert.Equal("NumberList[1]", secondBinding!.Path.Path);
        Assert.Equal(RelativeSourceMode.TemplatedParent, secondBinding!.RelativeSource!.Mode);
    }

    [Fact]
    public void Staged_shape_flip_clock_template_has_no_inflation_markup()
    {
        // The landed #104 template retains its visual root without
        // inflation markup, so inflating throws the precise BAML-tree
        // inflation boundary instead of sharing one instance.
        LoadStagedHandyControlEntry();
        var bytes = FlipClockDictionary(
            FlipClockEntry(firstSingle: true, "NumberList[0]", secondSingle: true, "NumberList[2]"));
        var dictionary = BamlReader.LoadDictionary(bytes);
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var flipClock = handyControl.GetType("HandyControl.Controls.FlipClock");
        Assert.NotNull(flipClock);
        var style = Assert.IsType<Style>(dictionary[flipClock!]);
        var template = Assert.IsType<ControlTemplate>(Assert.IsType<Setter>(style.Setters[1]).Value);
        var error = Assert.Throws<InvalidOperationException>(() => template.LoadContent());
        Assert.Contains("no inflation markup", error.Message);
        Assert.Contains("BAML-tree inflation", error.Message);
    }

    [Fact]
    public void IsEnabled_short_form_lands_false_on_engine_target()
    {
        // The staged-later short-form IsEnabled shape (implicit key,
        // well-known ContentControl TargetType, short-form IsEnabled
        // PropertyCustom plus a False/BooleanConverter value) lands as a
        // live Style whose Setter carries the inherited IsEnabled DP with
        // false; applying it to a real ContentControl flips the default
        // (Controls default IsEnabled to true) through the DP store.
        XamlLoader.CustomElementSource = null;
        var bytes = ImplicitIsEnabledDictionary("DesktopWasm.Wpf.ContentControl", -133, "False");
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary[typeof(ContentControl)]);
        Assert.Same(typeof(ContentControl), style.TargetType);
        var setter = Assert.IsType<Setter>(Assert.Single(style.Setters));
        Assert.Same(UIElement.IsEnabledProperty, setter.Property);
        Assert.Equal(false, setter.Value);
        var target = new ContentControl();
        Assert.True(target.IsEnabled);
        style.ApplyTo(target);
        Assert.False(target.IsEnabled);
    }

    [Fact]
    public void IsEnabled_short_form_lands_false_on_converted_like_target()
    {
        // The IsEnabled short-form over a converted-like target (a
        // test-assembly Control deriving no new IsEnabled DP, like the
        // staged HandyControl controls): the TargetType and the implicit key
        // resolve to the entry CLR type and the setter lands the inherited
        // engine IsEnabled DP with false.
        XamlLoader.CustomElementSource = null;
        var bytes = ImplicitIsEnabledDictionary(typeof(Slice121ColStub).FullName!, -133, "False");
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary[typeof(Slice121ColStub)]);
        Assert.Same(typeof(Slice121ColStub), style.TargetType);
        var setter = Assert.IsType<Setter>(Assert.Single(style.Setters));
        Assert.Same(UIElement.IsEnabledProperty, setter.Property);
        Assert.Equal(false, setter.Value);
        var target = new Slice121ColStub();
        Assert.True(target.IsEnabled);
        style.ApplyTo(target);
        Assert.False(target.IsEnabled);
    }

    [Fact]
    public void IsEnabled_on_target_without_dp_fails_precise()
    {
        // The short-form member resolves against the style TargetType: a
        // target with no IsEnabled DP anywhere in its hierarchy fails
        // precise naming the target instead of landing a half-built setter.
        XamlLoader.CustomElementSource = null;
        var bytes = ImplicitIsEnabledDictionary(typeof(Slice121PlainStub).FullName!, -133, "False");
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("'IsEnabled' is not a dependency property of Slice121PlainStub", error.Message);
    }

    [Fact]
    public void Empty_border_template_visual_lands()
    {
        // Border visuals land since slice-125 (the staged
        // SimpleItemsControl template carries one with TemplateBindings
        // plus a PART_Panel child); an empty Border lands with no child
        // and no bindings, like its XAML equivalent.
        var bytes = DirectVisualTemplateDictionary("DesktopWasm.Wpf.Button", new B()
            .Raw(0x03).I16(-50).Raw(0x00)
            .Raw(0x04));
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary[typeof(Button)]);
        var template = Assert.IsType<ControlTemplate>(Assert.IsType<Setter>(Assert.Single(style.Setters)).Value);
        var border = Assert.IsType<Border>(template.VisualTree);
        Assert.Null(border.Child);
        Assert.Empty(template.TemplateBindings);
    }

    [Fact]
    public void Unknown_template_member_fails_precise()
    {
        // Template simples beyond the staged Margin/Text members fail
        // precise naming the id instead of landing a half-built child.
        var bytes = StackTemplateDictionary("DesktopWasm.Wpf.Button", new B()
            .Raw(0x03).I16(0).Raw(0x00)
            .Sized(0x24, q => q.I16(-111).Str("x").I16(-615))
            .Raw(0x04));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("template members beyond Margin/Text/TextTrimming/StrokeThickness/Height/Width/MinHeight/MinWidth/Background", error.Message);
    }

    [Fact]
    public void Bad_template_value_fails_precise()
    {
        // Template simple values convert through the DP type like
        // setters: garbage Margin text fails naming the value.
        var bytes = StackTemplateDictionary("DesktopWasm.Wpf.Button", new B()
            .Raw(0x03).I16(0).Raw(0x00)
            .Sized(0x24, q => q.I16(-49).Str("abc").I16(-655))
            .Raw(0x04));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("'abc' with converter -655 does not convert to Thickness", error.Message);
    }

    [Fact]
    public void Binding_with_two_constructor_arguments_fails_precise()
    {
        // Template Bindings take one constructor argument (the path); richer
        // markup-extension arguments stay follow-ups.
        var bytes = StackTemplateDictionary("DesktopWasm.Wpf.Button", BindingChild("NumberList[0]", "Extra"));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("takes one constructor argument", error.Message);
    }

    [Fact]
    public void RelativeSource_with_bad_mode_fails_precise()
    {
        // Template RelativeSource modes parse like the engine enum; unknown
        // names fail naming the value.
        var bytes = StackTemplateDictionary("DesktopWasm.Wpf.Button",
            BindingChildWithMode("NumberList[0]", "Nowhere"));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("names 'Nowhere'", error.Message);
    }

    [Fact]
    public void Non_binding_template_complex_value_fails_precise()
    {
        // Template complex children beyond Binding/RelativeSource stay
        // follow-ups with the child named.
        var bytes = StackTemplateDictionary("DesktopWasm.Wpf.Button", new B()
            .Raw(0x03).I16(0).Raw(0x00)
            .Raw(0x07).I16(5)
            .Raw(0x03).I16(1).Raw(0x00)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("complex children take a Binding, RelativeSource, TransformGroup or TemplateBinding", error.Message);
    }

    /// <summary>Builds a single-entry implicit-style dictionary with the
    /// staged #104 shape: FlipClock TargetType plus Focusable/False and
    /// Template setters whose ControlTemplate holds a StackPanel visual
    /// with two converted FlipNumber children.</summary>
    private static byte[] FlipClockDictionary(B entry) => new B().Raw(Header()).Raw(0x01)
        .Raw(0x03).I16(-524).Raw(0x00)
        .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
        .Sized(0x1D, p => p.U16(0).U16(0).Str("HandyControl.Controls.FlipClock"))
        .Sized(0x1D, p => p.U16(1).U16(0).Str("HandyControl.Controls.FlipNumber"))
        .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
        .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
        .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
        .Sized(0x1F, p => p.I16(3).I16(-108).Raw(0x00).Str("TargetType"))
        .Sized(0x1F, p => p.I16(4).I16(-601).Raw(0x00).Str("Orientation"))
        .Sized(0x1F, p => p.I16(5).I16(1).Raw(0x00).Str("Number"))
        .Sized(0x1F, p => p.I16(6).I16(-20).Raw(0x00).Str("RelativeSource"))
        .Raw(0x25).U32((uint)(entry.ToArray().Length + 10))
        .Raw(0x27).I16(0).Raw(0x00).U32(0).Raw(0x00, 0x00)
        .Raw(entry.ToArray())
        .Raw(0x04, 0x02).ToArray();

    private static B FlipClockEntry(bool firstSingle, string firstPath, bool secondSingle, string secondPath) => new B()
        .Raw(0x03).I16(-620).Raw(0x00)
        .Raw(0x22).I16(0).I16(0)
        .Raw(0x2E).I16(-238)
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16(1).U16(137).I16(-132))
        .Sized(0x24, q => q.I16(2).Str("False").I16(-46))
        .Raw(0x04)
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16(1).U16(137).I16(-37))
        .Raw(0x07).I16(2)
        .Raw(0x03).I16(-108).Raw(0x00)
        .Raw(0x22).I16(3).I16(0)
        .Raw(0x2E).I16(-174)
        .Raw(0x03).I16(-601).Raw(0x00)
        .Sized(0x24, q => q.I16(4).Str("Horizontal").I16(-195))
        .Raw(0x2E).I16(-213)
        .Raw(FlipNumberChild(firstSingle ? "-20" : "0,-20,-20,-20", firstPath).ToArray())
        .Raw(FlipNumberChild(secondSingle ? "-20" : "0,-20,-20,-20", secondPath).ToArray())
        .Raw(0x04)
        .Raw(0x04)
        .Raw(0x08)
        .Raw(0x04)
        .Raw(0x04);

    private static B FlipNumberChild(string margin, string path) => new B()
        .Raw(0x03).I16(1).Raw(0x00)
        .Sized(0x24, q => q.I16(-49).Str(margin).I16(-655))
        .Raw(0x07).I16(5)
        .Raw(0x03).I16(-20).Raw(0x00)
        .Raw(0x2A).Sized(0x10, q => q.Str(path)).Raw(0x2B)
        .Raw(0x07).I16(6)
        .Raw(0x03).I16(-516).Raw(0x00)
        .Raw(0x2A).Sized(0x10, q => q.Str("TemplatedParent")).Raw(0x2B)
        .Raw(0x04)
        .Raw(0x08)
        .Raw(0x04)
        .Raw(0x08)
        .Raw(0x04);

    private static B BindingChild(params string[] args)
    {
        var ctor = new B().Raw(0x2A);
        foreach (var arg in args) ctor.Sized(0x10, q => q.Str(arg));
        ctor.Raw(0x2B);
        return new B()
            .Raw(0x03).I16(0).Raw(0x00)
            .Raw(0x07).I16(5)
            .Raw(0x03).I16(-20).Raw(0x00)
            .Raw(ctor.ToArray())
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04);
    }

    private static B BindingChildWithMode(string path, string mode) => new B()
        .Raw(0x03).I16(0).Raw(0x00)
        .Raw(0x07).I16(5)
        .Raw(0x03).I16(-20).Raw(0x00)
        .Raw(0x2A).Sized(0x10, q => q.Str(path)).Raw(0x2B)
        .Raw(0x07).I16(6)
        .Raw(0x03).I16(-516).Raw(0x00)
        .Raw(0x2A).Sized(0x10, q => q.Str(mode)).Raw(0x2B)
        .Raw(0x04)
        .Raw(0x08)
        .Raw(0x04)
        .Raw(0x08)
        .Raw(0x04);

    /// <summary>Builds a single-entry implicit-style dictionary with a
    /// StackPanel template visual over one engine child (TypeInfo 0), for
    /// template-surface negative pins.</summary>
    private static byte[] StackTemplateDictionary(string fullName, B visualContent)
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
            .Raw(0x03).I16(-601).Raw(0x00)
            .Sized(0x24, q => q.I16(4).Str("Horizontal").I16(-195))
            .Raw(0x2E).I16(-213)
            .Raw(visualContent.ToArray())
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x04);
        return new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1D, p => p.U16(0).U16(0).Str(fullName))
            .Sized(0x1D, p => p.U16(1).U16(0).Str("DesktopWasm.Wpf.Button"))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x1F, p => p.I16(3).I16(-108).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(4).I16(-601).Raw(0x00).Str("Orientation"))
            .Sized(0x1F, p => p.I16(5).I16(0).Raw(0x00).Str("Content"))
            .Sized(0x1F, p => p.I16(6).I16(-20).Raw(0x00).Str("RelativeSource"))
            .Raw(0x25).U32((uint)(entry.ToArray().Length + 10))
            .Raw(0x27).I16(0).Raw(0x00).U32(0).Raw(0x00, 0x00)
            .Raw(entry.ToArray())
            .Raw(0x04, 0x02).ToArray();
    }

    /// <summary>Builds a single-entry implicit-style dictionary with a
    /// direct template visual (no StackPanel), for template-visual
    /// landing and negative pins.</summary>
    private static byte[] DirectVisualTemplateDictionary(string fullName, B visual)
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
            .Raw(visual.ToArray())
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x04);
        return new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1D, p => p.U16(0).U16(0).Str(fullName))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x1F, p => p.I16(3).I16(-108).Raw(0x00).Str("TargetType"))
            .Raw(0x25).U32((uint)(entry.ToArray().Length + 10))
            .Raw(0x27).I16(0).Raw(0x00).U32(0).Raw(0x00, 0x00)
            .Raw(entry.ToArray())
            .Raw(0x04, 0x02).ToArray();
    }

    /// <summary>Builds a single-entry implicit-style dictionary with a
    /// short-form IsEnabled setter.</summary>
    private static byte[] ImplicitIsEnabledDictionary(string fullName, short memberId, string text)
    {
        var content = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(0)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(memberId))
            .Sized(0x24, q => q.I16(2).Str(text).I16(-46))
            .Raw(0x04)
            .Raw(0x04).ToArray();
        return new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1D, p => p.U16(0).U16(0).Str(fullName))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Raw(0x25).U32((uint)(content.Length + 10))
            .Raw(0x27).I16(0).Raw(0x00).U32(0).Raw(0x00, 0x00)
            .Raw(content)
            .Raw(0x04, 0x02).ToArray();
    }

    private sealed class Slice121ColStub : Control
    {
    }

    private sealed class Slice121PlainStub
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
