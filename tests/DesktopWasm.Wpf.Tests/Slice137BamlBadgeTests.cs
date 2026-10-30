// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-137 HandyControl theme.baml BadgeDotTemplate run.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the keyed 'BadgeDotTemplate' ControlTemplate entry keyidx 150 at
/// [BAML+0x1913D, BAML+0x191BC) lands: its converted SimplePanel root
/// (TypeInfo 46) takes the bare ContentPresenter child at BAML+0x19150 plus
/// the Border child carrying Width/Height "10" (LengthConverter), the
/// CornerRadius "5" simple (per-file row 31), the Margin TemplateBinding
/// over the per-file BadgeMargin row 55, the HorizontalAlignment/
/// VerticalAlignment/Background/BorderBrush/BorderThickness TemplateBindings
/// and the Visibility complex holding a TemplateBindingExtension element
/// (type id -634) with the "ShowBadge" constructor path plus the 0x38
/// StaticResource Converter over the per-file Converter row 56 (indexing
/// the key-block Boolean2VisibilityConverter static). The keyed
/// 'ColorPickerSliderRepeatButtonBaseStyle' Style entry keyidx 151 at
/// [BAML+0x191BC, BAML+0x19279) lands with zero new code (RepeatButton
/// target, generic style surface), and slice-138 lands the keyed
/// 'ColorPickerSliderThumbTemplate' ControlTemplate entry keyidx 152 at
/// [BAML+0x19279, BAML+0x192B2) (Thumb TargetType record at BAML+0x1927D
/// with a packed-White Border root), plus slice-139 lands the keyed
/// 'ColorPickerRainbowBrush' brush keyidx 153 at BAML+0x192B2 (WPF-default
/// points, zero first offset), and slice-140 lands the keyed
/// 'ColorPickerOpacityBrush' brush keyidx 154 at BAML+0x19383 with its
/// checker DrawingGroup, so the run stops precise at the keyed
/// 'CoverFlowBaseStyle' Style keyidx 155 at BAML+0x19497 (template Border
/// Viewport3D child well-known id -729).
///
/// Byte-exact decode against the staged 466350-byte theme.baml,
/// triangulated three ways: the staged bytes (the ControlTemplate/Style
/// heads, the VisualTree content member, the -213 Children content, the
/// bare ContentPresenter, the Border head, the seven 0x23 TemplateBinding
/// records with extension 634, the Width/Height/CornerRadius PWCs, the
/// Visibility complex with the 86 FD element plus the ShowBadge
/// constructor text plus the 0x38 static, the closes, the RepeatButton
/// TargetType, the key records 0x26 key ids 0x8C-0x8E with the key-150
/// 0x37 static plus the entry-152 head and Thumb TargetType); the staged
/// tables read straight from the file (TypeInfo 46 SimplePanel / 51 Badge,
/// AttributeInfo rows 7 TargetType, 31 CornerRadius owned by Border, 55
/// BadgeMargin owned by Badge, 56 Converter owned by -634, statics[150] =
/// Boolean2VisibilityConverter); a genuine net10.0-windows fixture
/// compiling the same TemplateBinding-with-Converter shape (emitting 86 FD
/// for the TemplateBindingExtension element, the ShowBadge-equivalent
/// constructor text, the 0x38 Converter static over a -634-owned row, and
/// the Margin TemplateBinding on a Border, byte-identical in every staged
/// record shape modulo per-file table numbering and line markers, which
/// the staged file omits).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched;
/// staged-oracle status-gate bypass stays in-probe only).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice137BamlBadgeTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice137BamlBadgeTests()
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
    public void Theme_baml_places_badge_stop_at_thumb_template()
    {
        // Staged framing pins for the keyed 'BadgeDotTemplate'
        // ControlTemplate entry keyidx 150 spanning [0x1913D, 0x191BC):
        // the ControlTemplate head with the Badge TargetType (TypeInfo 51)
        // and VisualTree content, the converted SimplePanel root (TypeInfo
        // 46) with the Children content, the bare ContentPresenter at
        // 0x19150, the Border with the Margin TemplateBinding over the
        // BadgeMargin row 55 plus the Width/Height/CornerRadius PWCs plus
        // the five remaining TemplateBindings plus the Visibility complex
        // (TemplateBindingExtension 86 FD with the ShowBadge constructor
        // path and the 0x38 Converter static), the closes, the keyed
        // 'ColorPickerSliderRepeatButtonBaseStyle' Style entry keyidx 151
        // head with its RepeatButton TargetType, the key records 0x26 for
        // keyidx 150-152 with key 150's 0x37 Boolean2VisibilityConverter
        // static, and the keyed 'ColorPickerSliderThumbTemplate' entry
        // keyidx 152 head with its Thumb TargetType record at 0x1927D
        // asserting the exact new frontier.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");

        var badgeHead = new byte[] { 0x03, 0x94, 0xFF, 0x00, 0x22, 0x07, 0x00, 0x33, 0x00, 0x2E, 0x52, 0xFF, 0x03, 0x2E, 0x00, 0x00 };
        Assert.Equal(badgeHead, theme[0x1913D..0x1914D]);
        var presenterHead = new byte[] { 0x2E, 0x2B, 0xFF, 0x03, 0x9A, 0xFF, 0x00 };
        Assert.Equal(presenterHead, theme[0x1914D..0x19154]);
        var borderMargin = new byte[] { 0x04, 0x03, 0xCE, 0xFF, 0x00, 0x23, 0xCF, 0xFF, 0x7A, 0x02, 0x37, 0x00 };
        Assert.Equal(borderMargin, theme[0x19154..0x19160]);
        var width = new byte[] { 0x24, 0x08, 0xC7, 0xFF, 0x02, 0x31, 0x30, 0xA4, 0xFE };
        Assert.Equal(width, theme[0x19160..0x19169]);
        var height = new byte[] { 0x24, 0x08, 0xD1, 0xFF, 0x02, 0x31, 0x30, 0xA4, 0xFE };
        Assert.Equal(height, theme[0x19169..0x19172]);
        var corner = new byte[] { 0x24, 0x07, 0x1F, 0x00, 0x01, 0x35, 0x91, 0xFF };
        Assert.Equal(corner, theme[0x19172..0x1917A]);
        var alignBindings = new byte[] { 0x23, 0xD0, 0xFF, 0x7A, 0x02, 0xDF, 0xFF, 0x23, 0xC8, 0xFF, 0x7A, 0x02, 0xDA, 0xFF };
        Assert.Equal(alignBindings, theme[0x1917A..0x19188]);
        var brushBindings = new byte[] { 0x23, 0xFC, 0xFF, 0x7A, 0x02, 0xE8, 0xFF, 0x23, 0xFB, 0xFF, 0x7A, 0x02, 0xE7, 0xFF, 0x23, 0xFA, 0xFF, 0x7A, 0x02, 0xE6, 0xFF };
        Assert.Equal(brushBindings, theme[0x19188..0x1919D]);
        var visibilityHead = new byte[] { 0x07, 0x79, 0xFF, 0x03, 0x86, 0xFD, 0x00, 0x2A, 0x10, 0x0B, 0x09, 0x53, 0x68, 0x6F, 0x77, 0x42 };
        Assert.Equal(visibilityHead, theme[0x1919D..0x191AD]);
        var visibilityTail = new byte[] { 0x61, 0x64, 0x67, 0x65, 0x2B, 0x38, 0x38, 0x00, 0x00, 0x00, 0x04, 0x08, 0x04, 0x04, 0x04 };
        Assert.Equal(visibilityTail, theme[0x191AD..0x191BC]);
        var styleHead = new byte[] { 0x03, 0x94, 0xFD, 0x00 };
        Assert.Equal(styleHead, theme[0x191BC..0x191C0]);
        var repeatTarget = new byte[] { 0x22, 0x00, 0x00, 0xF6, 0xFD };
        Assert.Equal(repeatTarget, theme[0x191C0..0x191C5]);

        var key150 = new byte[] { 0x26, 0x09, 0x8C, 0x00, 0xA6, 0x9F, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key150, theme[0xB501..0xB50B]);
        var key150Static = new byte[] { 0x37, 0x00, 0x08, 0x00 };
        Assert.Equal(key150Static, theme[0xB50B..0xB50F]);
        var key151 = new byte[] { 0x26, 0x09, 0x8D, 0x00, 0x25, 0xA0, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key151, theme[0xB50F..0xB519]);
        var key152 = new byte[] { 0x26, 0x09, 0x8E, 0x00, 0xE2, 0xA0, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key152, theme[0xB519..0xB523]);
        var thumbHead = new byte[] { 0x03, 0x94, 0xFF, 0x00, 0x22 };
        Assert.Equal(thumbHead, theme[0x19279..0x1927E]);
        var thumbTarget = new byte[] { 0x22, 0x07, 0x00, 0x6E, 0xFD };
        Assert.Equal(thumbTarget, theme[0x1927D..0x19282]);
    }

    [Fact]
    public void Staged_boot_advances_past_badge_to_thumb_template()
    {
        // The staged boot now materializes entry #150 (the
        // 'BadgeDotTemplate' ControlTemplate with its converted
        // SimplePanel root, bare ContentPresenter and TemplateBound dot
        // Border) plus entry #151 (the
        // 'ColorPickerSliderRepeatButtonBaseStyle' Style over the
        // RepeatButton target) plus entry #152 (the
        // 'ColorPickerSliderThumbTemplate' ControlTemplate with its Thumb
        // TargetType and packed-White Border root) plus the keyed
        // 'ColorPickerRainbowBrush' brush keyidx 153, and stops at the keyed
        // 'ColorPickerOpacityBrush' brush keyidx 154 with its checker DrawingGroup,
        // and stops at the keyed 'CoverFlowBaseStyle' Style keyidx 155 (template
        // Border Viewport3D child well-known id -729 at BAML+0x19497): the precise next
        // blocker. Every earlier stop stays absent (including the
        // ContentPresenter root-child stop and the BadgeDotTemplate entry),
        // and the new head asserts the exact frontier.
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
        Assert.DoesNotContain("root panels take ContentPresenter, Border, ScrollViewer, converted Shape and TextBlock children", inner.Message);
        Assert.DoesNotContain("ColorPickerSliderRepeatButtonBaseStyle", inner.Message);
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
        Assert.DoesNotContain("style targets beyond Path/Border/ContentControl/GridSplitter/Rectangle/RepeatButton/GroupBox/Thumb are a style-surface follow-up", inner.Message);
    }

    [Fact]
    public void Staged_shape_badge_dot_lands_live_template()
    {
        // The staged 'BadgeDotTemplate' entry shape (converted SimplePanel
        // root with a bare ContentPresenter plus a dot Border carrying the
        // Width/Height/CornerRadius simples, the six TemplateBindings and
        // the TemplateBinding-with-Converter Visibility transfer) lands as
        // a live engine ControlTemplate. Per-file ids stand in for the
        // staged rows (7 TargetType, 31 CornerRadius, 55 BadgeMargin, 56
        // Converter, TypeInfos 46/51). The converted HandyControl assembly
        // must be present for the target/root types.
        LoadStagedHandyControlEntry();
        var n = StagedBadgeTables();
        var dictionary = BamlReader.LoadDictionary(BadgeDictionary("BadgeDotTemplate", BadgeEntry(n).ToArray(), n));
        var template = Assert.IsType<ControlTemplate>(dictionary["BadgeDotTemplate"]);
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        Assert.Same(handyControl.GetType("HandyControl.Controls.Badge"), template.TargetType);
        var root = Assert.IsType<SimplePanel>(template.VisualTree);
        Assert.Equal(2, root.Children.Count);
        Assert.IsType<ContentPresenter>(root.Children[0]);
        var border = Assert.IsType<Border>(root.Children[1]);
        Assert.Equal(10.0, border.Width);
        Assert.True(border.HasLocalValue(FrameworkElement.WidthProperty));
        Assert.Equal(10.0, border.Height);
        Assert.True(border.HasLocalValue(FrameworkElement.HeightProperty));
        Assert.Equal(new CornerRadius(5), border.CornerRadius);
        Assert.True(border.HasLocalValue(Border.CornerRadiusProperty));
        var sources = template.TemplateBindings.Select(b => b.Property).ToArray();
        Assert.Equal(
            new[] { "BadgeMargin", "HorizontalContentAlignment", "VerticalContentAlignment", "Background", "BorderBrush", "BorderThickness" },
            sources);
        Assert.All(template.TemplateBindings, b => Assert.Same(border, b.Element));
        Assert.Null(BindingOperations.GetBinding(border, FrameworkElement.MarginProperty));
        var visibility = BindingOperations.GetBinding(border, UIElement.VisibilityProperty);
        Assert.NotNull(visibility);
        Assert.Equal("ShowBadge", visibility!.Path.Path);
        Assert.Equal(RelativeSourceMode.TemplatedParent, visibility.RelativeSource!.Mode);
        Assert.Equal("Boolean2VisibilityConverter", visibility.DeferredConverterKey);
    }

    [Fact]
    public void Staged_shape_badge_dot_has_no_inflation_markup()
    {
        // The landed badge template retains its visual root without
        // inflation markup, so inflating throws the precise BAML-tree
        // inflation boundary instead of sharing one instance.
        LoadStagedHandyControlEntry();
        var n = StagedBadgeTables();
        var dictionary = BamlReader.LoadDictionary(BadgeDictionary("BadgeDotTemplate", BadgeEntry(n).ToArray(), n));
        var template = Assert.IsType<ControlTemplate>(dictionary["BadgeDotTemplate"]);
        var error = Assert.Throws<InvalidOperationException>(() => template.LoadContent());
        Assert.Contains("no inflation markup", error.Message);
        Assert.Contains("BAML-tree inflation", error.Message);
    }

    [Fact]
    public void Template_binding_without_path_fails_precise()
    {
        // A TemplateBinding element with no constructor path fails instead
        // of binding an empty path.
        XamlLoader.CustomElementSource = null;
        var n = StagedBadgeTables();
        var visibility = new B()
            .Raw(0x03).I16(-634).Raw(0x00)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(BadgeDictionary("T", BadgeEntryWith(n, visibility), n)));
        Assert.Contains("has no path", error.Message);
        Assert.Contains("template TemplateBinding", error.Message);
    }

    [Fact]
    public void Template_binding_two_ctor_args_fails_precise()
    {
        // A TemplateBinding element with two constructor arguments fails
        // instead of binding the first and dropping the second.
        XamlLoader.CustomElementSource = null;
        var n = StagedBadgeTables();
        var visibility = new B()
            .Raw(0x03).I16(-634).Raw(0x00)
            .Raw(0x2A)
            .Sized(0x10, q => q.Str("ShowBadge"))
            .Sized(0x10, q => q.Str("Extra"))
            .Raw(0x2B)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(BadgeDictionary("T", BadgeEntryWith(n, visibility), n)));
        Assert.Contains("takes one constructor argument", error.Message);
    }

    [Fact]
    public void Template_binding_binding_owned_converter_attr_fails_precise()
    {
        // A TemplateBinding Converter static over a Binding-owned row (not
        // the TemplateBinding-owned row) fails naming the attribute: the
        // staged Badge row 56 is owned by -634, not -20.
        XamlLoader.CustomElementSource = null;
        var n = StagedBadgeTablesWithBindingConverter();
        var visibility = new B()
            .Raw(0x03).I16(-634).Raw(0x00)
            .Raw(0x2A)
            .Sized(0x10, q => q.Str("ShowBadge"))
            .Raw(0x2B)
            .Raw(0x38).U16(60).U16(0)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(BadgeDictionary("T", BadgeEntryWith(n, visibility), n)));
        Assert.Contains("holds a StaticResource value for 'Converter'", error.Message);
        Assert.Contains("template bindings take the Converter", error.Message);
    }

    [Fact]
    public void Template_binding_unknown_static_fails_precise()
    {
        // A TemplateBinding Converter naming a static index the entry does
        // not declare fails instead of binding without a converter.
        XamlLoader.CustomElementSource = null;
        var n = StagedBadgeTables();
        var visibility = new B()
            .Raw(0x03).I16(-634).Raw(0x00)
            .Raw(0x2A)
            .Sized(0x10, q => q.Str("ShowBadge"))
            .Raw(0x2B)
            .Raw(0x38).U16((ushort)n.Converter).U16(1)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(BadgeDictionary("T", BadgeEntryWith(n, visibility), n)));
        Assert.Contains("names static resource 1 (only 1 indexed static values", error.Message);
    }

    [Fact]
    public void Template_binding_no_statics_fails_precise()
    {
        // A TemplateBinding Converter with no key-block StaticResource
        // index fails instead of binding without a converter.
        XamlLoader.CustomElementSource = null;
        var n = StagedBadgeTables();
        var visibility = BadgeVisibility(n).Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(BadgeDictionary("T", BadgeEntryWith(n, visibility), n, withStatic: false)));
        Assert.Contains("with no StaticResource index", error.Message);
    }

    [Fact]
    public void Template_binding_duplicate_converter_fails_precise()
    {
        // Two Converter statics on one TemplateBinding fail instead of
        // keeping the first and dropping the second.
        XamlLoader.CustomElementSource = null;
        var n = StagedBadgeTables();
        var visibility = BadgeVisibility(n)
            .Raw(0x38).U16((ushort)n.Converter).U16(0)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(BadgeDictionary("T", BadgeEntryWith(n, visibility), n)));
        Assert.Contains("duplicate Converter on the template TemplateBinding", error.Message);
    }

    [Fact]
    public void Template_binding_garbage_record_fails_precise()
    {
        // A TemplateBinding record inside the TemplateBinding element fails
        // naming the record.
        XamlLoader.CustomElementSource = null;
        var n = StagedBadgeTables();
        var visibility = new B()
            .Raw(0x03).I16(-634).Raw(0x00)
            .Raw(0x2A)
            .Sized(0x10, q => q.Str("ShowBadge"))
            .Raw(0x2B)
            .Raw(0x23).I16(-49).U16(634).I16(55)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(BadgeDictionary("T", BadgeEntryWith(n, visibility), n)));
        Assert.Contains("template TemplateBinding holds record 0x23", error.Message);
        Assert.Contains("template bindings take a path plus Converter", error.Message);
    }

    [Fact]
    public void Root_panel_grid_child_fails_precise()
    {
        // A well-known Grid child on the converted root fails: root panels
        // take ContentPresenter, Border, converted Shape and TextBlock
        // children.
        XamlLoader.CustomElementSource = null;
        var n = StagedBadgeTables();
        var body = BadgeHead(n)
            .Raw(0x2E).I16(-213)
            .Raw(0x03).I16(-254).Raw(0x00)
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(BadgeDictionary("T", body, n)));
        Assert.Contains("element Grid on the template root panel", error.Message);
        Assert.Contains("root panels take ContentPresenter, Border, ScrollViewer, converted Shape and TextBlock children", error.Message);
    }

    [Fact]
    public void Root_panel_garbage_record_fails_precise()
    {
        // A trigger-list record directly on the converted root fails naming
        // the record with the extended root-panel contract.
        XamlLoader.CustomElementSource = null;
        var n = StagedBadgeTables();
        var body = BadgeHead(n)
            .Raw(0x2E).I16(-213)
            .Raw(0x0B).U16(1)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(BadgeDictionary("T", body, n)));
        Assert.Contains("template root panel holds record 0x0B", error.Message);
        Assert.Contains("root panels take the Name string plus the Children content with ContentPresenter, Border, ScrollViewer, Shape and TextBlock children", error.Message);
    }

    [Fact]
    public void Corner_radius_wrong_converter_fails_precise()
    {
        // A CornerRadius simple over any other converter (here the
        // DoubleConverter) fails naming the converter used.
        XamlLoader.CustomElementSource = null;
        var n = StagedBadgeTables();
        var border = BadgeBorder(n, cornerConv: -175, cornerText: "5");
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(BadgeDictionary("T", BadgeEntryWithBorder(n, border.ToArray()), n)));
        Assert.Contains("'5' with converter -175", error.Message);
        Assert.Contains("does not convert to CornerRadius", error.Message);
    }

    [Fact]
    public void Corner_radius_bad_text_fails_precise()
    {
        // A CornerRadius simple whose text is not a corner radius fails
        // instead of landing empty.
        XamlLoader.CustomElementSource = null;
        var n = StagedBadgeTables();
        var border = BadgeBorder(n, cornerConv: -111, cornerText: "round");
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(BadgeDictionary("T", BadgeEntryWithBorder(n, border.ToArray()), n)));
        Assert.Contains("'round' with converter -111", error.Message);
        Assert.Contains("does not convert to CornerRadius", error.Message);
    }

    [Fact]
    public void Border_text_target_fails_precise()
    {
        // A Text TemplateBinding target has no Border DP, so it fails
        // instead of landing on the wrong property.
        XamlLoader.CustomElementSource = null;
        var n = StagedBadgeTables();
        var border = new B()
            .Raw(0x03).I16(-50).Raw(0x00)
            .Raw(0x23).I16(-110).U16(634).I16((short)n.BadgeMargin)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(BadgeDictionary("T", BadgeEntryWithBorder(n, border), n)));
        Assert.Contains("template extension target id -110", error.Message);
        Assert.Contains("Background/BorderBrush/BorderThickness/Height/Width/Margin/HorizontalAlignment/VerticalAlignment on a Border", error.Message);
    }

    [Fact]
    public void Border_corner_radius_dp_contract()
    {
        // The engine Border carries the WPF-identical CornerRadius DP the
        // staged dot Border simple lands through, defaulting empty.
        Assert.Same(Border.CornerRadiusProperty, DependencyProperty.FromName(typeof(Border), "CornerRadius"));
        var border = new Border();
        Assert.Equal(new CornerRadius(0), border.CornerRadius);
        Assert.False(border.HasLocalValue(Border.CornerRadiusProperty));
        border.CornerRadius = new CornerRadius(5);
        Assert.Equal(new CornerRadius(5), border.CornerRadius);
        Assert.True(border.HasLocalValue(Border.CornerRadiusProperty));
    }

    /// <summary>Per-file table numbering standing in for the staged rows
    /// (attrs 7 TargetType, 31 CornerRadius, 55 BadgeMargin, 56 Converter;
    /// TypeInfos 46/51 over the converted HandyControl names).</summary>
    private sealed record BadgeNumbering(
        (int Id, string Name)[] Types,
        (int Id, string Name, short Owner)[] Attrs,
        int TargetType, int CornerRadius, int BadgeMargin, int Converter);

    private static BadgeNumbering StagedBadgeTables() => new(
        [(46, "HandyControl.Controls.SimplePanel"), (51, "HandyControl.Controls.Badge")],
        [(7, "TargetType", -108), (31, "CornerRadius", -50), (55, "BadgeMargin", 51), (56, "Converter", -634)],
        7, 31, 55, 56);

    /// <summary>Staged numbering plus a Binding-owned Converter row (for the
    /// owner-discrimination negative).</summary>
    private static BadgeNumbering StagedBadgeTablesWithBindingConverter() => new(
        [(46, "HandyControl.Controls.SimplePanel"), (51, "HandyControl.Controls.Badge")],
        [(7, "TargetType", -108), (31, "CornerRadius", -50), (55, "BadgeMargin", 51), (56, "Converter", -634), (60, "Converter", -20)],
        7, 31, 55, 56);

    /// <summary>Builds the staged 'BadgeDotTemplate' ControlTemplate head:
    /// the template element, the Badge TargetType, the VisualTree content
    /// and the converted SimplePanel root (for the root dispatch
    /// tests).</summary>
    private static B BadgeHead(BadgeNumbering n) => new B()
        .Raw(0x03).I16(-108).Raw(0x00)
        .Raw(0x22).I16((short)n.TargetType).I16(51)
        .Raw(0x2E).I16(-174)
        .Raw(0x03).I16(46).Raw(0x00);

    /// <summary>Builds the full staged 'BadgeDotTemplate' entry shape: the
    /// head, the Children content, the bare ContentPresenter, the staged
    /// dot Border, closed twice.</summary>
    private static byte[] BadgeEntry(BadgeNumbering n) => BadgeHead(n)
        .Raw(0x2E).I16(-213)
        .Raw(0x03).I16(-102).Raw(0x00)
        .Raw(0x04)
        .Raw(BadgeBorder(n).ToArray())
        .Raw(0x04)
        .Raw(0x04).ToArray();

    /// <summary>Builds a full entry body over a custom Visibility element
    /// (for the TemplateBinding negatives).</summary>
    private static byte[] BadgeEntryWith(BadgeNumbering n, byte[] visibility) => BadgeHead(n)
        .Raw(0x2E).I16(-213)
        .Raw(0x03).I16(-102).Raw(0x00)
        .Raw(0x04)
        .Raw(BadgeBorderWith(n, visibility).ToArray())
        .Raw(0x04)
        .Raw(0x04).ToArray();

    /// <summary>Builds a full entry body over a custom Border visual (for
    /// the Border negatives).</summary>
    private static byte[] BadgeEntryWithBorder(BadgeNumbering n, byte[] border) => BadgeHead(n)
        .Raw(0x2E).I16(-213)
        .Raw(0x03).I16(-102).Raw(0x00)
        .Raw(0x04)
        .Raw(border)
        .Raw(0x04)
        .Raw(0x04).ToArray();

    /// <summary>Builds the staged dot Border with the staged Visibility
    /// transfer (its ElementStart is included).</summary>
    private static B BadgeBorder(BadgeNumbering n, short cornerConv = -111, string cornerText = "5") =>
        BadgeBorderWith(n, BadgeVisibility(n).Raw(0x04).ToArray(), cornerConv, cornerText);

    /// <summary>Builds the staged dot Border over a custom Visibility
    /// element (its ElementStart is included): the Margin TemplateBinding
    /// over the BadgeMargin row, the Width/Height/CornerRadius simples, the
    /// five remaining TemplateBindings, the Visibility complex, closed
    /// once.</summary>
    private static B BadgeBorderWith(BadgeNumbering n, byte[] visibility, short cornerConv = -111, string cornerText = "5") => new B()
        .Raw(0x03).I16(-50).Raw(0x00)
        .Raw(0x23).I16(-49).U16(634).I16((short)n.BadgeMargin)
        .Sized(0x24, q => q.I16(-57).Str("10").I16(-348))
        .Sized(0x24, q => q.I16(-47).Str("10").I16(-348))
        .Sized(0x24, q => q.I16((short)n.CornerRadius).Str(cornerText).I16(cornerConv))
        .Raw(0x23).I16(-48).U16(634).I16(-33)
        .Raw(0x23).I16(-56).U16(634).I16(-38)
        .Raw(0x23).I16(-4).U16(634).I16(-24)
        .Raw(0x23).I16(-5).U16(634).I16(-25)
        .Raw(0x23).I16(-6).U16(634).I16(-26)
        .Raw(0x07).I16(-135)
        .Raw(visibility)
        .Raw(0x08)
        .Raw(0x04);

    /// <summary>Builds the staged TemplateBinding Visibility value (its
    /// ElementStart is included, closed by the caller): the ShowBadge
    /// constructor path plus the 0x38 Converter static.</summary>
    private static B BadgeVisibility(BadgeNumbering n) => new B()
        .Raw(0x03).I16(-634).Raw(0x00)
        .Raw(0x2A)
        .Sized(0x10, q => q.Str("ShowBadge"))
        .Raw(0x2B)
        .Raw(0x38).U16((ushort)n.Converter).U16(0);

    /// <summary>Builds a single-entry keyed ControlTemplate dictionary over
    /// the badge entry plus the key-block statics (the staged Badge key is
    /// shared). TypeInfo/AttributeInfo/StringInfo rows stand in for the
    /// staged tables; string id 1 carries the converter key.</summary>
    private static byte[] BadgeDictionary(string key, byte[] entry, BadgeNumbering n, bool withStatic = true)
    {
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"));
        foreach (var (id, name) in n.Types)
            head.Sized(0x1D, p => p.U16((ushort)id).U16(0).Str(name));
        foreach (var (id, name, owner) in n.Attrs)
            head.Sized(0x1F, p => p.I16((short)id).I16(owner).Raw(0x00).Str(name));
        head.Sized(0x20, p => p.U16(0).Str(key));
        head.Sized(0x20, p => p.U16(1).Str("Boolean2VisibilityConverter"));
        head.Deferable();
        head.Sized(0x26, p => p.U16(0).U32(0).U16(0));
        if (withStatic)
            head.Raw(0x37, 0x00).U16(1);
        return head.Raw(entry).FinishDictionary().ToArray();
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
