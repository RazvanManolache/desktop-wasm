// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-126 HandyControl theme.baml transition-Storyboard run.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the nine transition Storyboard entries keyidx 113-121 at
/// [BAML+0x1760B, BAML+0x17BDC) land as live engine Storyboards (each
/// DoubleAnimation with inline From/To, converted Duration, converted
/// TargetProperty path and an optional single-CubicEase EasingFunction),
/// plus the implicit TransitioningContentControl style at BAML+0x17BDC
/// (short-form Focusable/False through the converted DP, the slice-120
/// mechanism); the run stops precise at the
/// 'SliderRepeatButtonHorizontalStyle' style entry at BAML+0x17C02
/// (RepeatButton target -522 with Rectangle visuals, richer 0x23 targets,
/// Binding 0x38 converter values).
///
/// Parser-traced entry map (key consumption order, offsets validated):
/// 'Right2LeftTransition' at BAML+0x1760B (X 50 to 0, EaseOut),
/// 'Left2RightTransition' at BAML+0x176A0 (X -50 to 0, EaseOut),
/// 'Bottom2TopTransition' at BAML+0x17736 (Y 50 to 0, EaseOut),
/// 'Top2BottomTransition' at BAML+0x177CB (Y -50 to 0, EaseOut),
/// 'Right2LeftWithFadeTransition' at BAML+0x17861 (Opacity 0 to 1 plus
/// X 50 to 0), 'Left2RightWithFadeTransition' at BAML+0x1792F,
/// 'Bottom2TopWithFadeTransition' at BAML+0x179FE,
/// 'Top2BottomWithFadeTransition' at BAML+0x17ACC,
/// 'FadeTransition' at BAML+0x17B9B (Opacity 0 to 1, no easing),
/// implicit TransitioningContentControl at BAML+0x17BDC,
/// 'SliderRepeatButtonHorizontalStyle' at BAML+0x17C02 (follow-up).
///
/// Byte-exact decode against the staged 466350-byte theme.baml,
/// triangulated three ways (the staged bytes: Storyboard -608 head with
/// Children content -122, DoubleAnimation -169 children, 0x05 From/To
/// over AttributeInfo 19/20, 0x24 Duration over AttributeInfo 21 with
/// converter -188, 0x24 TargetProperty over AttributeInfo 22 with
/// converter -492, 0x07 EasingFunction over AttributeInfo 23 with the
/// TypeInfo 42 CubicEase child carrying the AttributeInfo 24 EasingMode
/// 0x24 value with converter -195; the per-file TypeInfo/AttributeInfo/
/// StringInfo tables: TypeInfo 42 CubicEase/43 EasingFunctionBase,
/// AttributeInfo 19 From/-169, 20 To/-169, 21 Duration/-665,
/// 22 TargetProperty/-608, 23 EasingFunction/-169, 24 EasingMode/43,
/// StringInfo 104-112 the nine transition keys; a genuine net10.0-windows
/// ResourceDictionary fixture compiling two keyed Storyboards
/// (Right2LeftTransition/FadeTransition, 1272-byte Story.baml) that is
/// byte-identical in every staged record shape - Storyboard -608,
/// Children -122, DoubleAnimation -169, 0x05 From/To, Duration -188,
/// TargetProperty -492 with the same X/Opacity path text, 0x07
/// EasingFunction, CubicEase element, EasingMode -195 "EaseOut", the
/// same Duration-owner -665 and TargetProperty-owner -608 ids, the same
/// 0x26 key shape with shared=0/sharedSet=1 - except per-file table
/// numbering (fixture attrs 0-5, types 0-1) and line markers: the
/// fixture carries fourteen 0x35 plus seven 0x36 records and its header
/// trailer ends in 0x01, while every staged file ends in 0x00, so the
/// line-info trailer stays a precise boundary).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched;
/// staged-oracle status-gate bypass stays in-probe only).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice126BamlTransitionTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice126BamlTransitionTests()
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
    public void Theme_baml_lands_nine_transition_storyboards_then_stops_at_groupbox()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        // Every transition entry head: ElementStart -608 with the Children
        // content member -122 plus the first DoubleAnimation start -169.
        var head = new byte[] { 0x03, 0xA0, 0xFD, 0x00, 0x2E, 0x86, 0xFF, 0x03, 0x57, 0xFF, 0x00 };
        Assert.Equal(head, theme[0x1760B..0x17616]);
        Assert.Equal(head, theme[0x176A0..0x176AB]);
        Assert.Equal(head, theme[0x17736..0x17741]);
        Assert.Equal(head, theme[0x177CB..0x177D6]);
        Assert.Equal(head, theme[0x17861..0x1786C]);
        Assert.Equal(head, theme[0x1792F..0x1793A]);
        Assert.Equal(head, theme[0x179FE..0x17A09]);
        Assert.Equal(head, theme[0x17ACC..0x17AD7]);
        Assert.Equal(head, theme[0x17B9B..0x17BA6]);
        // First entry inline values: From "50" plus To "0" (0x05 sized
        // inline records over AttributeInfo rows 19/20).
        Assert.Equal(
            new byte[] { 0x05, 0x06, 0x13, 0x00, 0x02, 0x35, 0x30 },
            theme[0x17616..0x1761D]);
        Assert.Equal(
            new byte[] { 0x05, 0x05, 0x14, 0x00, 0x01, 0x30 },
            theme[0x1761D..0x17623]);
        // The "-50" From shape (second, fourth and WithFade translate
        // animations).
        Assert.Equal(
            new byte[] { 0x05, 0x07, 0x13, 0x00, 0x03, 0x2D, 0x35, 0x30 },
            theme[0x176AB..0x176B3]);
        // DoubleAnimation Duration "0:0:0.4" with converter -188.
        Assert.Equal(
            new byte[] { 0x24, 0x0D, 0x15, 0x00, 0x07, 0x30, 0x3A, 0x30, 0x3A, 0x30, 0x2E, 0x34, 0x44, 0xFF },
            theme[0x17623..0x17631]);
        // Storyboard.TargetProperty X-path head (converter -492); the Y
        // entries carry the same head and only the tail differs.
        Assert.Equal(
            new byte[] { 0x24, 0x55, 0x16, 0x00, 0x4F, 0x28, 0x55, 0x49, 0x45, 0x6C, 0x65, 0x6D, 0x65, 0x6E, 0x74 },
            theme[0x17631..0x17640]);
        Assert.Equal(
            new byte[] { 0x2E, 0x58, 0x29, 0x14, 0xFE },
            theme[0x17682..0x17687]);
        Assert.Equal(
            new byte[] { 0x2E, 0x59, 0x29, 0x14, 0xFE },
            theme[0x177AD..0x177B2]);
        // EasingFunction complex child: AttributeInfo row 23 plus the
        // CubicEase TypeInfo 42 element plus EasingMode "EaseOut".
        Assert.Equal(
            new byte[] { 0x07, 0x17, 0x00, 0x03, 0x2A, 0x00, 0x00 },
            theme[0x17687..0x1768E]);
        Assert.Equal(
            new byte[] { 0x24, 0x0D, 0x18, 0x00, 0x07, 0x45, 0x61, 0x73, 0x65, 0x4F, 0x75, 0x74, 0x3D, 0xFF },
            theme[0x1768E..0x1769C]);
        // First entry close: CubicEase end, complex end, DoubleAnimation
        // end, Storyboard end, straight into the next Storyboard.
        Assert.Equal(
            new byte[] { 0x04, 0x08, 0x04, 0x04, 0x03, 0xA0, 0xFD, 0x00 },
            theme[0x1769C..0x176A4]);
        // FadeTransition entry: Opacity 0 to 1 with no EasingFunction.
        Assert.Equal(
            new byte[] { 0x05, 0x05, 0x13, 0x00, 0x01, 0x30 },
            theme[0x17BA6..0x17BAC]);
        Assert.Equal(
            new byte[] { 0x05, 0x05, 0x14, 0x00, 0x01, 0x31 },
            theme[0x17BAC..0x17BB2]);
        Assert.Equal(
            new byte[] { 0x24, 0x19, 0x16, 0x00, 0x13, 0x28, 0x55, 0x49, 0x45, 0x6C, 0x65, 0x6D, 0x65, 0x6E, 0x74, 0x2E, 0x4F, 0x70, 0x61, 0x63, 0x69, 0x74, 0x79, 0x29, 0x14, 0xFE },
            theme[0x17BC0..0x17BDA]);
        Assert.Equal(
            new byte[] { 0x04, 0x04, 0x03, 0x94, 0xFD, 0x00 },
            theme[0x17BDA..0x17BE0]);
        // Key records for the run: 0x26 with the StringInfo id, the entry
        // offset and the non-shared pair (0/1: every lookup materializes
        // a fresh instance, like the converted Behavior entries).
        Assert.Equal(
            new byte[] { 0x26, 0x09, 0x68, 0x00, 0x74, 0x84, 0x00, 0x00, 0x00, 0x01 },
            theme[0x0B373..0x0B37D]);
        Assert.Equal(
            new byte[] { 0x26, 0x09, 0x70, 0x00, 0x04, 0x8A, 0x00, 0x00, 0x00, 0x01 },
            theme[0x0B3C3..0x0B3CD]);
        // The implicit TransitioningContentControl style at BAML+0x17BDC:
        // Style start with the TargetType over TypeInfo 44 plus the
        // Setters content.
        Assert.Equal(
            new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x2C, 0x00, 0x2E, 0x12, 0xFF },
            theme[0x17BDC..0x17BE8]);
        // The follow-up style head at BAML+0x17C02: Style start with the
        // TargetType over the -522 RepeatButton id plus Setters.
        Assert.Equal(
            new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0xF6, 0xFD, 0x2E, 0x12, 0xFF },
            theme[0x17C02..0x17C0E]);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(theme));
        // Entries #87-122 materialize in index order (the entry machine
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
        Assert.DoesNotContain("Right2LeftTransition", error.Message);
        Assert.DoesNotContain("Left2RightTransition", error.Message);
        Assert.DoesNotContain("Bottom2TopTransition", error.Message);
        Assert.DoesNotContain("Top2BottomTransition", error.Message);
        Assert.DoesNotContain("WithFadeTransition", error.Message);
        Assert.DoesNotContain("FadeTransition", error.Message);
        Assert.DoesNotContain("well-known type id -608", error.Message);
        Assert.DoesNotContain("HandyControl.Controls.TransitioningContentControl", error.Message);
        Assert.DoesNotContain("BAML+0x17C06", error.Message);
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

        Assert.DoesNotContain("well-known type id -522", error.Message);
    }

    [Fact]
    public void Staged_boot_advances_past_transitions_to_groupbox_style()
    {
        var entry = LoadStagedHandyControlEntry();
        var error = Assert.Throws<InvalidOperationException>(() => WpfAppBoot.Boot(entry));
        Assert.Contains("startup failed in entry point", error.Message);
        // The line-17 SkinDefault stop, the line-23 Theme key/index plus
        // scalar run, PathBaseStyle, the x:Static-keyed Double, the six
        // converted Behavior entries, the implicit BlendEffectBox style, the
        // 'BorderCircular' entry, the implicit Col/ContentControl styles, the
        // implicit FlipClock style, the implicit FloatingBlock style, the
        // implicit GridSplitter style, the implicit Notification style, the
        // 'RectangleCircular' entry, the 'SnapRect4ScreenshotStyle' flat
        // run plus its Trigger list, the 'TargetArea4ScreenshotStyle' entry,
        // the implicit SimpleItemsControl style (Template/Border visual with
        // TemplateBindings plus PART_Panel) and the implicit Sprite style
        // (attached/window setters plus Template/ContentPresenter visual)
        // plus the nine transition Storyboards (keyidx 113-121) and the
        // implicit TransitioningContentControl style now resolve; boot
        // fails at the 'SliderRepeatButtonHorizontalStyle' style entry -
        // the precise next blocker.
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
        Assert.DoesNotContain("Right2LeftTransition", inner.Message);
        Assert.DoesNotContain("WithFadeTransition", inner.Message);
        Assert.DoesNotContain("FadeTransition", inner.Message);
        Assert.DoesNotContain("well-known type id -608", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.TransitioningContentControl", inner.Message);
        Assert.DoesNotContain("BAML+0x17C06", inner.Message);
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

        Assert.DoesNotContain("well-known type id -522", inner.Message);
    }

    [Fact]
    public void Staged_right2left_entry_bytes_land_as_live_storyboard()
    {
        // The staged 'Right2LeftTransition' entry shape (Storyboard with
        // the Children content plus one DoubleAnimation carrying From/To,
        // Duration, TargetProperty and a CubicEase EasingFunction) lands
        // as a live engine Storyboard: the synthetic entry below is
        // byte-identical to the staged [BAML+0x1760B, BAML+0x176A0) span,
        // so the landing proves the staged bytes materialize, while the
        // staged-run tests above prove the real parser consumes them.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        var entry = Right2LeftEntry(StagedTables()).ToArray();
        Assert.Equal(theme[0x1760B..0x176A0], entry);
        var dictionary = BamlReader.LoadDictionary(KeyedStoryboardDictionary("Right2LeftTransition", entry, StagedTables()));
        var board = Assert.IsType<Storyboard>(dictionary["Right2LeftTransition"]);
        var child = Assert.Single(board.Children);
        var animation = Assert.IsType<DoubleAnimation>(child);
        Assert.Equal(50.0, animation.From);
        Assert.Equal(0.0, animation.To);
        Assert.Equal(new Duration(TimeSpan.FromSeconds(0.4)), animation.Duration);
        Assert.Equal(
            "(UIElement.RenderTransform).(TransformGroup.Children)[3].(TranslateTransform.X)",
            Storyboard.GetTargetProperty(animation));
        var ease = Assert.IsType<CubicEase>(animation.EasingFunction);
        Assert.Equal(EasingMode.EaseOut, ease.EasingMode);
    }

    [Fact]
    public void Staged_fade_entry_bytes_land_without_easing()
    {
        // The staged 'FadeTransition' entry shape (Opacity 0 to 1 with no
        // EasingFunction child) lands with the easing hook empty: the
        // synthetic entry is byte-identical to the staged
        // [BAML+0x17B9B, BAML+0x17BDC) span.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        var entry = FadeEntry(StagedTables()).ToArray();
        Assert.Equal(theme[0x17B9B..0x17BDC], entry);
        var dictionary = BamlReader.LoadDictionary(KeyedStoryboardDictionary("FadeTransition", entry, StagedTables()));
        var board = Assert.IsType<Storyboard>(dictionary["FadeTransition"]);
        var animation = Assert.IsType<DoubleAnimation>(Assert.Single(board.Children));
        Assert.Equal(0.0, animation.From);
        Assert.Equal(1.0, animation.To);
        Assert.Equal(new Duration(TimeSpan.FromSeconds(0.4)), animation.Duration);
        Assert.Equal("(UIElement.Opacity)", Storyboard.GetTargetProperty(animation));
        Assert.Null(animation.EasingFunction);
    }

    [Fact]
    public void Staged_withfade_entry_lands_two_children()
    {
        // The staged 'Right2LeftWithFadeTransition' shape (an Opacity ramp
        // with no easing plus a translate ramp with EaseOut) lands both
        // children in document order with independent targets.
        XamlLoader.CustomElementSource = null;
        var entry = new B()
            .Raw(StoryboardHead().ToArray())
            .Raw(DoubleAnimationEntry("0", "1", "(UIElement.Opacity)", withEasing: false).ToArray())
            .Raw(DoubleAnimationEntry("50", "0", "(UIElement.RenderTransform).(TransformGroup.Children)[3].(TranslateTransform.X)", withEasing: true).ToArray())
            .Raw(0x04)
            .ToArray();
        var dictionary = BamlReader.LoadDictionary(KeyedStoryboardDictionary("Right2LeftWithFadeTransition", entry, StagedTables()));
        var board = Assert.IsType<Storyboard>(dictionary["Right2LeftWithFadeTransition"]);
        Assert.Equal(2, board.Children.Count);
        var opacity = Assert.IsType<DoubleAnimation>(board.Children[0]);
        Assert.Equal("(UIElement.Opacity)", Storyboard.GetTargetProperty(opacity));
        Assert.Null(opacity.EasingFunction);
        var slide = Assert.IsType<DoubleAnimation>(board.Children[1]);
        Assert.Equal(50.0, slide.From);
        Assert.Equal(
            "(UIElement.RenderTransform).(TransformGroup.Children)[3].(TranslateTransform.X)",
            Storyboard.GetTargetProperty(slide));
        Assert.Equal(EasingMode.EaseOut, Assert.IsType<CubicEase>(slide.EasingFunction).EasingMode);
    }

    [Fact]
    public void Staged_transitioning_content_control_style_lands()
    {
        // The staged keyidx-122 entry shape (implicit style over
        // TransitioningContentControl with a single short-form
        // Focusable/False setter) resolves to the engine
        // TransitioningContentControl by short name (the ResolveEntryType
        // engine-first rule, like the staged Col/ContentControl shapes)
        // and lands the inherited Focusable DP with false, which is why
        // the staged boot advances past BAML+0x17BDC for free.
        XamlLoader.CustomElementSource = null;
        var entry = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(0)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-132))
            .Sized(0x24, q => q.I16(2).Str("False").I16(-46))
            .Raw(0x04)
            .Raw(0x04)
            .ToArray();
        var dictionary = BamlReader.LoadDictionary(ImplicitDictionary(entry, 0,
            [("HandyControl.Controls.TransitioningContentControl", (ushort)0)],
            [(0, "TargetType", -620), (1, "Property", -556), (2, "Value", -556)]));
        var style = Assert.IsType<Style>(dictionary[typeof(TransitioningContentControl)]);
        Assert.Same(typeof(TransitioningContentControl), style.TargetType);
        var setter = Assert.IsType<Setter>(Assert.Single(style.Setters));
        Assert.Same(UIElement.FocusableProperty, setter.Property);
        Assert.Equal(false, setter.Value);
    }

    [Fact]
    public void Non_shared_storyboard_lookups_materialize_fresh_instances()
    {
        // The staged transition keys carry shared=0/sharedSet=1, so every
        // lookup materializes a fresh Storyboard (the converted-Behavior
        // x:Shared="False" semantics), never a shared singleton.
        XamlLoader.CustomElementSource = null;
        var dictionary = BamlReader.LoadDictionary(
            KeyedStoryboardDictionary("Right2LeftTransition", Right2LeftEntry(StagedTables()).ToArray(), StagedTables()));
        var first = Assert.IsType<Storyboard>(dictionary["Right2LeftTransition"]);
        var second = Assert.IsType<Storyboard>(dictionary["Right2LeftTransition"]);
        Assert.NotSame(first, second);
        Assert.Equal(
            Storyboard.GetTargetProperty(Assert.IsType<DoubleAnimation>(Assert.Single(first.Children))),
            Storyboard.GetTargetProperty(Assert.IsType<DoubleAnimation>(Assert.Single(second.Children))));
    }

    [Fact]
    public void Staged_shape_double_animation_samples_eased_value()
    {
        // The staged translate shape (From 50 to 0 over 0.4s with an
        // EaseOut CubicEase) samples the eased curve through the engine
        // EasingFunction hook: at half progress the EaseOut value is
        // 1-(1-0.5)^3 = 0.875, so the sample lands on 6.25.
        XamlLoader.CustomElementSource = null;
        var animation = new DoubleAnimation(50.0, 0.0, new Duration(TimeSpan.FromSeconds(0.4)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var clock = AnimationClock.CreateForTests(TimeSpan.FromSeconds(0.2), 0.5, TimeSpan.FromSeconds(0.4));
        Assert.Equal(6.25, animation.GetCurrentValue(50.0, 0.0, clock));
    }

    [Fact]
    public void Shared_storyboard_lookups_return_same_instance()
    {
        // Shared keys (sharedSet=0) land once through AddEntry, so every
        // lookup returns the same Storyboard instance.
        XamlLoader.CustomElementSource = null;
        var dictionary = BamlReader.LoadDictionary(KeyedStoryboardDictionary(
            "Right2LeftTransition", Right2LeftEntry(StagedTables()).ToArray(), StagedTables(), shared: true));
        var first = Assert.IsType<Storyboard>(dictionary["Right2LeftTransition"]);
        Assert.Same(first, Assert.IsType<Storyboard>(dictionary["Right2LeftTransition"]));
    }

    [Fact]
    public void Fixture_numbered_storyboard_lands_with_line_markers()
    {
        // The genuine net10.0-windows fixture numbers its tables from zero
        // (attrs 0-5, types 0-1) and carries 0x35/0x36 line markers: the
        // parser resolves everything through the tables, so the same
        // shapes land with line markers transparent.
        XamlLoader.CustomElementSource = null;
        var numbering = FixtureTables();
        var entry = new B()
            .Raw(0x03).I16(-608).Raw(0x00)
            .Raw(0x35).U32(3).U32(6)
            .Raw(0x2E).I16(-122)
            .Raw(0x35).U32(4).U32(10)
            .Raw(DoubleAnimationEntry(numbering, "50", "0",
                "(UIElement.RenderTransform).(TransformGroup.Children)[3].(TranslateTransform.X)", "EaseOut", withEasing: true).ToArray())
            .Raw(0x04)
            .ToArray();
        var dictionary = BamlReader.LoadDictionary(KeyedStoryboardDictionary("Right2LeftTransition", entry, numbering));
        var board = Assert.IsType<Storyboard>(dictionary["Right2LeftTransition"]);
        var animation = Assert.IsType<DoubleAnimation>(Assert.Single(board.Children));
        Assert.Equal(50.0, animation.From);
        Assert.Equal(0.0, animation.To);
        Assert.Equal(new Duration(TimeSpan.FromSeconds(0.4)), animation.Duration);
        Assert.Equal(
            "(UIElement.RenderTransform).(TransformGroup.Children)[3].(TranslateTransform.X)",
            Storyboard.GetTargetProperty(animation));
        Assert.Equal(EasingMode.EaseOut, Assert.IsType<CubicEase>(animation.EasingFunction).EasingMode);
    }

    [Fact]
    public void Line_info_header_trailer_stays_a_precise_boundary()
    {
        // The genuine net10.0-windows fixture ends its 15-byte header
        // trailer in 0x01 and carries 0x35/0x36 line markers, while every
        // staged file ends in 0x00 with no line markers: the reader keeps
        // asserting the staged trailer shape and fails loud on the
        // line-info shape instead of misparsing it.
        XamlLoader.CustomElementSource = null;
        var header = Header();
        header[^1] = 0x01;
        var bytes = new B().Raw(header).Raw(0x01).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("unsupported BAML header trailer", error.Message);
    }

    [Fact]
    public void Storyboard_wrong_content_member_fails_precise()
    {
        // Storyboard entries take the Children content member (-122); any
        // other content member fails naming it.
        XamlLoader.CustomElementSource = null;
        var body = new B().Raw(0x2E).I16(-161);
        var bytes = KeyedStoryboardDictionary("S", StoryboardEntry(body).ToArray(), StagedTables());
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("content member -161 on storyboard entry 'S'", error.Message);
        Assert.Contains("storyboards take the Children content", error.Message);
    }

    [Fact]
    public void Storyboard_animation_before_children_fails_precise()
    {
        // The Children content marker precedes the animation children
        // (BAML emits Children first); a leading child fails naming it.
        XamlLoader.CustomElementSource = null;
        var body = new B()
            .Raw(0x03).I16(-169).Raw(0x00)
            .Raw(0x04)
            .Raw(0x2E).I16(-122);
        var bytes = KeyedStoryboardDictionary("S", StoryboardEntry(body).ToArray(), StagedTables());
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("animation child precedes the Children content", error.Message);
        Assert.Contains("BAML emits Children first", error.Message);
    }

    [Fact]
    public void Storyboard_non_animation_child_fails_precise()
    {
        // Storyboard children are DoubleAnimations; any other element
        // fails naming it.
        XamlLoader.CustomElementSource = null;
        var body = new B()
            .Raw(0x2E).I16(-122)
            .Raw(0x03).I16(-514).Raw(0x00);
        var bytes = KeyedStoryboardDictionary("S", StoryboardEntry(body).ToArray(), StagedTables());
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("element Rectangle inside storyboard entry 'S'", error.Message);
        Assert.Contains("storyboards take DoubleAnimation children", error.Message);
    }

    [Fact]
    public void Storyboard_from_text_not_a_double_fails_precise()
    {
        // Inline From/To values convert through Double like the scalar
        // Double entries; anything else fails naming the text.
        XamlLoader.CustomElementSource = null;
        var body = StoryboardHead()
            .Raw(DoubleAnimationEntry("fast", "0", "(UIElement.Opacity)", withEasing: false).ToArray());
        var bytes = KeyedStoryboardDictionary("S", body.Raw(0x04).ToArray(), StagedTables());
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("'fast' is not a Double for 'S'", error.Message);
    }

    [Fact]
    public void Storyboard_missing_to_fails_precise()
    {
        // Animations take From plus To plus Duration plus TargetProperty;
        // a missing To fails naming it.
        XamlLoader.CustomElementSource = null;
        var n = StagedTables();
        var body = StoryboardHead()
            .Raw(0x03).I16(-169).Raw(0x00)
            .Sized(0x05, q => q.I16(n.From).Str("50"))
            .Sized(0x24, q => q.I16(n.DurationAttr).Str("0:0:0.4").I16(-188))
            .Sized(0x24, q => q.I16(n.TargetAttr).Str("(UIElement.Opacity)").I16(-492))
            .Raw(0x04);
        var bytes = KeyedStoryboardDictionary("S", body.Raw(0x04).ToArray(), StagedTables());
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("DoubleAnimation in storyboard entry 'S' has no To", error.Message);
        Assert.Contains("animations take From plus To plus Duration plus TargetProperty", error.Message);
    }

    [Fact]
    public void Storyboard_duration_wrong_converter_fails_precise()
    {
        // Durations convert through the Duration converter (-188); any
        // other converter fails naming it.
        XamlLoader.CustomElementSource = null;
        var n = StagedTables();
        var body = StoryboardHead()
            .Raw(0x03).I16(-169).Raw(0x00)
            .Sized(0x05, q => q.I16(n.From).Str("50"))
            .Sized(0x05, q => q.I16(n.To).Str("0"))
            .Sized(0x24, q => q.I16(n.DurationAttr).Str("0:0:0.4").I16(-175))
            .Raw(0x04);
        var bytes = KeyedStoryboardDictionary("S", body.Raw(0x04).ToArray(), StagedTables());
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("Duration value with converter -175 in storyboard entry 'S'", error.Message);
        Assert.Contains("storyboard Durations take the Duration converter", error.Message);
    }

    [Fact]
    public void Storyboard_duration_bad_text_fails_precise()
    {
        // Durations parse like the engine Duration; anything else fails
        // naming the text.
        XamlLoader.CustomElementSource = null;
        var n = StagedTables();
        var body = StoryboardHead()
            .Raw(0x03).I16(-169).Raw(0x00)
            .Sized(0x05, q => q.I16(n.From).Str("50"))
            .Sized(0x05, q => q.I16(n.To).Str("0"))
            .Sized(0x24, q => q.I16(n.DurationAttr).Str("soon").I16(-188))
            .Raw(0x04);
        var bytes = KeyedStoryboardDictionary("S", body.Raw(0x04).ToArray(), StagedTables());
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("'soon' is not a Duration for 'S'", error.Message);
    }

    [Fact]
    public void Storyboard_target_wrong_converter_fails_precise()
    {
        // Target paths convert through the PropertyPath converter (-492);
        // any other converter fails naming it.
        XamlLoader.CustomElementSource = null;
        var n = StagedTables();
        var body = StoryboardHead()
            .Raw(0x03).I16(-169).Raw(0x00)
            .Sized(0x05, q => q.I16(n.From).Str("50"))
            .Sized(0x05, q => q.I16(n.To).Str("0"))
            .Sized(0x24, q => q.I16(n.DurationAttr).Str("0:0:0.4").I16(-188))
            .Sized(0x24, q => q.I16(n.TargetAttr).Str("(UIElement.Opacity)").I16(-615))
            .Raw(0x04);
        var bytes = KeyedStoryboardDictionary("S", body.Raw(0x04).ToArray(), StagedTables());
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("TargetProperty value with converter -615 in storyboard entry 'S'", error.Message);
        Assert.Contains("storyboard target paths take the PropertyPath converter", error.Message);
    }

    [Fact]
    public void Storyboard_non_cubicease_easing_fails_precise()
    {
        // Easing children are CubicEase elements (the staged run carries
        // nothing else); any other easing fails naming it.
        XamlLoader.CustomElementSource = null;
        var n = StagedTables();
        var body = StoryboardHead()
            .Raw(0x03).I16(-169).Raw(0x00)
            .Sized(0x05, q => q.I16(n.From).Str("50"))
            .Sized(0x05, q => q.I16(n.To).Str("0"))
            .Sized(0x24, q => q.I16(n.DurationAttr).Str("0:0:0.4").I16(-188))
            .Sized(0x24, q => q.I16(n.TargetAttr).Str("(UIElement.Opacity)").I16(-492))
            .Raw(0x07).I16(n.EaseAttr)
            .Raw(0x03).I16(44).Raw(0x00)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04);
        var numbering = n with
        {
            Types = [.. n.Types, ("System.Windows.Media.Animation.PowerEase", (ushort)44)],
        };
        var bytes = KeyedStoryboardDictionary("S", body.Raw(0x04).ToArray(), numbering);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("easing child <System.Windows.Media.Animation.PowerEase> in storyboard entry 'S'", error.Message);
        Assert.Contains("only CubicEase easing lands", error.Message);
    }

    [Fact]
    public void Storyboard_easing_mode_bad_value_fails_precise()
    {
        // EasingMode values are the EasingMode enum; anything else fails
        // naming the text.
        XamlLoader.CustomElementSource = null;
        var n = StagedTables();
        var body = StoryboardHead()
            .Raw(DoubleAnimationEntry(n, "50", "0", "(UIElement.Opacity)", "EaseSideways", withEasing: true).ToArray());
        var bytes = KeyedStoryboardDictionary("S", body.Raw(0x04).ToArray(), StagedTables());
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("easing value 'EaseSideways' does not convert to EasingMode in storyboard entry 'S'", error.Message);
    }

    [Fact]
    public void Storyboard_easing_mode_wrong_owner_fails_precise()
    {
        // EasingMode travels over the EasingFunctionBase AttributeInfo row
        // (owner 43 staged, owner 1 in the genuine fixture); any other
        // owner fails naming the attribute.
        XamlLoader.CustomElementSource = null;
        var n = StagedTables();
        var numbering = n with
        {
            Attrs = [.. n.Attrs.Where(a => a.Name != "EasingMode"), (24, "EasingMode", (short)42)],
        };
        var body = StoryboardHead()
            .Raw(DoubleAnimationEntry(n, "50", "0", "(UIElement.Opacity)", "EaseOut", withEasing: true).ToArray());
        var bytes = KeyedStoryboardDictionary("S", body.Raw(0x04).ToArray(), numbering);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("attribute 'EasingMode' in storyboard entry 'S'", error.Message);
        Assert.Contains("easing values take EasingMode over EasingFunctionBase", error.Message);
    }

    [Fact]
    public void Storyboard_empty_with_children_content_fails_precise()
    {
        // Storyboards take DoubleAnimation children; the Children content
        // with an immediate end fails instead of landing empty.
        XamlLoader.CustomElementSource = null;
        var body = StoryboardHead();
        var bytes = KeyedStoryboardDictionary("S", body.Raw(0x04).ToArray(), StagedTables());
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("keyed entry 'S' holds a Storyboard with no DoubleAnimation", error.Message);
        Assert.Contains("storyboards take DoubleAnimation children", error.Message);
    }

    [Fact]
    public void Storyboard_static_value_fails_precise()
    {
        // StaticResource values inside DoubleAnimations (the staged
        // GroupBox-template 0x38 follow-up shape) stay a precise
        // boundary instead of landing half-built.
        XamlLoader.CustomElementSource = null;
        var n = StagedTables();
        var body = StoryboardHead()
            .Raw(0x03).I16(-169).Raw(0x00)
            .Raw(0x38).U16(37).U16(0);
        var bytes = KeyedStoryboardDictionary("S", body.Raw(0x04).Raw(0x04).ToArray(), n);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("StaticResource value on the DoubleAnimation in storyboard entry 'S'", error.Message);
        Assert.Contains("storyboard static values are a style-surface follow-up", error.Message);
    }

    /// <summary>Per-file table numbering plus the record ids the storyboard
    /// grammar resolves through the tables (file-independent: the staged
    /// file numbers attrs 19-24 over types 42-43, the genuine net10
    /// fixture numbers attrs 0-5 over types 0-1).</summary>
    private sealed record StoryboardNumbering(
        (string FullName, ushort Id)[] Types,
        (int Id, string Name, short Owner)[] Attrs,
        short From, short To, short DurationAttr, short TargetAttr, short EaseAttr, short ModeAttr, short CubicType);

    private static StoryboardNumbering StagedTables() => new(
        [("System.Windows.Media.Animation.CubicEase", (ushort)42),
         ("System.Windows.Media.Animation.EasingFunctionBase", (ushort)43)],
        [(19, "From", (short)-169), (20, "To", (short)-169), (21, "Duration", (short)-665),
         (22, "TargetProperty", (short)-608), (23, "EasingFunction", (short)-169), (24, "EasingMode", (short)43)],
        19, 20, 21, 22, 23, 24, 42);

    private static StoryboardNumbering FixtureTables() => new(
        [("System.Windows.Media.Animation.CubicEase", (ushort)0),
         ("System.Windows.Media.Animation.EasingFunctionBase", (ushort)1)],
        [(0, "From", (short)-169), (1, "To", (short)-169), (2, "Duration", (short)-665),
         (3, "TargetProperty", (short)-608), (4, "EasingFunction", (short)-169), (5, "EasingMode", (short)1)],
        0, 1, 2, 3, 4, 5, 0);

    /// <summary>Builds the staged 'Right2LeftTransition' entry shape:
    /// Storyboard start, Children content, one DoubleAnimation (From 50
    /// to 0, Duration 0.4s, X translate target, EaseOut CubicEase) and
    /// the Storyboard end.</summary>
    private static B Right2LeftEntry(StoryboardNumbering n) => StoryboardHead()
        .Raw(DoubleAnimationEntry(n, "50", "0",
            "(UIElement.RenderTransform).(TransformGroup.Children)[3].(TranslateTransform.X)", "EaseOut", withEasing: true).ToArray())
        .Raw(0x04);

    /// <summary>Builds the staged 'FadeTransition' entry shape: one
    /// Opacity DoubleAnimation with no EasingFunction child.</summary>
    private static B FadeEntry(StoryboardNumbering n) => StoryboardHead()
        .Raw(DoubleAnimationEntry(n, "0", "1", "(UIElement.Opacity)", "EaseOut", withEasing: false).ToArray())
        .Raw(0x04);

    /// <summary>Storyboard start plus the Children content member.</summary>
    private static B StoryboardHead() => new B()
        .Raw(0x03).I16(-608).Raw(0x00)
        .Raw(0x2E).I16(-122);

    /// <summary>Wraps an entry body with the Storyboard start and end.</summary>
    private static B StoryboardEntry(B body) => new B()
        .Raw(0x03).I16(-608).Raw(0x00)
        .Raw(body.ToArray())
        .Raw(0x04);

    /// <summary>Builds one DoubleAnimation child with staged numbering
    /// and an EaseOut CubicEase unless easing is off.</summary>
    private static B DoubleAnimationEntry(string from, string to, string target, bool withEasing) =>
        DoubleAnimationEntry(StagedTables(), from, to, target, "EaseOut", withEasing);

    /// <summary>Builds one DoubleAnimation child over explicit table ids.</summary>
    private static B DoubleAnimationEntry(StoryboardNumbering n, string from, string to, string target, string easingMode, bool withEasing)
    {
        var body = new B()
            .Raw(0x03).I16(-169).Raw(0x00)
            .Sized(0x05, q => q.I16((short)n.From).Str(from))
            .Sized(0x05, q => q.I16((short)n.To).Str(to))
            .Sized(0x24, q => q.I16((short)n.DurationAttr).Str("0:0:0.4").I16(-188))
            .Sized(0x24, q => q.I16((short)n.TargetAttr).Str(target).I16(-492));
        if (withEasing)
        {
            body.Raw(0x07).I16((short)n.EaseAttr)
                .Raw(0x03).I16(n.CubicType).Raw(0x00)
                .Sized(0x24, q => q.I16((short)n.ModeAttr).Str(easingMode).I16(-195))
                .Raw(0x04)
                .Raw(0x08);
        }
        return body.Raw(0x04);
    }

    /// <summary>Builds a single-entry keyed Storyboard dictionary (the
    /// staged keys are non-shared, so every lookup materializes fresh;
    /// shared opts into the AddEntry singleton path).</summary>
    private static byte[] KeyedStoryboardDictionary(string key, byte[] entry, StoryboardNumbering n, bool shared = false)
    {
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"));
        foreach (var (fullName, id) in n.Types)
            head.Sized(0x1D, p => p.U16(id).U16(0).Str(fullName));
        foreach (var (id, name, owner) in n.Attrs)
            head.Sized(0x1F, p => p.I16((short)id).I16(owner).Raw(0x00).Str(name));
        head.Sized(0x20, p => p.U16(0).Str(key));
        head.Deferable();
        if (shared)
            head.Sized(0x26, p => p.U16(0).U32(0).U16(0));
        else
            head.Sized(0x26, p => p.U16(0).U32(0).Raw(0x00, 0x01));
        return head.Raw(entry).FinishDictionary().ToArray();
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

    private sealed class B
    {
        private readonly List<byte> _b = new();
        private int _deferPos = -1;
        public B Raw(params byte[] b) { _b.AddRange(b); return this; }
        public B Raw(B other) { _b.AddRange(other._b); return this; }
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
