// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-136 HandyControl theme.baml Wrap/Dock run.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the keyed 'WrapHorizontalItemsPanelTemplate' entry keyidx 145 at
/// [BAML+0x19022, BAML+0x19052) (WrapPanel visual at BAML+0x19029 carrying
/// the HorizontalAlignment/VerticalAlignment "Center" EnumConverter PWCs
/// plus the FocusVisualStyle x:Null complex), its bare-orientation sibling
/// keyidx 146 'WrapVerticalItemsPanelTemplate' at [BAML+0x19052,
/// BAML+0x19091) (WrapPanel visual carrying Orientation "Vertical" plus the
/// same Center pair), and keyidx 147 'DockItemsPanelTemplate' at
/// [BAML+0x19091, BAML+0x190B2) (DockPanel visual carrying LastChildFill
/// "True" plus the same FocusVisualStyle complex) land as live engine
/// ItemsPanelTemplates; the generic keyed-entry paths behind them land with
/// zero new code (keyidx 148 'ResizeGripperForeground' LinearGradientBrush
/// at [BAML+0x190B2, BAML+0x1912A), keyidx 149 'SineEaseOut' SineEase at
/// [BAML+0x1912A, BAML+0x1913D)), and slice-137 landed the keyed
/// 'BadgeDotTemplate' ControlTemplate entry keyidx 150 at [BAML+0x1913D,
/// BAML+0x191BC) plus the keyed 'ColorPickerSliderRepeatButtonBaseStyle'
/// Style entry keyidx 151 at [BAML+0x191BC, BAML+0x19279) plus the keyed
/// 'ColorPickerSliderThumbTemplate' entry keyidx 152 at [BAML+0x19279,
/// BAML+0x192B2) with its Thumb TargetType and packed-White Border root,
/// so the run lands the keyed 'ColorPickerRainbowBrush' brush keyidx 153
/// at BAML+0x192B2 in slice-139 (WPF-default points, zero first offset),
/// then lands the keyed 'ColorPickerOpacityBrush' brush keyidx 154 at
/// BAML+0x19383 with its checker DrawingGroup in slice-140, and stops at
/// the keyed 'CoverFlowBaseStyle' Style keyidx 155 at BAML+0x19497
/// (template Border Viewport3D child well-known id -729).
///
/// Byte-exact decode against the staged 466350-byte theme.baml,
/// triangulated three ways (the staged bytes: entry heads 03 B6 FE, the
/// VisualTree content member 2E 52 FF, the WrapPanel visuals 03 1A FD, the
/// DockPanel visual 03 5D FF, the HAlign/VAlign PWCs 24 0C D0/C8 FF over the
/// well-known -48/-56 members, the Orientation PWC 24 0E 36 00 over
/// per-file AttributeInfo row 54 owned by WrapPanel -742, the LastChildFill
/// PWC 24 0A D8 FF over member -40, the FocusVisualStyle x:Null complexes
/// 07 28 00 with the NullExtension child 03 5A FE, the three-entry closes,
/// key records 0x26 key ids 0x87-0x8C with shared 0/0 plus the next Style
/// head 03 94 FD at 0x191BC; the live upstream HandyControl
/// ItemsPanelTemplate.xaml block (the same two WrapPanel templates and the
/// DockPanel template with the same values), Window.xaml
/// ResizeGripperForeground block (StartPoint 0,0.25 / EndPoint 1,0.75 plus
/// the same three GradientStops) and BadgeBaseStyle.xaml SineEaseOut +
/// BadgeDotTemplate blocks (EasingMode EaseOut; TargetType hc:Badge with the
/// SimplePanel root and the ContentPresenter first child); a genuine
/// net10.0-windows ResourceDictionary fixture is not rebuilt this slice -
/// the staged shapes reuse the slice-133 brush grammar and the
/// converted-element grammar verbatim).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched;
/// staged-oracle status-gate bypass stays in-probe only).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice136BamlWrapTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice136BamlWrapTests()
    {
        XamlLoader.CustomElementSource = null;
    }

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    [Fact]
    public void Staged_manifest_keeps_six_staged_two_absent()
    {
        // Live staged-oracle gate: HandyControl stays conversion-failed
        // with exactly the same two absent upstream binaries, so the run
        // below decodes the same staged bytes the earlier slices pinned.
        // The 466350-byte theme.baml length plus the sha256 map below pin
        // the input. Loading the staged entry first guarantees the
        // HandyControl assembly is present however the runner orders the
        // tests.
        LoadStagedHandyControlEntry();
        var apps = StagedHandyAppsDirectory();
        using var document = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
        var root = document.RootElement;
        Assert.Equal("conversion-failed", root.GetProperty("status").GetString());
        var assemblies = root.GetProperty("assemblies").EnumerateArray().ToArray();
        Assert.Equal(6, assemblies.Length);
        var names = assemblies.Select(a => a.GetProperty("name").GetString()).ToArray();
        Assert.Equal(
            new[] { "HandyControlDemo", "HandyControl", "GalaSoft.MvvmLight", "ICSharpCode.AvalonEdit", "HandyControlDemoCode", "Newtonsoft.Json" },
            names);
        foreach (var assembly in assemblies)
        {
            var file = assembly.GetProperty("file").GetString()!;
            var expected = assembly.GetProperty("sha256").GetString()!;
            var actual = Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(System.IO.Path.Combine(apps, file))));
            Assert.True(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                $"hash mismatch in staged '{file}': {actual} != {expected}");
        }
        var gaps = root.GetProperty("gaps").EnumerateArray()
            .Select(g => g.GetProperty("detail").GetString()).ToArray();
        Assert.Equal(2, gaps.Length);
        Assert.Contains(gaps, g => g!.Contains("System.Security.Permissions", StringComparison.Ordinal));
        Assert.Contains(gaps, g => g!.Contains("System.Windows.Extensions", StringComparison.Ordinal));
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
    }

    [Fact]
    public void Theme_baml_lands_wrap_dock_brush_ease_then_badge_stop()
    {
        // Staged framing pins for the two WrapPanel plus one DockPanel
        // ItemsPanelTemplate entries keyidx 145-147 spanning
        // [0x19022, 0x190B2): the 03 B6 FE entry heads, the VisualTree
        // content member, the 03 1A FD WrapPanel visuals, the 03 5D FF
        // DockPanel visual, the staged HAlign/VAlign PWCs ("Center" through
        // the EnumConverter over the well-known -48/-56 members), the
        // staged Orientation PWC ("Vertical" through the EnumConverter over
        // per-file row 54, vertical entry only), the LastChildFill PWC
        // ("True" through the BooleanConverter), the FocusVisualStyle x:Null
        // complexes, the closes, the 0x26 key records for 145-147 plus the
        // 148-150 keys, the brush/ease/badge entry spans and the next Style
        // head at 0x191BC asserting the exact new frontier. Loading the
        // staged entry first guarantees the HandyControl assembly is
        // present however the runner orders tests.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");

        var wrapHHead = new byte[] { 0x03, 0xB6, 0xFE, 0x00, 0x2E, 0x52, 0xFF, 0x03, 0x1A, 0xFD, 0x00 };
        Assert.Equal(wrapHHead, theme[0x19022..0x1902D]);
        var halign = new byte[] { 0x24, 0x0C, 0xD0, 0xFF, 0x06, 0x43, 0x65, 0x6E, 0x74, 0x65, 0x72, 0x3D, 0xFF };
        Assert.Equal(halign, theme[0x1902D..0x1903A]);
        var valign = new byte[] { 0x24, 0x0C, 0xC8, 0xFF, 0x06, 0x43, 0x65, 0x6E, 0x74, 0x65, 0x72, 0x3D, 0xFF };
        Assert.Equal(valign, theme[0x1903A..0x19047]);
        var nullStyle = new byte[] { 0x07, 0x28, 0x00, 0x03, 0x5A, 0xFE, 0x00, 0x04, 0x08 };
        Assert.Equal(nullStyle, theme[0x19047..0x19050]);
        Assert.Equal(0x04, theme[0x19050]);
        Assert.Equal(0x04, theme[0x19051]);

        Assert.Equal(wrapHHead, theme[0x19052..0x1905D]);
        var orientation = new byte[] { 0x24, 0x0E, 0x36, 0x00, 0x08, 0x56, 0x65, 0x72, 0x74, 0x69, 0x63, 0x61, 0x6C, 0x3D, 0xFF };
        Assert.Equal(orientation, theme[0x1905D..0x1906C]);
        Assert.Equal(halign, theme[0x1906C..0x19079]);
        Assert.Equal(valign, theme[0x19079..0x19086]);
        Assert.Equal(nullStyle, theme[0x19086..0x1908F]);
        Assert.Equal(0x04, theme[0x1908F]);
        Assert.Equal(0x04, theme[0x19090]);

        var dockHead = new byte[] { 0x03, 0xB6, 0xFE, 0x00, 0x2E, 0x52, 0xFF, 0x03, 0x5D, 0xFF, 0x00 };
        Assert.Equal(dockHead, theme[0x19091..0x1909C]);
        var fill = new byte[] { 0x24, 0x0A, 0xD8, 0xFF, 0x04, 0x54, 0x72, 0x75, 0x65, 0xD2, 0xFF };
        Assert.Equal(fill, theme[0x1909C..0x190A7]);
        Assert.Equal(nullStyle, theme[0x190A7..0x190B0]);
        Assert.Equal(0x04, theme[0x190B0]);
        Assert.Equal(0x04, theme[0x190B1]);

        var brushHead = new byte[] { 0x03, 0x9A, 0xFE, 0x00 };
        Assert.Equal(brushHead, theme[0x190B2..0x190B6]);
        var easeHead = new byte[] { 0x03, 0x32, 0x00, 0x00 };
        Assert.Equal(easeHead, theme[0x1912A..0x1912E]);
        var easeMode = new byte[] { 0x24, 0x0D, 0x18, 0x00, 0x07, 0x45, 0x61, 0x73, 0x65, 0x4F, 0x75, 0x74, 0x3D, 0xFF };
        Assert.Equal(easeMode, theme[0x1912E..0x1913C]);
        Assert.Equal(0x04, theme[0x1913C]);
        var badgeHead = new byte[] { 0x03, 0x94, 0xFF, 0x00, 0x22, 0x07, 0x00, 0x33, 0x00, 0x2E, 0x52, 0xFF, 0x03, 0x2E, 0x00, 0x00 };
        Assert.Equal(badgeHead, theme[0x1913D..0x1914D]);
        var presenterHead = new byte[] { 0x03, 0x9A, 0xFF, 0x00 };
        Assert.Equal(presenterHead, theme[0x19150..0x19154]);
        var styleHead = new byte[] { 0x03, 0x94, 0xFD, 0x00 };
        Assert.Equal(styleHead, theme[0x191BC..0x191C0]);

        var key145 = new byte[] { 0x26, 0x09, 0x87, 0x00, 0x8B, 0x9E, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key145, theme[0xB4CF..0xB4D9]);
        var key146 = new byte[] { 0x26, 0x09, 0x88, 0x00, 0xBB, 0x9E, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key146, theme[0xB4D9..0xB4E3]);
        var key147 = new byte[] { 0x26, 0x09, 0x89, 0x00, 0xFA, 0x9E, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key147, theme[0xB4E3..0xB4ED]);
        var key148 = new byte[] { 0x26, 0x09, 0x8A, 0x00, 0x1B, 0x9F, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key148, theme[0xB4ED..0xB4F7]);
        var key149 = new byte[] { 0x26, 0x09, 0x8B, 0x00, 0x93, 0x9F, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key149, theme[0xB4F7..0xB501]);
        var key150 = new byte[] { 0x26, 0x09, 0x8C, 0x00, 0xA6, 0x9F, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key150, theme[0xB501..0xB50B]);
    }

    [Fact]
    public void Staged_boot_advances_past_wrap_to_badge()
    {
        // The staged boot now materializes entries #145-149 in index order
        // (the two WrapPanel ItemsPanelTemplates with the Center pair plus
        // Orientation on the vertical entry, the DockPanel ItemsPanelTemplate
        // with LastChildFill, the ResizeGripperForeground LinearGradientBrush
        // and the SineEaseOut SineEase) plus the keyed 'BadgeDotTemplate'
        // entry keyidx 150 and the keyed
        // 'ColorPickerSliderRepeatButtonBaseStyle' Style entry keyidx 151 plus
        // the 'ColorPickerSliderThumbTemplate' entry keyidx 152 plus the keyed
        // 'ColorPickerRainbowBrush' brush keyidx 153 plus the keyed
        // 'ColorPickerOpacityBrush' brush keyidx 154 with its checker DrawingGroup,
        // and stops at the keyed 'CoverFlowBaseStyle' Style keyidx 155
        // (template Border Viewport3D child well-known id -729 at BAML+0x19497): the precise next
        // blocker. Every earlier stop stays absent, and the new head asserts
        // the exact frontier.
        var entry = LoadStagedHandyControlEntry();
        var error = Assert.Throws<InvalidOperationException>(() => WpfAppBoot.Boot(entry));
        Assert.Contains("startup failed in entry point", error.Message);
        Assert.Contains("line 23", error.Message);
        Assert.Contains("position 26", error.Message);
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
        Assert.DoesNotContain("CircleProgressBarTemplate", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.SimplePanel", inner.Message);
        Assert.DoesNotContain("BAML+0x18FEC", inner.Message);
        Assert.DoesNotContain("VirtualizingStackHorizontalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("VirtualizingStackVerticalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("BAML+0x19029", inner.Message);
        Assert.DoesNotContain("WrapHorizontalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("WrapVerticalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("DockItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("well-known type id -742", inner.Message);
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
    }

    [Fact]
    public void Staged_shape_wrap_horizontal_lands_live_template()
    {
        // The staged 'WrapHorizontalItemsPanelTemplate' entry shape
        // (WrapPanel visual with HorizontalAlignment/VerticalAlignment
        // "Center" plus the FocusVisualStyle x:Null complex, no Orientation
        // simple) lands as a live engine ItemsPanelTemplate keeping the
        // Horizontal default with Center alignments. Per-file ids 0-1 stand
        // in for the staged rows (Orientation row 54, FSV row 40); the
        // alignments travel the well-known -48/-56 members like the staged
        // file.
        var visual = WrapVisual(horizontal: null).ToArray();
        var bytes = WrapDictionary("WrapHorizontalItemsPanelTemplate", visual);
        var dictionary = BamlReader.LoadDictionary(bytes);
        var template = Assert.IsType<ItemsPanelTemplate>(dictionary["WrapHorizontalItemsPanelTemplate"]);
        var panel = Assert.IsType<WrapPanel>(template.VisualRoot);
        Assert.Equal(Orientation.Horizontal, panel.Orientation);
        Assert.False(panel.HasLocalValue(WrapPanel.OrientationProperty));
        Assert.Equal(HorizontalAlignment.Center, panel.HorizontalAlignment);
        Assert.True(panel.HasLocalValue(FrameworkElement.HorizontalAlignmentProperty));
        Assert.Equal(VerticalAlignment.Center, panel.VerticalAlignment);
        Assert.True(panel.HasLocalValue(FrameworkElement.VerticalAlignmentProperty));
        Assert.Null(panel.FocusVisualStyle);
    }

    [Fact]
    public void Staged_shape_wrap_vertical_lands_live_template()
    {
        // The staged 'WrapVerticalItemsPanelTemplate' entry shape (WrapPanel
        // visual with Orientation "Vertical" plus the Center pair) lands
        // with the Vertical local value.
        var visual = WrapVisual(horizontal: false).ToArray();
        var bytes = WrapDictionary("WrapVerticalItemsPanelTemplate", visual);
        var dictionary = BamlReader.LoadDictionary(bytes);
        var template = Assert.IsType<ItemsPanelTemplate>(dictionary["WrapVerticalItemsPanelTemplate"]);
        var panel = Assert.IsType<WrapPanel>(template.VisualRoot);
        Assert.Equal(Orientation.Vertical, panel.Orientation);
        Assert.True(panel.HasLocalValue(WrapPanel.OrientationProperty));
        Assert.Equal(HorizontalAlignment.Center, panel.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, panel.VerticalAlignment);
        Assert.Null(panel.FocusVisualStyle);
    }

    [Fact]
    public void Staged_shape_dock_lands_live_template()
    {
        // The staged 'DockItemsPanelTemplate' entry shape (DockPanel visual
        // with LastChildFill "True" plus the FocusVisualStyle x:Null
        // complex) lands as a live engine ItemsPanelTemplate keeping the
        // fill default as a local value.
        var visual = new B()
            .Raw(0x03).I16(-163).Raw(0x00)
            .Sized(0x24, q => q.I16(-40).Str("True").I16(-46))
            .Raw(NullStyleBytes())
            .Raw(0x04).ToArray();
        var bytes = WrapDictionary("DockItemsPanelTemplate", visual);
        var dictionary = BamlReader.LoadDictionary(bytes);
        var template = Assert.IsType<ItemsPanelTemplate>(dictionary["DockItemsPanelTemplate"]);
        var panel = Assert.IsType<DockPanel>(template.VisualRoot);
        Assert.True(panel.LastChildFill);
        Assert.True(panel.HasLocalValue(DockPanel.LastChildFillProperty));
        Assert.Null(panel.FocusVisualStyle);
    }

    [Fact]
    public void Staged_shape_brush_lands_live_gradient()
    {
        // The staged 'ResizeGripperForeground' entry shape (the generic
        // brush grammar: StartPoint/EndPoint PointConverter simples plus the
        // GradientStops content with three GradientStop children) lands as
        // a live engine LinearGradientBrush with its stops, pinning that
        // entry 148 needs no new code.
        var bytes = BrushDictionary(
            "ResizeGripperForeground",
            BrushEntry(("0,0.25", "1,0.75"),
                [("#FFFFFF", "0.3"), ("#BBC5D7", "0.75"), ("#6D83A9", "1")]).ToArray());
        var dictionary = BamlReader.LoadDictionary(bytes);
        var brush = Assert.IsType<LinearGradientBrush>(dictionary["ResizeGripperForeground"]);
        Assert.Equal(new Point(0, 0.25), brush.StartPoint);
        Assert.Equal(new Point(1, 0.75), brush.EndPoint);
        Assert.Equal(3, brush.GradientStops.Count);
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), brush.GradientStops[0].Color);
        Assert.Equal(0.3, brush.GradientStops[0].Offset);
        Assert.Equal(Color.FromArgb(0xFF, 0xBB, 0xC5, 0xD7), brush.GradientStops[1].Color);
        Assert.Equal(0.75, brush.GradientStops[1].Offset);
        Assert.Equal(Color.FromArgb(0xFF, 0x6D, 0x83, 0xA9), brush.GradientStops[2].Color);
        Assert.Equal(1.0, brush.GradientStops[2].Offset);
    }

    [Fact]
    public void Staged_shape_sine_ease_lands_live_easing()
    {
        // The staged 'SineEaseOut' entry shape (a framework SineEase element
        // with the EasingMode EnumConverter simple) lands through the
        // generic converted-element grammar as a live engine SineEase,
        // pinning that entry 149 needs no new code.
        var entry = new B()
            .Raw(0x03).I16(50).Raw(0x00)
            .Sized(0x24, q => q.I16(24).Str("EaseOut").I16(-195))
            .Raw(0x04).ToArray();
        var bytes = SineDictionary("SineEaseOut", entry);
        var dictionary = BamlReader.LoadDictionary(bytes);
        var ease = Assert.IsType<SineEase>(dictionary["SineEaseOut"]);
        Assert.Equal(EasingMode.EaseOut, ease.EasingMode);
    }

    [Fact]
    public void Staged_shape_wrap_panels_have_no_inflation_markup()
    {
        // Landed wrap/dock panels retain their visual roots without
        // inflation markup, so inflating throws the precise BAML-tree
        // inflation boundary instead of sharing one instance.
        var bytes = WrapDictionary("WrapHorizontalItemsPanelTemplate", WrapVisual(horizontal: null).ToArray());
        var dictionary = BamlReader.LoadDictionary(bytes);
        var template = Assert.IsType<ItemsPanelTemplate>(dictionary["WrapHorizontalItemsPanelTemplate"]);
        var error = Assert.Throws<InvalidOperationException>(() => template.LoadContent());
        Assert.Contains("no inflation markup", error.Message);
        Assert.Contains("BAML-tree inflation", error.Message);
    }

    [Fact]
    public void Wrap_panel_defaults_horizontal_with_store_contract()
    {
        // The engine WrapPanel keeps the WPF defaults: Horizontal wrap
        // direction with no local Orientation value, Stretch alignments,
        // and a null FocusVisualStyle.
        var panel = new WrapPanel();
        Assert.Equal(Orientation.Horizontal, panel.Orientation);
        Assert.False(panel.HasLocalValue(WrapPanel.OrientationProperty));
        Assert.Equal(HorizontalAlignment.Stretch, panel.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Stretch, panel.VerticalAlignment);
        Assert.Null(panel.FocusVisualStyle);
        panel.Orientation = Orientation.Vertical;
        Assert.Equal(Orientation.Vertical, panel.Orientation);
        Assert.True(panel.HasLocalValue(WrapPanel.OrientationProperty));
    }

    [Fact]
    public void Wrap_panel_parses_loose_xaml_and_flows()
    {
        // Loose XAML resolves the same engine name with Orientation through
        // the EnumConverter, and the panel wraps rows like the staged
        // templates (horizontal sum here, vertical stack below).
        var root = (WrapPanel)XamlLoader.Load(
            "<WrapPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Orientation=\"Vertical\" HorizontalAlignment=\"Center\" />").Root;
        Assert.Equal(Orientation.Vertical, root.Orientation);
        Assert.Equal(HorizontalAlignment.Center, root.HorizontalAlignment);
        var horizontal = new WrapPanel();
        var first = new TextBlock { Text = "A" };
        var second = new TextBlock { Text = "B" };
        horizontal.AddChild(first);
        horizontal.AddChild(second);
        horizontal.Measure(new WpfSize(800, 600));
        horizontal.Arrange(new WpfRect(0, 0, 800, 600));
        Assert.Equal(first.DesiredSize.Width + second.DesiredSize.Width, horizontal.DesiredSize.Width);
        var vertical = new WrapPanel { Orientation = Orientation.Vertical };
        var top = new TextBlock { Text = "A" };
        var bottom = new TextBlock { Text = "B" };
        vertical.AddChild(top);
        vertical.AddChild(bottom);
        vertical.Measure(new WpfSize(800, 600));
        vertical.Arrange(new WpfRect(0, 0, 800, 600));
        Assert.Equal(top.DesiredSize.Height + bottom.DesiredSize.Height, vertical.DesiredSize.Height);
        Assert.Equal(top.DesiredSize.Height, bottom.ArrangedRect.Y);
    }

    [Fact]
    public void Dock_panel_defaults_fill_with_store_contract()
    {
        // The engine DockPanel keeps the WPF default: LastChildFill true
        // with no local value, filled through the DP store like the staged
        // template.
        var panel = new DockPanel();
        Assert.True(panel.LastChildFill);
        Assert.False(panel.HasLocalValue(DockPanel.LastChildFillProperty));
        Assert.Null(panel.FocusVisualStyle);
        panel.LastChildFill = false;
        Assert.False(panel.LastChildFill);
        Assert.True(panel.HasLocalValue(DockPanel.LastChildFillProperty));
    }

    [Fact]
    public void Dock_panel_parses_loose_xaml_and_docks()
    {
        // Loose XAML resolves the same engine name with LastChildFill
        // through the BooleanConverter, and docked children keep their
        // edges in arrange order.
        var root = (DockPanel)XamlLoader.Load(
            "<DockPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" LastChildFill=\"False\" />").Root;
        Assert.False(root.LastChildFill);
        var panel = new DockPanel();
        var left = new TextBlock { Text = "A" };
        DockPanel.SetDock(left, Dock.Left);
        var top = new TextBlock { Text = "B" };
        DockPanel.SetDock(top, Dock.Top);
        panel.AddChild(left);
        panel.AddChild(top);
        panel.Measure(new WpfSize(800, 600));
        panel.Arrange(new WpfRect(0, 0, 800, 600));
        Assert.Equal(0, left.ArrangedRect.X);
        Assert.Equal(left.ArrangedRect.Width, top.ArrangedRect.X);
    }

    [Fact]
    public void Wrap_orientation_wrong_converter_fails_precise()
    {
        // An Orientation simple over any other converter (here the
        // BooleanConverter) fails naming the converter used.
        XamlLoader.CustomElementSource = null;
        var visual = WrapVisualWith(horizontal: false, orientationConv: -46).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(WrapDictionary("T", visual)));
        Assert.Contains("with converter -46", error.Message);
        Assert.Contains("does not convert to Orientation", error.Message);
    }

    [Fact]
    public void Wrap_orientation_bad_text_fails_precise()
    {
        // An Orientation simple whose text is not an Orientation fails
        // instead of landing Horizontal.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-742).Raw(0x00)
            .Sized(0x24, q => q.I16(0).Str("Diagonal").I16(-195))
            .Raw(NullStyleBytes())
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(WrapDictionary("T", visual)));
        Assert.Contains("'Diagonal' with converter -195", error.Message);
        Assert.Contains("does not convert to Orientation", error.Message);
    }

    [Fact]
    public void Wrap_halign_bad_text_fails_precise()
    {
        // A HorizontalAlignment simple whose text is not an alignment fails
        // instead of landing Stretch.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-742).Raw(0x00)
            .Sized(0x24, q => q.I16(-48).Str("Middle").I16(-195))
            .Raw(NullStyleBytes())
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(WrapDictionary("T", visual)));
        Assert.Contains("'Middle' with converter -195", error.Message);
        Assert.Contains("does not convert to HorizontalAlignment", error.Message);
    }

    [Fact]
    public void Wrap_unknown_simple_fails_precise()
    {
        // A simple over an AttributeInfo row the file does not have fails
        // instead of landing on the wrong member.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-742).Raw(0x00)
            .Sized(0x24, q => q.I16(9).Str("Vertical").I16(-195))
            .Raw(NullStyleBytes())
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(WrapDictionary("T", visual)));
        Assert.Contains("template property id 9", error.Message);
        Assert.Contains("has no AttributeInfo record", error.Message);
    }

    [Fact]
    public void Uniform_rows_simple_on_wrap_panel_fails_precise()
    {
        // The UniformGrid-owned Rows row has no WrapPanel DP, so it fails
        // precise instead of landing on the wrong panel.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-742).Raw(0x00)
            .Sized(0x24, q => q.I16(2).Str("1").I16(-315))
            .Raw(NullStyleBytes())
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(WrapDictionary("T", visual)));
        Assert.Contains("template attribute owner UniformGrid", error.Message);
        Assert.Contains("StackPanel/VirtualizingStackPanel/WrapPanel/Binding", error.Message);
    }

    [Fact]
    public void Focus_visual_style_duplicate_on_wrap_fails_precise()
    {
        // Two FocusVisualStyle complexes on one items-panel WrapPanel fail
        // instead of overwriting the null.
        XamlLoader.CustomElementSource = null;
        var visual = WrapVisualWith(horizontal: false, duplicateStyle: true).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(WrapDictionary("T", visual)));
        Assert.Contains("duplicate FocusVisualStyle on the items-panel WrapPanel", error.Message);
    }

    [Fact]
    public void Focus_visual_style_non_null_child_on_dock_fails_precise()
    {
        // A FocusVisualStyle complex holding a real element (here a Border)
        // fails: the staged panels carry x:Null only.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-163).Raw(0x00)
            .Sized(0x24, q => q.I16(-40).Str("True").I16(-46))
            .Raw(0x07).I16(1)
            .Raw(0x03).I16(-50).Raw(0x00).Raw(0x04)
            .Raw(0x08)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(WrapDictionary("T", visual)));
        Assert.Contains("holds <Border>", error.Message);
        Assert.Contains("take a NullExtension element", error.Message);
    }

    [Fact]
    public void Dock_last_child_fill_wrong_converter_fails_precise()
    {
        // A LastChildFill simple over any other converter (here the
        // EnumConverter) fails naming the converter used.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-163).Raw(0x00)
            .Sized(0x24, q => q.I16(-40).Str("True").I16(-195))
            .Raw(NullStyleBytes())
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(WrapDictionary("T", visual)));
        Assert.Contains("with converter -195", error.Message);
        Assert.Contains("does not convert to Boolean", error.Message);
    }

    [Fact]
    public void Dock_last_child_fill_bad_text_fails_precise()
    {
        // A LastChildFill simple whose text is not a boolean fails instead
        // of landing filled.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-163).Raw(0x00)
            .Sized(0x24, q => q.I16(-40).Str("Maybe").I16(-46))
            .Raw(NullStyleBytes())
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(WrapDictionary("T", visual)));
        Assert.Contains("'Maybe' with converter -46", error.Message);
        Assert.Contains("does not convert to Boolean", error.Message);
    }

    [Fact]
    public void Items_panel_second_visual_fails_precise()
    {
        // Two visuals in one ItemsPanelTemplate entry fail instead of
        // keeping the first and dropping the second.
        XamlLoader.CustomElementSource = null;
        var first = WrapVisual(horizontal: null).ToArray();
        var second = new B()
            .Raw(0x03).I16(-163).Raw(0x00)
            .Sized(0x24, q => q.I16(-40).Str("True").I16(-46))
            .Raw(NullStyleBytes())
            .Raw(0x04).ToArray();
        var visual = new B().Raw(first).Raw(second).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(WrapEntryDictionary("T", WrapEntry(visual).ToArray())));
        Assert.Contains("second visual on items-panel entry", error.Message);
    }

    [Fact]
    public void Items_panel_unknown_visual_fails_precise()
    {
        // A still-unrouted visual (here a Border) fails precise with the
        // extended takes list, pinning the exact next-visual contract.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-50).Raw(0x00)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(WrapDictionary("T", visual)));
        Assert.Contains("element Border on items-panel entry", error.Message);
        Assert.Contains("take a UniformGrid/StackPanel/VirtualizingStackPanel/WrapPanel/DockPanel visual", error.Message);
    }

    [Fact]
    public void Items_panel_garbage_record_fails_precise()
    {
        // A simple value record directly inside the entry (outside any
        // visual) fails naming the record.
        XamlLoader.CustomElementSource = null;
        var entry = new B()
            .Raw(0x03).I16(-330).Raw(0x00)
            .Raw(0x2E).I16(-174)
            .Sized(0x24, q => q.I16(0).Str("Vertical").I16(-195))
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(WrapEntryDictionary("T", entry)));
        Assert.Contains("holds record 0x24", error.Message);
        Assert.Contains("take the VisualTree content plus one UniformGrid/StackPanel/VirtualizingStackPanel/WrapPanel/DockPanel visual", error.Message);
    }

    [Fact]
    public void Items_panel_wrong_content_member_fails_precise()
    {
        // Any other content member (here Panel.Children) fails: items
        // panels take the VisualTree content.
        XamlLoader.CustomElementSource = null;
        var entry = new B()
            .Raw(0x03).I16(-330).Raw(0x00)
            .Raw(0x2E).I16(-213)
            .Raw(WrapVisual(horizontal: null).ToArray())
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(WrapEntryDictionary("T", entry)));
        Assert.Contains("content member -213 on items-panel entry", error.Message);
        Assert.Contains("take the VisualTree content", error.Message);
    }

    [Fact]
    public void Items_panel_non_shared_key_fails_precise()
    {
        // A non-shared keyed items-panel entry fails: keyed templates take
        // shared entries.
        XamlLoader.CustomElementSource = null;
        var visual = WrapVisual(horizontal: null).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(WrapDictionaryShared("T", visual, shared: false)));
        Assert.Contains("is non-shared", error.Message);
        Assert.Contains("keyed templates take shared entries", error.Message);
    }

    /// <summary>Builds one staged-shape WrapPanel visual (its ElementStart
    /// is included): the optional Orientation EnumConverter simple over row
    /// 0 plus the HorizontalAlignment/VerticalAlignment EnumConverter
    /// simples over the well-known -48/-56 members plus the FocusVisualStyle
    /// x:Null complex, closed once. A null direction carries no Orientation
    /// simple like the staged horizontal entry.</summary>
    private static B WrapVisual(bool? horizontal) => WrapVisualWith(horizontal, orientationConv: -195, duplicateStyle: false);

    /// <summary>Builds one WrapPanel visual with caller-chosen Orientation
    /// converter and optional duplicated FocusVisualStyle (for the
    /// converter/duplicate negatives).</summary>
    private static B WrapVisualWith(bool? horizontal, short orientationConv = -195, bool duplicateStyle = false)
    {
        var visual = new B().Raw(0x03).I16(-742).Raw(0x00);
        if (horizontal is not null)
            visual.Sized(0x24, q => q.I16(0).Str(horizontal.Value ? "Horizontal" : "Vertical").I16(orientationConv));
        visual.Sized(0x24, q => q.I16(-48).Str("Center").I16(-195));
        visual.Sized(0x24, q => q.I16(-56).Str("Center").I16(-195));
        visual.Raw(NullStyleBytes());
        if (duplicateStyle)
            visual.Raw(NullStyleBytes());
        return visual.Raw(0x04);
    }

    /// <summary>Builds the staged FocusVisualStyle x:Null complex child
    /// (complex attr 1 plus the NullExtension element).</summary>
    private static byte[] NullStyleBytes() => new B()
        .Raw(0x07).I16(1)
        .Raw(NullExtensionBytes())
        .Raw(0x08).ToArray();

    /// <summary>Builds the NullExtension child element with its end.</summary>
    private static byte[] NullExtensionBytes() => new B()
        .Raw(0x03).I16(-422).Raw(0x00)
        .Raw(0x04).ToArray();

    /// <summary>Wraps one panel visual in an ItemsPanelTemplate entry body
    /// with the VisualTree content member (for the entry dispatch
    /// tests).</summary>
    private static B WrapEntry(byte[] visual) => new B()
        .Raw(0x03).I16(-330).Raw(0x00)
        .Raw(0x2E).I16(-174)
        .Raw(visual)
        .Raw(0x04);

    /// <summary>Builds a single-entry keyed items-panel dictionary over one
    /// panel visual with a shared key: the visual is wrapped in an
    /// ItemsPanelTemplate entry body with the VisualTree content
    /// member.</summary>
    private static byte[] WrapDictionary(string key, byte[] visual) =>
        WrapShapeDictionary(key, WrapEntry(visual).ToArray(), shared: true);

    /// <summary>Builds a single-entry keyed items-panel dictionary over one
    /// panel visual with caller-chosen sharing.</summary>
    private static byte[] WrapDictionaryShared(string key, byte[] visual, bool shared) =>
        WrapShapeDictionary(key, WrapEntry(visual).ToArray(), shared);

    /// <summary>Builds a single-entry keyed items-panel dictionary over a raw
    /// entry body (for the entry-content negatives).</summary>
    private static byte[] WrapEntryDictionary(string key, byte[] entry) =>
        WrapShapeDictionary(key, entry, shared: true);

    /// <summary>Builds a single-entry keyed items-panel dictionary over the
    /// engine panel visuals (well-known tables only, no TypeInfo rows;
    /// per-file AttributeInfo ids 0-1 stand in for the staged rows -
    /// Orientation row 54, FocusVisualStyle row 40 - plus a UniformGrid-owned
    /// Rows row for the wrong-owner negative).</summary>
    private static byte[] WrapShapeDictionary(string key, byte[] entry, bool shared) => new B().Raw(ItemsHeader()).Raw(0x01)
        .Raw(0x03).I16(-524).Raw(0x00)
        .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
        .Sized(0x1F, p => p.I16(0).I16(-742).Raw(0x00).Str("Orientation"))
        .Sized(0x1F, p => p.I16(1).I16(-226).Raw(0x00).Str("FocusVisualStyle"))
        .Sized(0x1F, p => p.I16(2).I16(-703).Raw(0x00).Str("Rows"))
        .Sized(0x20, p => p.U16(0).Str(key))
        .Deferable()
        .Sized(0x26, p => WrapKeyPair(p, shared))
        .Raw(entry).FinishDictionary().ToArray();

    private static B WrapKeyPair(B p, bool shared)
    {
        p.U16(0).U32(0);
        return shared ? p.U16(0) : p.Raw(0x00, 0x01);
    }

    /// <summary>Builds one staged-shape LinearGradientBrush entry (its
    /// ElementStart is included): StartPoint/EndPoint PointConverter simples
    /// plus the GradientStops content with the given stops, closed
    /// once.</summary>
    private static B BrushEntry((string Start, string End) points, (string Color, string Offset)[] stops)
    {
        var entry = new B()
            .Raw(0x03).I16(-358).Raw(0x00)
            .Sized(0x24, q => q.I16(3).Str(points.Start).I16(-474))
            .Sized(0x24, q => q.I16(4).Str(points.End).I16(-474))
            .Raw(0x2E).I16(-60);
        foreach (var (color, offset) in stops)
            entry.Raw(new B()
                .Raw(0x03).I16(-252).Raw(0x00)
                .Sized(0x24, q => q.I16(5).Str(color).I16(-87))
                .Sized(0x24, q => q.I16(6).Str(offset).I16(-175))
                .Raw(0x04).ToArray());
        return entry.Raw(0x04);
    }

    /// <summary>Builds a single-entry keyed brush dictionary over the
    /// engine brush ids (well-known tables only, no TypeInfo rows;
    /// per-file AttributeInfo ids stand in for the staged rows -
    /// StartPoint row 42, EndPoint row 43, Color row 44, Offset row 45).</summary>
    private static byte[] BrushDictionary(string key, byte[] entry) => new B().Raw(ItemsHeader()).Raw(0x01)
        .Raw(0x03).I16(-524).Raw(0x00)
        .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
        .Sized(0x1F, p => p.I16(3).I16(-358).Raw(0x00).Str("StartPoint"))
        .Sized(0x1F, p => p.I16(4).I16(-358).Raw(0x00).Str("EndPoint"))
        .Sized(0x1F, p => p.I16(5).I16(-252).Raw(0x00).Str("Color"))
        .Sized(0x1F, p => p.I16(6).I16(-252).Raw(0x00).Str("Offset"))
        .Sized(0x20, p => p.U16(0).Str(key))
        .Deferable()
        .Sized(0x26, p => p.U16(0).U32(0).U16(0))
        .Raw(entry).FinishDictionary().ToArray();

    /// <summary>Builds a single-entry keyed easing dictionary over the
    /// staged SineEase shape (TypeInfo id 50 naming the framework SineEase
    /// plus the EasingMode AttributeInfo row 24 owned by the staged row
    /// owner).</summary>
    private static byte[] SineDictionary(string key, byte[] entry) => new B().Raw(ItemsHeader()).Raw(0x01)
        .Raw(0x03).I16(-524).Raw(0x00)
        .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
        .Sized(0x1D, p => p.U16(50).U16(2).Str("System.Windows.Media.Animation.SineEase"))
        .Sized(0x1F, p => p.I16(24).I16(43).Raw(0x00).Str("EasingMode"))
        .Sized(0x20, p => p.U16(0).Str(key))
        .Deferable()
        .Sized(0x26, p => p.U16(0).U32(0).U16(0))
        .Raw(entry).FinishDictionary().ToArray();

    private static byte[] ItemsHeader() => new B()
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
                VerifyStagedHashes();
                return hit;
            }
            if (_stagedEntry is not null) return _stagedEntry;
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

    private sealed class B
    {
        private readonly List<byte> _b = new();
        public B Raw(params byte[] b) { _b.AddRange(b); return this; }
        public B U16(ushort v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
        public B U32(uint v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
        public B I16(short v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
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
            _pos = _b.Count;
            _b.AddRange(new byte[] { 0x25, 0x00, 0x00, 0x00, 0x00 });
            return this;
        }
        public B FinishDictionary()
        {
            if (_pos < 0) throw new InvalidOperationException("no open deferable");
            var endPos = _b.Count;
            _b.Add(0x04);
            var skip = (uint)(endPos - (_pos + 5));
            var patch = BitConverter.GetBytes(skip);
            for (var i = 0; i < 4; i++) _b[_pos + 1 + i] = patch[i];
            _b.Add(0x02);
            return this;
        }
        public byte[] ToArray() => _b.ToArray();
        private int _pos = -1;
    }
}
