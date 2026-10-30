// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-135 HandyControl theme.baml VirtualizingStackPanel run.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the keyed 'VirtualizingStackHorizontalItemsPanelTemplate' entry keyidx
/// 143 at [BAML+0x18FE5, BAML+0x1900C) (VirtualizingStackPanel visual with
/// the Orientation "Horizontal" EnumConverter PWC plus the FocusVisualStyle
/// x:Null complex) and its bare sibling keyidx 144
/// 'VirtualizingStackVerticalItemsPanelTemplate' at [BAML+0x1900C,
/// BAML+0x19022) land as live engine VirtualizingStackPanels, and
/// slice-136 landed the keyed 'WrapHorizontal/WrapVertical/DockItemsPanelTemplate' entries (keyidx 145-147) plus the generic 'ResizeGripperForeground' brush (keyidx 148) and 'SineEaseOut' easing (keyidx 149), and slice-137 landed the keyed 'BadgeDotTemplate' entry (keyidx 150) plus the 'ColorPickerSliderRepeatButtonBaseStyle' Style (keyidx 151), and slice-138 landed the keyed 'ColorPickerSliderThumbTemplate' entry (keyidx 152) with its Thumb TargetType and packed-White Border root, and slice-139 landed the keyed 'ColorPickerRainbowBrush' brush (keyidx 153) at BAML+0x192B2 with WPF-default points and a zero first offset, and slice-140 landed the keyed 'ColorPickerOpacityBrush' brush (keyidx 154) at BAML+0x19383 with its checker DrawingGroup, so the run stops precise at the keyed 'CoverFlowBaseStyle' Style (keyidx 155) at BAML+0x19497 on its template Border's Viewport3D child (well-known type id -729)
///
/// Byte-exact decode against the staged 466350-byte theme.baml,
/// triangulated three ways (the staged bytes: entry heads 03 B6 FE, the
/// VisualTree content member 2E 52 FF, VirtualizingStackPanel visuals
/// 03 24 FD, the staged Orientation PWC 24 10 35 00 "Horizontal"/EnumConverter
/// over per-file AttributeInfo row 53 owned by VirtualizingStackPanel, the
/// FocusVisualStyle x:Null complex 07 28 00 over row 40 with the
/// NullExtension child 03 5A FE, the 04 08 04 04 closes, key records 0x26
/// key ids 0x85/0x86/0x87 with shared 0/0 at 0xB4BB/0xB4C5/0xB4CF plus the
/// next entry head at 0x19022; the live upstream HandyControl
/// ItemsPanelTemplate.xaml block at pinned commit 5b0be3b1 (the same two
/// VirtualizingStackPanel templates with the same values, plus the Wrap/
/// Dock siblings that stay pinned); a genuine net10.0-windows
/// ResourceDictionary fixture compiling the exact upstream VirtH/VirtV
/// block into VirtPanels.baml, byte-identical in every staged record shape
/// - 03 24 FD head, Orientation PWC with the EnumConverter -195,
/// 03 5A FE NullExtension child - differing only in per-file table
/// numbering (fixture rows 0/1 over the same -732 owner, staged rows
/// 53/40) and line markers (the fixture carries 0x35/0x36 records while the
/// staged file omits them; fixture added as VirtPanels.xaml to the
/// E:/tmp/bamlfix project outside the tree, retained like Panels.xaml).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched;
/// staged-oracle status-gate bypass stays in-probe only).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice135BamlVirtTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice135BamlVirtTests()
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
    public void Theme_baml_lands_virt_panels_then_wrap_stop()
    {
        // Staged framing pins for the two VirtualizingStackPanel
        // ItemsPanelTemplate entries keyidx 143-144 spanning
        // [0x18FE5, 0x19022): the 03 B6 FE entry heads, the VisualTree
        // content member, the 03 24 FD visuals, the staged Orientation PWC
        // ("Horizontal" through the EnumConverter over per-file row 53) on
        // the horizontal entry only, the FocusVisualStyle x:Null complexes
        // on both, the 04 08 04 04 closes, the 0x26 key records for 143/144
        // plus the WrapH key, and the 'ColorPickerSliderThumbTemplate' entry head at
        // 0x19279 asserting the exact new frontier. Loading
        // the staged entry first guarantees the HandyControl assembly is
        // present however the runner orders tests.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");

        var head = new byte[] { 0x03, 0xB6, 0xFE, 0x00, 0x2E, 0x52, 0xFF, 0x03, 0x24, 0xFD, 0x00 };
        Assert.Equal(head, theme[0x18FE5..0x18FF0]);
        var orientation = new byte[] { 0x24, 0x10, 0x35, 0x00, 0x0A, 0x48, 0x6F, 0x72, 0x69, 0x7A, 0x6F, 0x6E, 0x74, 0x61, 0x6C, 0x3D, 0xFF };
        Assert.Equal(orientation, theme[0x18FF0..0x19001]);
        var nullStyle = new byte[] { 0x07, 0x28, 0x00, 0x03, 0x5A, 0xFE, 0x00, 0x04, 0x08 };
        Assert.Equal(nullStyle, theme[0x19001..0x1900A]);
        Assert.Equal(0x04, theme[0x1900A]);
        Assert.Equal(0x04, theme[0x1900B]);

        Assert.Equal(head, theme[0x1900C..0x19017]);
        Assert.Equal(nullStyle, theme[0x19017..0x19020]);
        Assert.Equal(0x04, theme[0x19020]);
        Assert.Equal(0x04, theme[0x19021]);

        var key143 = new byte[] { 0x26, 0x09, 0x85, 0x00, 0x4E, 0x9E, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key143, theme[0xB4BB..0xB4C5]);
        var key144 = new byte[] { 0x26, 0x09, 0x86, 0x00, 0x75, 0x9E, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key144, theme[0xB4C5..0xB4CF]);
        var key145 = new byte[] { 0x26, 0x09, 0x87, 0x00, 0x8B, 0x9E, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key145, theme[0xB4CF..0xB4D9]);

        var wrapHead = new byte[] { 0x03, 0xB6, 0xFE, 0x00, 0x2E, 0x52, 0xFF, 0x03, 0x1A, 0xFD, 0x00 };
        Assert.Equal(wrapHead, theme[0x19022..0x1902D]);
    }

    [Fact]
    public void Staged_boot_advances_past_virt_to_wrap()
    {
        // The staged boot now materializes entries #143-144 in index order
        // (the two VirtualizingStackPanel ItemsPanelTemplates, horizontal
        // with Orientation plus bare vertical, each with FocusVisualStyle
        // x:Null) plus the keyed 'ColorPickerRainbowBrush' brush keyidx 153,
        // and stops at the keyed 'CoverFlowBaseStyle' Style keyidx 155
        // (template Border Viewport3D child well-known id -729 at BAML+0x19497): the
        // precise next blocker. Every earlier stop stays
        // absent, and the new head asserts the exact frontier.
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
        Assert.DoesNotContain("well-known type id -742", inner.Message);
        Assert.DoesNotContain("VirtualizingStackPanel", inner.Message);
        Assert.DoesNotContain("templates take a ContentPresenter", inner.Message);
        Assert.DoesNotContain("items panels take a UniformGrid/StackPanel/VirtualizingStackPanel/WrapPanel/DockPanel visual", inner.Message);
    }

    [Fact]
    public void Staged_shape_horizontal_virt_lands_live_template()
    {
        // The staged 'VirtualizingStackHorizontalItemsPanelTemplate' entry
        // shape (VirtualizingStackPanel visual with Orientation "Horizontal"
        // plus the FocusVisualStyle x:Null complex) lands as a live engine
        // ItemsPanelTemplate with the Orientation DP set. Per-file ids 0-1
        // stand in for the staged rows (Orientation row 53, FSV row 40).
        var bytes = VirtDictionary("VirtualizingStackHorizontalItemsPanelTemplate", VirtVisual(horizontal: true).ToArray());
        var dictionary = BamlReader.LoadDictionary(bytes);
        var template = Assert.IsType<ItemsPanelTemplate>(dictionary["VirtualizingStackHorizontalItemsPanelTemplate"]);
        var panel = Assert.IsType<VirtualizingStackPanel>(template.VisualRoot);
        Assert.Equal(Orientation.Horizontal, panel.Orientation);
        Assert.True(panel.HasLocalValue(VirtualizingStackPanel.OrientationProperty));
        Assert.Null(panel.FocusVisualStyle);
    }

    [Fact]
    public void Staged_shape_vertical_virt_lands_live_template()
    {
        // The staged 'VirtualizingStackVerticalItemsPanelTemplate' entry
        // shape (bare VirtualizingStackPanel visual with only the
        // FocusVisualStyle x:Null complex) lands as a live engine
        // ItemsPanelTemplate keeping the Vertical default with no local
        // Orientation value.
        var bytes = VirtDictionary("VirtualizingStackVerticalItemsPanelTemplate", VirtVisual(horizontal: false).ToArray());
        var dictionary = BamlReader.LoadDictionary(bytes);
        var template = Assert.IsType<ItemsPanelTemplate>(dictionary["VirtualizingStackVerticalItemsPanelTemplate"]);
        var panel = Assert.IsType<VirtualizingStackPanel>(template.VisualRoot);
        Assert.Equal(Orientation.Vertical, panel.Orientation);
        Assert.False(panel.HasLocalValue(VirtualizingStackPanel.OrientationProperty));
        Assert.Null(panel.FocusVisualStyle);
    }

    [Fact]
    public void Staged_shape_virt_panels_have_no_inflation_markup()
    {
        // A landed virt panel retains its visual root without inflation
        // markup, so inflating throws the precise BAML-tree inflation
        // boundary instead of sharing one instance.
        var bytes = VirtDictionary("VirtualizingStackHorizontalItemsPanelTemplate", VirtVisual(horizontal: true).ToArray());
        var dictionary = BamlReader.LoadDictionary(bytes);
        var template = Assert.IsType<ItemsPanelTemplate>(dictionary["VirtualizingStackHorizontalItemsPanelTemplate"]);
        var error = Assert.Throws<InvalidOperationException>(() => template.LoadContent());
        Assert.Contains("no inflation markup", error.Message);
        Assert.Contains("BAML-tree inflation", error.Message);
    }

    [Fact]
    public void Fixture_numbered_virt_lands_through_line_markers()
    {
        // The genuine net10.0-windows fixture numbers the same VirtH shape
        // over its own AttributeInfo rows (0 Orientation, 1
        // FocusVisualStyle) with 0x35/0x36 line markers around the simples
        // and the complex: the same shape lands here, proving the staged
        // numbering and marker gaps are table artifacts, not content.
        var visual = new B()
            .Raw(0x03).I16(-732).Raw(0x00)
            .Raw(0x36, 0x05, 0x00, 0x00, 0x00)
            .Sized(0x24, q => q.I16(0).Str("Horizontal").I16(-195))
            .Raw(0x36, 0x3D, 0x00, 0x00, 0x00)
            .Raw(0x07).I16(1)
            .Raw(0x36, 0x21, 0x00, 0x00, 0x00)
            .Raw(NullExtensionBytes())
            .Raw(0x08)
            .Raw(0x04).ToArray();
        var bytes = VirtDictionary("VirtualizingStackHorizontalItemsPanelTemplate", visual);
        var dictionary = BamlReader.LoadDictionary(bytes);
        var template = Assert.IsType<ItemsPanelTemplate>(dictionary["VirtualizingStackHorizontalItemsPanelTemplate"]);
        var panel = Assert.IsType<VirtualizingStackPanel>(template.VisualRoot);
        Assert.Equal(Orientation.Horizontal, panel.Orientation);
        Assert.Null(panel.FocusVisualStyle);
    }

    [Fact]
    public void Virt_panel_defaults_vertical_with_hint_contract()
    {
        // The engine VirtualizingStackPanel keeps the WPF defaults:
        // Vertical orientation with no local value, and the attached
        // IsVirtualizing hint defaulting to true like the slice-102 probe.
        var panel = new VirtualizingStackPanel();
        Assert.Equal(Orientation.Vertical, panel.Orientation);
        Assert.False(panel.HasLocalValue(VirtualizingStackPanel.OrientationProperty));
        Assert.True(VirtualizingStackPanel.GetIsVirtualizing(panel));
        VirtualizingStackPanel.SetIsVirtualizing(panel, false);
        Assert.False(VirtualizingStackPanel.GetIsVirtualizing(panel));
    }

    [Fact]
    public void Virt_panel_parses_loose_xaml_and_stacks()
    {
        // Loose XAML resolves the same engine name with Orientation through
        // the EnumConverter, and the panel stacks children like a StackPanel
        // (vertical sum here, horizontal max-union below).
        var root = (VirtualizingStackPanel)XamlLoader.Load(
            "<VirtualizingStackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Orientation=\"Horizontal\" />").Root;
        Assert.Equal(Orientation.Horizontal, root.Orientation);
        var vertical = new VirtualizingStackPanel();
        var first = new TextBlock { Text = "A" };
        var second = new TextBlock { Text = "B" };
        vertical.AddChild(first);
        vertical.AddChild(second);
        vertical.Measure(new WpfSize(800, 600));
        vertical.Arrange(new WpfRect(0, 0, 800, 600));
        Assert.Equal(first.DesiredSize.Height + second.DesiredSize.Height, vertical.DesiredSize.Height);
        Assert.Equal(first.DesiredSize.Height, second.ArrangedRect.Y);
        var horizontal = new VirtualizingStackPanel { Orientation = Orientation.Horizontal };
        var left = new TextBlock { Text = "A" };
        var right = new TextBlock { Text = "B" };
        horizontal.AddChild(left);
        horizontal.AddChild(right);
        horizontal.Measure(new WpfSize(800, 600));
        horizontal.Arrange(new WpfRect(0, 0, 800, 600));
        Assert.Equal(left.DesiredSize.Width + right.DesiredSize.Width, horizontal.DesiredSize.Width);
        Assert.Equal(left.DesiredSize.Width, right.ArrangedRect.X);
    }

    // Note: two Orientation simples overwrite like the staged StackPanel
    // path (only Rows/Columns/FocusVisualStyle carry duplicate guards), so
    // no duplicate-Orientation negative exists here either.

    [Fact]
    public void Virt_orientation_wrong_converter_fails_precise()
    {
        // An Orientation simple over any other converter (here the
        // StringConverter) fails naming the converter used.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-732).Raw(0x00)
            .Sized(0x24, q => q.I16(0).Str("Horizontal").I16(-615))
            .Raw(NullStyleBytes())
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(VirtDictionary("T", visual)));
        Assert.Contains("with converter -615", error.Message);
        Assert.Contains("does not convert to Orientation", error.Message);
    }

    [Fact]
    public void Virt_orientation_bad_text_fails_precise()
    {
        // An Orientation simple whose text is not an Orientation fails
        // instead of landing Vertical.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-732).Raw(0x00)
            .Sized(0x24, q => q.I16(0).Str("Sideways").I16(-195))
            .Raw(NullStyleBytes())
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(VirtDictionary("T", visual)));
        Assert.Contains("'Sideways' with converter -195", error.Message);
        Assert.Contains("does not convert to Orientation", error.Message);
    }

    [Fact]
    public void Virt_unknown_simple_fails_precise()
    {
        // A simple over an AttributeInfo row the file does not have fails
        // instead of landing on the wrong member.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-732).Raw(0x00)
            .Sized(0x24, q => q.I16(9).Str("Horizontal").I16(-195))
            .Raw(NullStyleBytes())
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(VirtDictionary("T", visual)));
        Assert.Contains("template property id 9", error.Message);
        Assert.Contains("has no AttributeInfo record", error.Message);
    }

    [Fact]
    public void Uniform_rows_simple_on_virt_panel_fails_precise()
    {
        // The UniformGrid-owned Rows row has no VirtualizingStackPanel DP,
        // so it fails precise instead of landing on the wrong panel.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-732).Raw(0x00)
            .Sized(0x24, q => q.I16(2).Str("1").I16(-315))
            .Raw(NullStyleBytes())
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(VirtDictionary("T", visual)));
        Assert.Contains("template attribute owner UniformGrid", error.Message);
        Assert.Contains("StackPanel/VirtualizingStackPanel/WrapPanel/Binding", error.Message);
    }

    [Fact]
    public void Focus_visual_style_duplicate_fails_precise()
    {
        // Two FocusVisualStyle complexes on one items-panel
        // VirtualizingStackPanel fail instead of overwriting the null.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-732).Raw(0x00)
            .Sized(0x24, q => q.I16(0).Str("Horizontal").I16(-195))
            .Raw(NullStyleBytes())
            .Raw(NullStyleBytes())
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(VirtDictionary("T", visual)));
        Assert.Contains("duplicate FocusVisualStyle on the items-panel VirtualizingStackPanel", error.Message);
    }

    [Fact]
    public void Focus_visual_style_unknown_complex_fails_precise()
    {
        // A complex over any other per-file row (here the Orientation row)
        // fails: items panels take the FocusVisualStyle x:Null value.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-732).Raw(0x00)
            .Sized(0x24, q => q.I16(0).Str("Horizontal").I16(-195))
            .Raw(0x07).I16(0)
            .Raw(NullExtensionBytes())
            .Raw(0x08)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(VirtDictionary("T", visual)));
        Assert.Contains("complex property id 0 on the items-panel VirtualizingStackPanel", error.Message);
        Assert.Contains("take the FocusVisualStyle x:Null value", error.Message);
    }

    [Fact]
    public void Focus_visual_style_non_null_child_fails_precise()
    {
        // A FocusVisualStyle complex holding a real element (here a Border)
        // fails: the staged panels carry x:Null only.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-732).Raw(0x00)
            .Raw(0x07).I16(1)
            .Raw(0x03).I16(-50).Raw(0x00).Raw(0x04)
            .Raw(0x08)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(VirtDictionary("T", visual)));
        Assert.Contains("holds <Border>", error.Message);
        Assert.Contains("take a NullExtension element", error.Message);
    }

    [Fact]
    public void Items_panel_second_visual_fails_precise()
    {
        // Two visuals in one ItemsPanelTemplate entry fail instead of
        // keeping the first and dropping the second.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(VirtVisual(horizontal: true).ToArray())
            .Raw(VirtVisual(horizontal: false).ToArray()).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(VirtEntryDictionary("T", VirtEntry(visual).ToArray())));
        Assert.Contains("second visual on items-panel entry", error.Message);
    }

    [Fact]
    public void Items_panel_wrap_visual_lands_live_template()
    {
        // The bare WrapPanel visual slice-136 routes lands as a live engine
        // ItemsPanelTemplate through this fixture's dictionary (no simples,
        // so no per-file wrap rows are needed): the Horizontal default with
        // Stretch alignments.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-742).Raw(0x00)
            .Raw(0x04).ToArray();
        var dictionary = BamlReader.LoadDictionary(VirtDictionary("T", visual));
        var template = Assert.IsType<ItemsPanelTemplate>(dictionary["T"]);
        var panel = Assert.IsType<WrapPanel>(template.VisualRoot);
        Assert.Equal(Orientation.Horizontal, panel.Orientation);
        Assert.False(panel.HasLocalValue(WrapPanel.OrientationProperty));
        Assert.Equal(HorizontalAlignment.Stretch, panel.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Stretch, panel.VerticalAlignment);
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
            .Sized(0x24, q => q.I16(0).Str("Horizontal").I16(-195))
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(VirtEntryDictionary("T", entry)));
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
            .Raw(VirtVisual(horizontal: true).ToArray())
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(VirtEntryDictionary("T", entry)));
        Assert.Contains("content member -213 on items-panel entry", error.Message);
        Assert.Contains("take the VisualTree content", error.Message);
    }

    [Fact]
    public void Items_panel_non_shared_key_fails_precise()
    {
        // A non-shared keyed items-panel entry fails: keyed templates take
        // shared entries.
        XamlLoader.CustomElementSource = null;
        var visual = VirtVisual(horizontal: true).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(VirtDictionaryShared("T", visual, shared: false)));
        Assert.Contains("is non-shared", error.Message);
        Assert.Contains("keyed templates take shared entries", error.Message);
    }

    /// <summary>Builds one staged-shape VirtualizingStackPanel visual (its
    /// ElementStart is included): the optional Orientation EnumConverter
    /// simple plus the FocusVisualStyle x:Null complex, closed once.</summary>
    private static B VirtVisual(bool horizontal)
    {
        var visual = new B().Raw(0x03).I16(-732).Raw(0x00);
        if (horizontal)
            visual.Sized(0x24, q => q.I16(0).Str("Horizontal").I16(-195));
        visual.Raw(NullStyleBytes()).Raw(0x04);
        return visual;
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
    private static B VirtEntry(byte[] visual) => new B()
        .Raw(0x03).I16(-330).Raw(0x00)
        .Raw(0x2E).I16(-174)
        .Raw(visual)
        .Raw(0x04);

    /// <summary>Builds a single-entry keyed items-panel dictionary over one
    /// panel visual with a shared key: the visual is wrapped in an
    /// ItemsPanelTemplate entry body with the VisualTree content
    /// member.</summary>
    private static byte[] VirtDictionary(string key, byte[] visual) =>
        VirtShapeDictionary(key, VirtEntry(visual).ToArray(), shared: true);

    /// <summary>Builds a single-entry keyed items-panel dictionary over one
    /// panel visual with caller-chosen sharing.</summary>
    private static byte[] VirtDictionaryShared(string key, byte[] visual, bool shared) =>
        VirtShapeDictionary(key, VirtEntry(visual).ToArray(), shared);

    /// <summary>Builds a single-entry keyed items-panel dictionary over a raw
    /// entry body (for the entry-content negatives).</summary>
    private static byte[] VirtEntryDictionary(string key, byte[] entry) =>
        VirtShapeDictionary(key, entry, shared: true);

    /// <summary>Builds a single-entry keyed items-panel dictionary over the
    /// engine panel visuals (well-known tables only, no TypeInfo rows;
    /// per-file AttributeInfo ids 0-1 stand in for the staged rows, plus a
    /// UniformGrid-owned Rows row for the wrong-owner negative).</summary>
    private static byte[] VirtShapeDictionary(string key, byte[] entry, bool shared) => new B().Raw(ItemsHeader()).Raw(0x01)
        .Raw(0x03).I16(-524).Raw(0x00)
        .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
        .Sized(0x1F, p => p.I16(0).I16(-732).Raw(0x00).Str("Orientation"))
        .Sized(0x1F, p => p.I16(1).I16(-226).Raw(0x00).Str("FocusVisualStyle"))
        .Sized(0x1F, p => p.I16(2).I16(-703).Raw(0x00).Str("Rows"))
        .Sized(0x20, p => p.U16(0).Str(key))
        .Deferable()
        .Sized(0x26, p => VirtKeyPair(p, shared))
        .Raw(entry).FinishDictionary().ToArray();

    private static B VirtKeyPair(B p, bool shared)
    {
        p.U16(0).U32(0);
        return shared ? p.U16(0) : p.Raw(0x00, 0x01);
    }

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
}
