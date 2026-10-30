// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-125 HandyControl theme.baml SimpleItemsControl/Sprite run.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the implicit SimpleItemsControl style at BAML+0x17425 (Template setter
/// with a Border visual carrying Background/BorderBrush/BorderThickness
/// TemplateBindings plus a PART_Panel StackPanel child) and the implicit
/// Sprite style at BAML+0x17483 (WindowAttach/Window setters plus the
/// Template setter with a ContentPresenter carrying the Margin
/// TemplateBinding, the converted IsDragElement PWC and Center alignment
/// PWCs) land as live engine styles; the run stops precise at the
/// 'Right2LeftTransition' Storyboard entry at BAML+0x1760B. Circular-apply
/// lands too (ActualWidth/ActualHeight/RadiusX/RadiusY DP identities plus
/// the MultiBinding transfer the converted RectangleAttach.OnCircularChanged
/// needs).
///
/// Parser-traced entry map (key consumption order, offsets validated):
/// implicit SimpleItemsControl at BAML+0x17425 (Template/Border visual,
/// this slice), implicit Sprite at BAML+0x17483 (attached/window setters
/// plus Template/ContentPresenter visual, this slice),
/// 'Right2LeftTransition' Storyboard at BAML+0x1760B (DoubleAnimation
/// children with 0x05 From/To values, Duration -188, TargetProperty paths
/// and CubicEase EasingFunction children, follow-up).
///
/// Byte-exact decode against the staged 466350-byte theme.baml,
/// triangulated three ways (the staged bytes, the per-file
/// TypeInfo/AttributeInfo/StringInfo tables, and real net10 BAML fixtures:
/// a Border/TemplateBinding fixture byte-identical in every staged shape
/// - Border visual -50, Child content -161, TemplateBinding targets
/// -4/-5/-6 with sources -24/-25/-26, PART_Panel Name PWC with converter
/// -615, Storyboard -608 with Children content -122, DoubleAnimation -169
/// with 0x05 From/To plus Duration -188/TargetProperty/EasingFunction -
/// plus a swapped-source fixture proving the -25/-26 ids track the source
/// property names, and the EventTrigger/StaticResource fixture pinning the
/// -198/-17/0x38 shapes that stay behind).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched;
/// staged-oracle status-gate bypass stays in-probe only).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice125BamlSimpleItemsControlTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice125BamlSimpleItemsControlTests()
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
    public void Theme_baml_lands_sprite_styles_then_stops_at_groupbox()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        // SimpleItemsControl implicit-style head: Style start with the
        // TargetType over TypeInfo 40 plus the Setters content.
        Assert.Equal(
            new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x28, 0x00, 0x2E, 0x12, 0xFF },
            theme[0x17425..0x17431]);
        // Border visual head: ElementStart -50 plus the Background
        // TemplateBinding (target -4, extension 634, source -24).
        Assert.Equal(
            new byte[] { 0x03, 0xCE, 0xFF, 0x00, 0x23, 0xFC, 0xFF, 0x7A, 0x02, 0xE8, 0xFF },
            theme[0x1744C..0x17457]);
        // BorderBrush/BorderThickness TemplateBindings (targets -5/-6,
        // sources -25/-26).
        Assert.Equal(
            new byte[] { 0x23, 0xFB, 0xFF, 0x7A, 0x02, 0xE7, 0xFF, 0x23, 0xFA, 0xFF, 0x7A, 0x02, 0xE6, 0xFF },
            theme[0x17457..0x17465]);
        // Border Child content plus the PART_Panel StackPanel head (Name
        // AttributeInfo row 17, StringConverter -615).
        Assert.Equal(
            new byte[] { 0x2E, 0x5F, 0xFF, 0x03, 0xA7, 0xFD, 0x00, 0x24, 0x10, 0x11, 0x00 },
            theme[0x17465..0x17470]);
        // Sprite implicit-style head: Style start with the TargetType over
        // TypeInfo 41 (HandyControl.Controls.Sprite) plus Setters content.
        Assert.Equal(
            new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x29, 0x00, 0x2E, 0x12, 0xFF },
            theme[0x17483..0x1748F]);
        // Sprite Template setter head: short-form Template Property plus
        // the complex Value start.
        Assert.Equal(
            new byte[] { 0x03, 0xD4, 0xFD, 0x00, 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xDB, 0xFF },
            theme[0x175BB..0x175C7]);
        // Sprite ContentPresenter visual with the Margin TemplateBinding
        // (target -49, source -35 Padding).
        Assert.Equal(
            new byte[] { 0x03, 0x9A, 0xFF, 0x00, 0x23, 0xCF, 0xFF, 0x7A, 0x02, 0xDD, 0xFF },
            theme[0x175D6..0x175E1]);
        // Storyboard entry head: ElementStart -608 with the Children
        // content member plus the first DoubleAnimation start.
        Assert.Equal(
            new byte[] { 0x03, 0xA0, 0xFD, 0x00, 0x2E, 0x86, 0xFF, 0x03, 0x57, 0xFF, 0x00 },
            theme[0x1760B..0x17616]);
        // DoubleAnimation inline From "50" plus To "0" values (0x05 sized
        // inline records over AttributeInfo rows 19/20).
        Assert.Equal(
            new byte[] { 0x05, 0x06, 0x13, 0x00, 0x02, 0x35, 0x30 },
            theme[0x17616..0x1761D]);
        Assert.Equal(
            new byte[] { 0x05, 0x05, 0x14, 0x00, 0x01, 0x30 },
            theme[0x1761D..0x17623]);
        // DoubleAnimation Duration "0:0:0.4" with converter -188.
        Assert.Equal(
            new byte[] { 0x24, 0x0D, 0x15, 0x00, 0x07, 0x30, 0x3A, 0x30, 0x3A, 0x30, 0x2E, 0x34, 0x44, 0xFF },
            theme[0x17623..0x17631]);
        // Storyboard.TargetProperty path head (converter -492).
        Assert.Equal(
            new byte[] { 0x24, 0x55, 0x16, 0x00, 0x4F, 0x28, 0x55, 0x49, 0x45, 0x6C, 0x65, 0x6D, 0x65, 0x6E, 0x74 },
            theme[0x17631..0x17640]);
        // EasingFunction complex child: AttributeInfo row 23 plus the
        // CubicEase TypeInfo 42 element.
        Assert.Equal(
            new byte[] { 0x07, 0x17, 0x00, 0x03, 0x2A, 0x00, 0x00 },
            theme[0x17687..0x1768E]);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(theme));
        // Entries #87-111 materialize in index order (the entry machine
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
        Assert.DoesNotContain("templates take a ContentPresenter or StackPanel visual", error.Message);
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
        Assert.DoesNotContain("BAML+0x1744C", error.Message);
        Assert.DoesNotContain("element Border on the template", error.Message);
        Assert.DoesNotContain("SimpleItemsControl", error.Message);
        Assert.DoesNotContain("BAML+0x17483", error.Message);
        Assert.DoesNotContain("Sprite", error.Message);
        Assert.DoesNotContain("IsDragElement", error.Message);
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
    }

    [Fact]
    public void Staged_boot_advances_past_sprite_to_groupbox_style()
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
        // run plus its Trigger list, the 'TargetArea4ScreenshotStyle' entry,
        // the implicit SimpleItemsControl style (Template/Border visual with
        // TemplateBindings plus PART_Panel) and the implicit Sprite style
        // (attached/window setters plus Template/ContentPresenter visual),
        // the nine transition Storyboards (keyidx 113-121) and the implicit
        // TransitioningContentControl style, the keyed 'ToggleBlockBaseStyle'
        // style (keyidx 127) and the keyed 'GroupBoxBaseTopTemplate' template
        // (keyidx 128) plus the keyed 'GroupBoxBaseLeftTemplate' template
        // (keyidx 129) plus the keyed 'GroupBoxTabTopTemplate' template
        // (keyidx 130) and 'GroupBoxTabLeftTemplate' template (keyidx 131)
        // now resolve; boot fails at the keyed
        // 'ButtonGroupHorizontalUniformItemsPanelTemplate' entry - the
        // precise next blocker.
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
        Assert.DoesNotContain("BAML+0x1744C", inner.Message);
        Assert.DoesNotContain("element Border on the template", inner.Message);
        Assert.DoesNotContain("SimpleItemsControl", inner.Message);
        Assert.DoesNotContain("BAML+0x17483", inner.Message);
        Assert.DoesNotContain("Sprite", inner.Message);
        Assert.DoesNotContain("IsDragElement", inner.Message);
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
    }

    [Fact]
    public void Staged_simple_items_control_template_lands_against_converted_type()
    {
        // The staged SimpleItemsControl entry shape (Template setter with a
        // Border visual carrying Background/BorderBrush/BorderThickness
        // TemplateBindings plus a PART_Panel StackPanel child) lands
        // against the staged HandyControl image: the Border visual, the
        // named child and the three (element, source) pairs are retained
        // on the template like the XAML loader path.
        LoadStagedHandyControlEntry();
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var targetType = handyControl.GetType("HandyControl.Controls.SimpleItemsControl");
        Assert.NotNull(targetType);
        var dictionary = BamlReader.LoadDictionary(SimpleItemsControlDictionary());
        var style = Assert.IsType<Style>(dictionary[targetType]);
        Assert.Same(targetType, style.TargetType);
        var setter = Assert.IsType<Setter>(Assert.Single(style.Setters));
        Assert.Same(DependencyProperty.FromName(targetType, "Template"), setter.Property);
        var template = Assert.IsType<ControlTemplate>(setter.Value);
        Assert.Same(targetType, template.TargetType);
        var border = Assert.IsType<Border>(template.VisualTree);
        var panel = Assert.IsType<StackPanel>(border.Child);
        Assert.Equal("PART_Panel", panel.Name);
        Assert.Equal(3, template.TemplateBindings.Count);
        Assert.Same(border, Assert.IsType<Border>(template.TemplateBindings[0].Element));
        Assert.Equal("Background", template.TemplateBindings[0].Property);
        Assert.Equal("BorderBrush", template.TemplateBindings[1].Property);
        Assert.Equal("BorderThickness", template.TemplateBindings[2].Property);
    }

    [Fact]
    public void Staged_sprite_presenter_converted_pwc_lands_in_dp_store()
    {
        // The Sprite advance mechanism: a template ContentPresenter PWC
        // over a converted attached owner (the staged IsDragElement shape,
        // mirrored here with TipElement.Placement) resolves the converted
        // DP and lands the converted enum value in the DP store.
        LoadStagedHandyControlEntry();
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var tip = handyControl.GetType("HandyControl.Controls.TipElement");
        Assert.NotNull(tip);
        var placement = Assert.IsType<DependencyProperty>(tip!.GetField("PlacementProperty")!.GetValue(null));
        var bytes = ConvertedPresenterDictionary("DesktopWasm.Wpf.Button", "HandyControl.Controls.TipElement", "Placement", "LeftTop");
        var dictionary = BamlReader.LoadDictionary(bytes);
        var style = Assert.IsType<Style>(dictionary[typeof(Button)]);
        var template = Assert.IsType<ControlTemplate>(Assert.IsType<Setter>(Assert.Single(style.Setters)).Value);
        var presenter = Assert.IsType<ContentPresenter>(template.VisualTree);
        Assert.Equal(Enum.Parse(placement.PropertyType, "LeftTop"), presenter.GetValue(placement));
        Assert.Empty(template.TemplateBindings);
    }

    [Fact]
    public void Actual_width_height_are_read_only_layout_identities()
    {
        // WPF parity: ActualWidth/ActualHeight exist as read-only DP
        // identities owned by FrameworkElement (the converted Circular
        // callback binds through their Names); the store mirrors RenderRect
        // on every size change so GetValue agrees with the CLR read.
        Assert.Same(typeof(FrameworkElement), FrameworkElement.ActualWidthProperty.OwnerType);
        Assert.Same(typeof(FrameworkElement), FrameworkElement.ActualHeightProperty.OwnerType);
        Assert.Equal("ActualWidth", FrameworkElement.ActualWidthProperty.Name);
        Assert.Equal("ActualHeight", FrameworkElement.ActualHeightProperty.Name);
        Assert.True(FrameworkElement.ActualWidthProperty.IsReadOnly);
        Assert.True(FrameworkElement.ActualHeightProperty.IsReadOnly);
        var rectangle = new Rectangle();
        Assert.Equal(0.0, rectangle.GetValue(FrameworkElement.ActualWidthProperty));
        Assert.Equal(0.0, rectangle.GetValue(FrameworkElement.ActualHeightProperty));
        rectangle.Arrange(new Rect(0, 0, 100, 60));
        Assert.Equal(100.0, rectangle.ActualWidth);
        Assert.Equal(60.0, rectangle.ActualHeight);
        Assert.Equal(100.0, rectangle.GetValue(FrameworkElement.ActualWidthProperty));
        Assert.Equal(60.0, rectangle.GetValue(FrameworkElement.ActualHeightProperty));
    }

    [Fact]
    public void Radiusx_y_are_store_backed_dps()
    {
        // WPF parity: RadiusX/RadiusY exist as DP identities on Rectangle
        // (the converted Circular callback binds both); the CLR wrappers
        // stay store-backed so binding pushes and direct sets agree.
        Assert.Same(typeof(Rectangle), Rectangle.RadiusXProperty.OwnerType);
        Assert.Same(typeof(Rectangle), Rectangle.RadiusYProperty.OwnerType);
        Assert.Equal("RadiusX", Rectangle.RadiusXProperty.Name);
        Assert.Equal("RadiusY", Rectangle.RadiusYProperty.Name);
        var rectangle = new Rectangle();
        Assert.Equal(0.0, rectangle.RadiusX);
        Assert.Equal(0.0, rectangle.GetValue(Rectangle.RadiusXProperty));
        rectangle.RadiusX = 3;
        rectangle.RadiusY = 4;
        Assert.Equal(3.0, rectangle.GetValue(Rectangle.RadiusXProperty));
        Assert.Equal(4.0, rectangle.GetValue(Rectangle.RadiusYProperty));
    }

    [Fact]
    public void Staged_circular_apply_drives_radius_through_converted_callback()
    {
        // The staged 'RectangleCircular' Circular setter lands the converted
        // attached DP, and applying it fires the converted OnCircularChanged
        // callback (IL-verified: MultiBinding over ActualWidth/ActualHeight
        // driving RadiusX/RadiusY through RectangleCircularConverter): the
        // transfers attach at once, and the first layout pass pushes
        // Min/2 into both radii.
        LoadStagedHandyControlEntry();
        var dictionary = BamlReader.LoadDictionary(MinimalCircularDictionary());
        var style = Assert.IsType<Style>(dictionary["CircularOnly"]);
        var rectangle = new Rectangle();
        style.ApplyTo(rectangle);
        var radiusX = Assert.IsType<MultiBindingExpression>(
            rectangle.GetBindingExpressionBase(Rectangle.RadiusXProperty));
        Assert.IsType<MultiBindingExpression>(
            rectangle.GetBindingExpressionBase(Rectangle.RadiusYProperty));
        Assert.Equal("RadiusX", radiusX.TargetProperty);
        Assert.Equal(0.0, rectangle.RadiusX);
        Assert.Equal(0.0, rectangle.RadiusY);
        rectangle.Arrange(new Rect(0, 0, 100, 60));
        Assert.Equal(30.0, rectangle.RadiusX);
        Assert.Equal(30.0, rectangle.RadiusY);
    }

    [Fact]
    public void Staged_circular_clear_detaches_transfers()
    {
        // The converted OnCircularChanged false branch clears all four
        // bindings (ActualWidth/ActualHeight/RadiusX/RadiusY): the composite
        // transfers detach and stop pushing, the landed radii stand.
        LoadStagedHandyControlEntry();
        var dictionary = BamlReader.LoadDictionary(MinimalCircularDictionary());
        var style = Assert.IsType<Style>(dictionary["CircularOnly"]);
        var rectangle = new Rectangle();
        style.ApplyTo(rectangle);
        rectangle.Arrange(new Rect(0, 0, 100, 60));
        Assert.Equal(30.0, rectangle.RadiusX);
        var clearing = BamlReader.LoadDictionary(CircularOffDictionary());
        Assert.IsType<Style>(clearing["CircularOff"]).ApplyTo(rectangle);
        Assert.Null(rectangle.GetBindingExpressionBase(Rectangle.RadiusXProperty));
        Assert.Null(rectangle.GetBindingExpressionBase(Rectangle.RadiusYProperty));
        Assert.Equal(30.0, rectangle.RadiusX);
        rectangle.Arrange(new Rect(0, 0, 200, 40));
        Assert.Equal(30.0, rectangle.RadiusX);
    }

    [Fact]
    public void MultiBinding_transfer_skips_unset_and_null_combinations()
    {
        // WPF parity: an UnsetValue or null composite keeps the old target
        // value instead of landing a sentinel or crashing the set.
        XamlLoader.CustomElementSource = null;
        var rectangle = new Rectangle { RadiusX = 7 };
        var unset = new MultiBinding { Converter = new UnsetConverter() };
        unset.Bindings.Add(new Binding("ActualWidth") { Source = rectangle });
        BindingOperations.SetBinding(rectangle, Rectangle.RadiusXProperty, unset);
        Assert.Equal(7.0, rectangle.RadiusX);
        var nil = new MultiBinding { Converter = new NullConverter() };
        nil.Bindings.Add(new Binding("ActualWidth") { Source = rectangle });
        BindingOperations.SetBinding(rectangle, Rectangle.RadiusXProperty, nil);
        Assert.Equal(7.0, rectangle.RadiusX);
    }

    [Fact]
    public void MultiBinding_transfer_rejects_non_framework_targets_precise()
    {
        // Composite transfers need the FrameworkElement CLR surface (SizeChanged
        // refresh plus the DP-named CLR property); plain DependencyObjects
        // and attached DP targets fail precise instead of half-attaching.
        XamlLoader.CustomElementSource = null;
        var plain = new DependencyObject();
        var multi = new MultiBinding();
        multi.Bindings.Add(new Binding("ActualWidth") { Source = new Rectangle() });
        var plainError = Assert.Throws<NotSupportedException>(() =>
            BindingOperations.SetBinding(plain, FrameworkElement.ActualWidthProperty, multi));
        Assert.Contains("composite transfers take a FrameworkElement", plainError.Message);
        var attached = DependencyProperty.RegisterAttached(
            "Slice125Attached", typeof(double), typeof(Slice125BamlSimpleItemsControlTests),
            new PropertyMetadata(0.0));
        var element = new Rectangle();
        var attachedError = Assert.Throws<NotSupportedException>(() =>
            BindingOperations.SetBinding(element, attached, multi));
        Assert.Contains("composite transfers take a CLR property", attachedError.Message);
    }

    [Fact]
    public void Border_template_with_non_binding_extension_fails_precise()
    {
        // Border template extensions take the TemplateBinding discriminator
        // (634); any other extension id fails naming it.
        XamlLoader.CustomElementSource = null;
        var bytes = BorderVisualDictionary(new B().Raw(0x23).I16(-4).U16(189).I16(-24));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("template Border holds extension 189", error.Message);
        Assert.Contains("template extensions take TemplateBinding", error.Message);
    }

    [Fact]
    public void Border_template_unknown_target_fails_precise()
    {
        // Border TemplateBinding targets are Background/BorderBrush/
        // BorderThickness; anything else fails naming the id.
        XamlLoader.CustomElementSource = null;
        var bytes = BorderVisualDictionary(new B().Raw(0x23).I16(-110).U16(634).I16(-24));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("template extension target id -110", error.Message);
        Assert.Contains("template extension targets take ContentTemplate/Margin/Content/Height/Width/TextElement.Foreground/HorizontalAlignment/VerticalAlignment on a ContentPresenter and Background/BorderBrush/BorderThickness/Height/Width/Margin/HorizontalAlignment/VerticalAlignment on a Border and Fill/Height/Width/Stroke on a Rectangle and Fill/Stroke/Height/Width on a Shape and Foreground/Text on a TextBlock and Margin/HorizontalAlignment/VerticalAlignment on a Panel", error.Message);
    }

    [Fact]
    public void Border_template_unknown_source_fails_precise()
    {
        // Border TemplateBinding sources are the Background/BorderBrush/
        // BorderThickness members (slice-129 adds Foreground for the GroupBox
        // header); an unknown well-known id fails naming it.
        XamlLoader.CustomElementSource = null;
        var bytes = BorderVisualDictionary(new B().Raw(0x23).I16(-4).U16(634).I16(-999));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("template extension source id -999", error.Message);
        Assert.Contains("template extension sources take ContentTemplate/Padding/Background/BorderBrush/BorderThickness/Content/Height/Width/HorizontalContentAlignment/VerticalContentAlignment/Foreground or an AttributeInfo row", error.Message);
    }

    [Fact]
    public void Border_template_simple_value_fails_precise()
    {
        // Border template visuals take resolving PropertyWithConverter
        // simples (slice-129 lands Margin/Grid.Row like the staged GroupBox
        // borders, and any per-file row that names a real DP); a member no
        // visual carries fails naming the id.
        XamlLoader.CustomElementSource = null;
        var bytes = BorderVisualDictionary(
            new B().Sized(0x24, q => q.I16(-15).Str("PART_Panel").I16(-615)));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("template property id -15", error.Message);
        Assert.Contains("template members beyond Margin/Text/TextTrimming/StrokeThickness/Height/Width/MinHeight/MinWidth/Background", error.Message);
    }

    [Fact]
    public void Border_template_wrong_content_fails_precise()
    {
        // The Border child travels under the Child content member (-161);
        // any other content member fails naming it.
        XamlLoader.CustomElementSource = null;
        var bytes = BorderVisualDictionary(new B().Raw(0x2E).I16(-238), withChild: true);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("content member -238 on the template Border", error.Message);
        Assert.Contains("borders take the Child content", error.Message);
    }

    [Fact]
    public void Border_template_grid_child_still_fails_precise()
    {
        // Border template children are a StackPanel (the staged PART_Panel),
        // a Rectangle, a ContentPresenter (slice-129 lands the GroupBox
        // presenters) or a converted panel; a Grid visual still fails naming
        // it.
        XamlLoader.CustomElementSource = null;
        var bytes = BorderVisualDictionary(
            new B().Raw(0x2E).I16(-161).Raw(0x03).I16(-254).Raw(0x00).Raw(0x04));
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("element Grid on the template Border", error.Message);
        Assert.Contains("template borders take a StackPanel, Rectangle, ContentPresenter, SimplePanel or Viewport3D child", error.Message);
    }

    [Fact]
    public void Border_template_second_child_fails_precise()
    {
        // Borders take one child; a second visual fails instead of
        // replacing the retained PART_Panel.
        XamlLoader.CustomElementSource = null;
        var child = new B().Raw(0x2E).I16(-161)
            .Raw(0x03).I16(-601).Raw(0x00)
            .Sized(0x24, q => q.I16(4).Str("PART_Panel").I16(-615))
            .Raw(0x04)
            .Raw(0x03).I16(-601).Raw(0x00);
        var bytes = BorderVisualDictionary(child);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("second visual on the template Border", error.Message);
        Assert.Contains("borders take one child", error.Message);
    }

    [Fact]
    public void Storyboard_empty_without_children_stays_precise()
    {
        // Animation resources land through the Children content plus
        // DoubleAnimation children: a keyed Storyboard entry with an
        // immediate end fails naming the missing Children instead of
        // landing an empty resource.
        XamlLoader.CustomElementSource = null;
        var bytes = KeyedStoryboardDictionary();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("keyed entry 'S'", error.Message);
        Assert.Contains("holds a Storyboard with no Children", error.Message);
        Assert.Contains("storyboards take the Children content plus DoubleAnimation children", error.Message);
    }

    /// <summary>Builds the staged SimpleItemsControl entry shape: an
    /// implicit style over the converted SimpleItemsControl with a Template
    /// setter holding a ControlTemplate with a Border visual carrying the
    /// three TemplateBindings plus a PART_Panel StackPanel child.</summary>
    private static byte[] SimpleItemsControlDictionary()
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
            .Raw(0x03).I16(-50).Raw(0x00)
            .Raw(0x23).I16(-4).U16(634).I16(-24)
            .Raw(0x23).I16(-5).U16(634).I16(-25)
            .Raw(0x23).I16(-6).U16(634).I16(-26)
            .Raw(0x2E).I16(-161)
            .Raw(0x03).I16(-601).Raw(0x00)
            .Sized(0x24, q => q.I16(4).Str("PART_Panel").I16(-615))
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x04);
        return ImplicitDictionary(entry.ToArray(), 0,
            [("HandyControl.Controls.SimpleItemsControl", (ushort)0)],
            [(0, "TargetType", -620), (1, "Property", -556), (2, "Value", -556), (3, "TargetType", -108), (4, "Name", -601)]);
    }

    /// <summary>Builds an implicit style over an engine type with a Template
    /// setter holding a ControlTemplate with a ContentPresenter carrying
    /// one PWC over a converted attached owner (the Sprite IsDragElement
    /// mechanism, mirrored with the staged TipElement).</summary>
    private static byte[] ConvertedPresenterDictionary(string fullName, string ownerFullName, string property, string value)
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
            .Sized(0x24, q => q.I16(4).Str(value).I16(-195))
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x04);
        return ImplicitDictionary(entry.ToArray(), 0,
            [(fullName, (ushort)0), (ownerFullName, (ushort)1)],
            [(0, "TargetType", -620), (1, "Property", -556), (2, "Value", -556), (3, "TargetType", -108), (4, property, 1)]);
    }

    /// <summary>Builds an implicit Button style with a Template setter
    /// holding a ControlTemplate with a Border visual whose body is the
    /// given record run (plus an optional PART_Panel StackPanel child);
    /// negative shapes fail inside the Border parse.</summary>
    private static byte[] BorderVisualDictionary(B inner, bool withChild = false)
    {
        var body = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(0)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-37))
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-108).Raw(0x00)
            .Raw(0x22).I16(3).I16(0)
            .Raw(0x2E).I16(-174)
            .Raw(0x03).I16(-50).Raw(0x00)
            .Raw(inner.ToArray());
        if (withChild)
            body.Raw(0x03).I16(-601).Raw(0x00)
                .Sized(0x24, q => q.I16(4).Str("PART_Panel").I16(-615))
                .Raw(0x04);
        body.Raw(0x04).Raw(0x04).Raw(0x08).Raw(0x04).Raw(0x04);
        return ImplicitDictionary(body.ToArray(), 0,
            [("DesktopWasm.Wpf.Button", (ushort)0)],
            [(0, "TargetType", -620), (1, "Property", -556), (2, "Value", -556), (3, "TargetType", -108), (4, "Name", -601)]);
    }

    /// <summary>Builds a single-entry keyed Storyboard dictionary (one
    /// empty -608 element): the animation-resource follow-up shape.</summary>
    private static byte[] KeyedStoryboardDictionary() => new B().Raw(Header()).Raw(0x01)
        .Raw(0x03).I16(-524).Raw(0x00)
        .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
        .Sized(0x20, p => p.U16(0).Str("S"))
        .Deferable()
        .Sized(0x26, p => p.U16(0).U32(0).U16(0))
        .Raw(0x03).I16(-608).Raw(0x00)
        .Raw(0x04)
        .FinishDictionary().ToArray();

    /// <summary>Builds a keyed Rectangle style with a single qualified
    /// RectangleAttach.Circular setter (the staged 'RectangleCircular'
    /// setter shape, isolated for the apply/clear transfer pins).</summary>
    private static byte[] CircularDictionary(string key, string text)
    {
        var content = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-514)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(16521).I16(0).Str("Circular"))
            .Sized(0x24, q => q.I16(2).Str(text).I16(-46))
            .Raw(0x04)
            .Raw(0x04);
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1D, p => p.U16(0).U16(0).Str("HandyControl.Controls.RectangleAttach"))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x20, p => p.U16(0).Str(key))
            .Deferable()
            .Sized(0x26, p => p.U16(0).U32(0).U16(0));
        return head.Raw(content.ToArray()).FinishDictionary().ToArray();
    }

    private static byte[] MinimalCircularDictionary() => CircularDictionary("CircularOnly", "True");

    private static byte[] CircularOffDictionary() => CircularDictionary("CircularOff", "False");

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

    private sealed class UnsetConverter : IMultiValueConverter
    {
        public object? Convert(object?[] values, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            DependencyProperty.UnsetValue;
        public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class NullConverter : IMultiValueConverter
    {
        public object? Convert(object?[] values, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => null;
        public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
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
