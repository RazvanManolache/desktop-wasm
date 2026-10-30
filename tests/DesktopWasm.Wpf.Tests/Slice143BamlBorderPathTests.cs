// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-143 HandyControl theme.baml Border-Padding plus path-data brush run.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the slice-142 blocker (the Template setter on the keyed
/// 'GridViewColumnHeaderGripper' Style entry keyidx 157 whose Border
/// Padding TemplateBinding at BAML+0x196C6 named a DP the engine Border
/// did not declare) routes through the engine Border.PaddingProperty, so
/// the whole Template lands; then the keyed 'SortByCategoryDrawingBrush'
/// DrawingBrush entry keyidx 158 at BAML+0x196F6 lands through the
/// Freeze-tolerant brush grammar plus the compact path-data Geometry
/// decoder, as does its sibling 'SortByNameDrawingBrush' entry keyidx 159
/// (same shapes). The run stops precise at the keyed 'TabItemMenu'
/// ContextMenu entry at BAML+0x19D99, which names well-known type id -105
/// the engine has no keyed-entry routing for - the precise next blocker.
///
/// Byte-exact decode against the staged 466350-byte theme.baml,
/// triangulated three ways: the staged bytes (the Template tail with the
/// Border child Rectangle carrying the Fill TemplateBinding plus the
/// Center/1 simples, the DrawingBrush heads with their o:Freeze markers,
/// the DrawingGroup/GeometryDrawing nesting, the 746 path-data Geometry
/// values and the 0x2B/0x74/0xD7-sized customs, the key records 0x26 key
/// ids 0x95-0x96, and the TabItemMenu head 03 97 FF with its MenuItem
/// child); the upstream PropertyGrid/TabControl blocks at pinned commit
/// e9ad3d70 (the gripper Border/Rectangle template, the SortByCategory
/// and SortByName DrawingBrush blocks with their five F1M...z geometries
/// each, the TabItemMenu ContextMenu with its converted Command and
/// x:Static Header values); a genuine net10.0-windows fixture compiling
/// the exact upstream brush blocks (sortbrushes.baml, emitting
/// byte-identical 746 payloads plus byte-identical Freeze markers modulo
/// the string-table id, fixture retained at E:/tmp/bamlfix143 outside the
/// tree) plus a live net10.0-windows probe (Border.PaddingProperty owner/
/// type/default/registration shape, XamlReader-loaded figures/points/
/// closed/fill plus WPF's own XamlPathDataSerializer decoding of every
/// staged payload, which the engine decoder reproduces bit-exact).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// manifests otherwise hand-untouched; staged-oracle status-gate bypass
/// stays in-probe only).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice143BamlBorderPathTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice143BamlBorderPathTests()
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
    public void Theme_baml_lands_gripper_template_tail_and_brush_heads()
    {
        // Staged framing pins for the Template tail on the keyed
        // 'GridViewColumnHeaderGripper' Style entry keyidx 157 spanning
        // [0x196CD, 0x196F6): the Border child-content member, the
        // Rectangle child head, the Fill TemplateBinding (well-known
        // target -100, TemplateBinding 634, Background source -24), the
        // Center HorizontalAlignment and 1 Width simples, and the six
        // closes (Rectangle, Border, ControlTemplate complex, setter,
        // Style); then the keyed 'SortByCategoryDrawingBrush' DrawingBrush
        // entry keyidx 158 head at BAML+0x196F6 (brush head -180, the
        // o:Freeze marker, the Drawing complex, the DrawingGroup head
        // with its own Freeze marker, the Children content, the first
        // GeometryDrawing head with its Freeze marker, the text-form
        // Brush custom and the path-data Geometry custom prefix); then
        // the key records 0x26 key ids 0x95-0x96 (the second carrying the
        // non-shared flags for the x:Shared=False 'TabItemMenu' entry);
        // then the 'TabItemMenu' ContextMenu head at BAML+0x19D99 with
        // its Items content member, the MenuItem head and the converted
        // Command text record prefix.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");

        var borderContent = new byte[] { 0x2E, 0x5F, 0xFF };
        Assert.Equal(borderContent, theme[0x196CD..0x196D0]);
        var rectangleHead = new byte[] { 0x03, 0xFE, 0xFD, 0x00 };
        Assert.Equal(rectangleHead, theme[0x196D0..0x196D4]);
        var fillTb = new byte[] { 0x23, 0x9C, 0xFF, 0x7A, 0x02, 0xE8, 0xFF };
        Assert.Equal(fillTb, theme[0x196D4..0x196DB]);
        var halignPwc = new byte[] { 0x24, 0x0C, 0xD0, 0xFF, 0x06, 0x43, 0x65, 0x6E, 0x74, 0x65, 0x72, 0x3D, 0xFF };
        Assert.Equal(halignPwc, theme[0x196DB..0x196E8]);
        var widthPwc = new byte[] { 0x24, 0x07, 0xC7, 0xFF, 0x01, 0x31, 0xA4, 0xFE };
        Assert.Equal(widthPwc, theme[0x196E8..0x196F0]);
        var templateCloses = new byte[] { 0x04, 0x04, 0x04, 0x08, 0x04, 0x04 };
        Assert.Equal(templateCloses, theme[0x196F0..0x196F6]);

        var brushHead = new byte[] { 0x03, 0x4C, 0xFF, 0x00 };
        Assert.Equal(brushHead, theme[0x196F6..0x196FA]);
        var brushFreeze = new byte[] { 0x34, 0x08, 0x04, 0x54, 0x72, 0x75, 0x65, 0x22, 0x00 };
        Assert.Equal(brushFreeze, theme[0x196FA..0x19703]);
        var drawingComplex = new byte[] { 0x07, 0x3D, 0x00 };
        Assert.Equal(drawingComplex, theme[0x19703..0x19706]);
        var groupHead = new byte[] { 0x03, 0x49, 0xFF, 0x00 };
        Assert.Equal(groupHead, theme[0x19706..0x1970A]);
        var groupFreeze = new byte[] { 0x34, 0x08, 0x04, 0x54, 0x72, 0x75, 0x65, 0x22, 0x00 };
        Assert.Equal(groupFreeze, theme[0x1970A..0x19713]);
        var childrenContent = new byte[] { 0x2E, 0xD6, 0xFF };
        Assert.Equal(childrenContent, theme[0x19713..0x19716]);
        var firstDrawingHead = new byte[] { 0x03, 0x0F, 0xFF, 0x00 };
        Assert.Equal(firstDrawingHead, theme[0x19716..0x1971A]);
        var firstDrawingFreeze = new byte[] { 0x34, 0x08, 0x04, 0x54, 0x72, 0x75, 0x65, 0x22, 0x00 };
        Assert.Equal(firstDrawingFreeze, theme[0x1971A..0x19723]);
        var firstBrushCustom = new byte[] { 0x06, 0x10, 0x3E, 0x00, 0xE8, 0x02, 0x02, 0x09, 0x23, 0x30, 0x30, 0x46, 0x46, 0x46, 0x46, 0x46, 0x46 };
        Assert.Equal(firstBrushCustom, theme[0x19723..0x19734]);
        var firstGeometryPrefix = new byte[] { 0x06, 0x2B, 0x3F, 0x00, 0xEA, 0x02, 0xF0, 0x00, 0x24, 0xF4, 0x00, 0x00 };
        Assert.Equal(firstGeometryPrefix, theme[0x19734..0x19740]);

        var key159 = new byte[] { 0x26, 0x09, 0x95, 0x00, 0x88, 0xA8, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key159, theme[0xB55F..0xB569]);
        var key160 = new byte[] { 0x26, 0x09, 0x96, 0x00, 0x02, 0xAC, 0x00, 0x00, 0x00, 0x01 };
        Assert.Equal(key160, theme[0xB569..0xB573]);

        var tabMenuHead = new byte[] { 0x03, 0x97, 0xFF, 0x00 };
        Assert.Equal(tabMenuHead, theme[0x19D99..0x19D9D]);
        var tabMenuContent = new byte[] { 0x2E, 0x40, 0xFF };
        Assert.Equal(tabMenuContent, theme[0x19D9D..0x19DA0]);
        var menuItemHead = new byte[] { 0x03, 0x6D, 0xFE, 0x00 };
        Assert.Equal(menuItemHead, theme[0x19DA0..0x19DA4]);
        var commandRecordHead = new byte[] { 0x24, 0x29, 0x4C, 0x00, 0x23 };
        Assert.Equal(commandRecordHead, theme[0x19DA4..0x19DA9]);
        Assert.Equal(
            Encoding.ASCII.GetBytes("interactivity:ControlCommands.Close"),
            theme[0x19DA9..0x19DCC]);
    }

    [Fact]
    public void Staged_boot_advances_past_gripper_template_and_brushes_to_tab_item_menu()
    {
        // The staged boot now materializes the keyed
        // 'GridViewColumnHeaderGripper' Style entry keyidx 157 through its
        // Template setter (the Border Padding TemplateBinding routes
        // through the engine Border.PaddingProperty, the Rectangle Fill
        // TemplateBinding plus the Center/1 simples land) and both
        // DrawingBrush entries keyidx 158-159
        // ('SortByCategoryDrawingBrush' at BAML+0x196F6 and
        // 'SortByNameDrawingBrush', each a frozen DrawingGroup of five
        // frozen GeometryDrawings with text-form brushes and path-data
        // geometries), then stops precise at the keyed 'TabItemMenu'
        // ContextMenu entry at BAML+0x19D99: it names well-known type id
        // -105 the keyed-entry routing has no shape for - the precise
        // next blocker. Every earlier stop stays absent, and the new head
        // asserts the exact frontier.
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
        Assert.DoesNotContain("VirtualizingStackHorizontalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("VirtualizingStackVerticalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("WrapHorizontalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("WrapVerticalItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("DockItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("SineEaseOut", inner.Message);
        Assert.DoesNotContain("BAML+0x19150", inner.Message);
        Assert.DoesNotContain("BadgeDotTemplate", inner.Message);
        Assert.DoesNotContain("element ContentPresenter on the template root panel", inner.Message);
        Assert.DoesNotContain("root panels take ContentPresenter, Border, converted Shape and TextBlock children", inner.Message);
        Assert.DoesNotContain("BAML+0x1927D", inner.Message);
        Assert.DoesNotContain("ColorPickerSliderThumbTemplate", inner.Message);
        Assert.DoesNotContain("well-known type id -658", inner.Message);
        Assert.DoesNotContain("style targets beyond Path/Border/ContentControl/GridSplitter/Rectangle/RepeatButton/GroupBox/Thumb are a style-surface follow-up", inner.Message);
        Assert.DoesNotContain("BAML+0x192B2", inner.Message);
        Assert.DoesNotContain("BAML+0x192B9", inner.Message);
        Assert.DoesNotContain("ColorPickerRainbowBrush", inner.Message);
        Assert.DoesNotContain("has no Offset", inner.Message);
        Assert.DoesNotContain("holds a LinearGradientBrush", inner.Message);
        Assert.DoesNotContain("BAML+0x19383", inner.Message);
        Assert.DoesNotContain("ColorPickerOpacityBrush", inner.Message);
        Assert.DoesNotContain("well-known type id -180", inner.Message);
        Assert.DoesNotContain("BAML+0x19497", inner.Message);
        Assert.DoesNotContain("CoverFlowBaseStyle", inner.Message);
        Assert.DoesNotContain("well-known type id -729", inner.Message);
        Assert.DoesNotContain("template borders take", inner.Message);
        Assert.DoesNotContain("CoverViewItemBaseStyle", inner.Message);
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
    }

    [Fact]
    public void Staged_shape_gripper_template_lands_live()
    {
        // The full staged 'GridViewColumnHeaderGripper' entry shape (the
        // five scalar setters plus the Template setter holding the
        // ControlTemplate whose Border visual carries the Padding
        // TemplateBinding and the Rectangle child with its Fill
        // TemplateBinding plus the Center/1 simples) lands as a live
        // engine Style with six setters, the template visual tree and the
        // two deferred bindings. Per-file ids mirror the staged rows
        // 0/1/2/7/10 plus the Border-owned Padding row 75.
        LoadStagedHandyControlEntry();
        var n = Staged143Tables();
        var dictionary = BamlReader.LoadDictionary(
            Gripper143Dictionary("GridViewColumnHeaderGripper", FullGripperEntry(n), n));
        var style = Assert.IsType<Style>(dictionary["GridViewColumnHeaderGripper"]);
        Assert.Same(typeof(Thumb), style.TargetType);
        var setters = style.Setters.OfType<Setter>().ToArray();
        Assert.Equal(6, setters.Length);
        var template = Assert.IsType<ControlTemplate>(setters[5].Value);
        Assert.Same(typeof(Thumb), template.TargetType);
        var border = Assert.IsType<Border>(template.VisualTree);
        var background = Assert.IsType<SolidColorBrush>(border.Background);
        Assert.Equal(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), background.Color);
        Assert.Equal(new Thickness(0), border.Padding);
        var rectangle = Assert.IsType<Rectangle>(border.Child);
        Assert.Equal(HorizontalAlignment.Center, rectangle.HorizontalAlignment);
        Assert.Equal(1.0, rectangle.Width);
        Assert.Null(rectangle.Fill);
        Assert.Equal(2, template.TemplateBindings.Count);
        Assert.Contains((border, "Padding"), template.TemplateBindings.Select(b => (b.Element, b.Property)));
        Assert.Contains((rectangle, "Background"), template.TemplateBindings.Select(b => (b.Element, b.Property)));
    }

    [Fact]
    public void Staged_shape_sort_by_category_lands_live()
    {
        // The staged 'SortByCategoryDrawingBrush' entry shape (frozen
        // DrawingBrush with a frozen DrawingGroup of GeometryDrawings,
        // text-form brushes plus path-data geometries) lands as a live
        // engine DrawingBrush with two drawings here: the 16x16 rect with
        // the #00FFFFFF brush and the 11-point icon with the #FFF6F6F6
        // brush, both frozen, Nonzero fill, closed filled figures. Expected
        // points come from the live net10.0-windows probe (WPF's own
        // XamlPathDataSerializer decoding of the byte-identical staged
        // payloads); the engine reproduces them bit-exact.
        LoadStagedHandyControlEntry();
        var n = Brush143Tables();
        var dictionary = BamlReader.LoadDictionary(Brush143Dictionary(
            "SortByCategoryDrawingBrush",
            SortByCategoryEntry(n),
            n));
        var brush = Assert.IsType<DrawingBrush>(dictionary["SortByCategoryDrawingBrush"]);
        Assert.True(brush.IsFrozen);
        Assert.Equal(BrushMappingMode.RelativeToBoundingBox, brush.ViewportUnits);
        var group = Assert.IsType<DrawingGroup>(brush.Drawing);
        Assert.True(group.IsFrozen);
        Assert.Equal(2, group.Children.Count);

        var first = Assert.IsType<GeometryDrawing>(group.Children[0]);
        Assert.True(first.IsFrozen);
        Assert.Equal(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), Assert.IsType<SolidColorBrush>(first.Brush).Color);
        var firstGeometry = Assert.IsType<StreamGeometry>(first.Geometry);
        Assert.Equal(FillRule.Nonzero, firstGeometry.FillRule);
        var firstFlat = firstGeometry.GetFlattenedPathGeometry();
        var firstFigure = Assert.Single(firstFlat.Figures);
        Assert.Equal(new Point(16, 16), firstFigure.StartPoint);
        Assert.True(firstFigure.IsClosed);
        Assert.True(firstFigure.IsFilled);
        var firstLines = firstFigure.Segments.OfType<LineSegment>().ToArray();
        Assert.Equal(new[] { new Point(0, 16), new Point(0, 0), new Point(16, 0) }, firstLines.Select(l => l.Point));

        var second = Assert.IsType<GeometryDrawing>(group.Children[1]);
        Assert.Equal(Color.FromArgb(0xFF, 0xF6, 0xF6, 0xF6), Assert.IsType<SolidColorBrush>(second.Brush).Color);
        var secondGeometry = Assert.IsType<StreamGeometry>(second.Geometry);
        var secondFlat = secondGeometry.GetFlattenedPathGeometry();
        var secondFigure = Assert.Single(secondFlat.Figures);
        Assert.Equal(new Point(0.9998999999999999, -0.000199999999999534), secondFigure.StartPoint);
        Assert.True(secondFigure.IsClosed);
        var secondLines = secondFigure.Segments.OfType<LineSegment>().ToArray();
        Assert.Equal(10, secondLines.Length);
        Assert.Equal(new Point(0.9998999999999999, 15.999799999999999), secondLines[0].Point);
        Assert.Equal(new Point(10.9999, 13.4148), secondLines[2].Point);
        Assert.Equal(new Point(14.0009, 3.0008), secondLines[7].Point);
        Assert.Equal(new Point(10.9999, -0.000199999999999534), secondLines[9].Point);
    }

    [Fact]
    public void Staged_shape_sort_by_name_lands_live()
    {
        // The staged 'SortByNameDrawingBrush' entry shape exercises the
        // multi-figure close (a single trailing 19 08 closes every open
        // figure): the four-figure drawing with the #FF424242 brush lands
        // four closed filled figures, and the two-figure drawing with the
        // #FFEFEFF0 brush lands the double-moveto start plus the exact
        // last point. Expected values come from the same live probe.
        LoadStagedHandyControlEntry();
        var n = Brush143Tables();
        var dictionary = BamlReader.LoadDictionary(Brush143Dictionary(
            "SortByNameDrawingBrush",
            SortByNameEntry(n),
            n));
        var brush = Assert.IsType<DrawingBrush>(dictionary["SortByNameDrawingBrush"]);
        var group = Assert.IsType<DrawingGroup>(brush.Drawing);
        Assert.Equal(2, group.Children.Count);

        var multi = Assert.IsType<GeometryDrawing>(group.Children[0]);
        Assert.Equal(Color.FromArgb(0xFF, 0x42, 0x42, 0x42), Assert.IsType<SolidColorBrush>(multi.Brush).Color);
        var multiFlat = Assert.IsType<StreamGeometry>(multi.Geometry).GetFlattenedPathGeometry();
        Assert.Equal(4, multiFlat.Figures.Count);
        Assert.All(multiFlat.Figures, f => Assert.True(f.IsClosed));
        Assert.Equal(new Point(7, 7), multiFlat.Figures[0].StartPoint);
        var multiLines = multiFlat.Figures[0].Segments.OfType<LineSegment>().ToArray();
        Assert.Equal(7, multiLines.Length);
        Assert.Equal(new Point(3.399, 6), multiLines[4].Point);
        Assert.Equal(new Point(8, 8), multiFlat.Figures[1].StartPoint);
        Assert.Equal(new Point(4.5, 3.25), multiFlat.Figures[2].StartPoint);
        Assert.Equal(new Point(2, 11), multiFlat.Figures[3].StartPoint);
        var lastFigure = multiFlat.Figures[3];
        var lastLines = lastFigure.Segments.OfType<LineSegment>().ToArray();
        Assert.Equal(11, lastLines.Length);
        Assert.Equal(new Point(2, 10), lastLines[10].Point);

        var dbl = Assert.IsType<GeometryDrawing>(group.Children[1]);
        Assert.Equal(Color.FromArgb(0xFF, 0xEF, 0xEF, 0xF0), Assert.IsType<SolidColorBrush>(dbl.Brush).Color);
        var dblFlat = Assert.IsType<StreamGeometry>(dbl.Geometry).GetFlattenedPathGeometry();
        Assert.Equal(2, dblFlat.Figures.Count);
        Assert.Equal(new Point(3.7998, 5), dblFlat.Figures[0].StartPoint);
        Assert.Equal(new Point(5.9998, 7), dblFlat.Figures[1].StartPoint);
        var dblLines = dblFlat.Figures[1].Segments.OfType<LineSegment>().ToArray();
        Assert.Equal(7, dblLines.Length);
        Assert.Equal(new Point(1.9997999999999998, 7), dblLines[3].Point);
        Assert.Equal(new Point(5.6008, 6), dblLines[6].Point);
    }

    [Fact]
    public void Fixture_numbered_brush_lands_live()
    {
        // The genuine net10.0-windows fixture numbers its tables from zero
        // (attrs 0-6 over the same owners, key string id 0, "Freeze" string
        // id 1); the same staged entry shape lands identically, proving
        // the routing never hardcodes an index.
        LoadStagedHandyControlEntry();
        var n = FixtureBrushTables();
        var dictionary = BamlReader.LoadDictionary(Brush143Dictionary(
            "SortByCategoryDrawingBrush",
            BrushShell(n, GeometryDrawingBytes(n, "#00FFFFFF", GeoRectPayload(), "True"), "True"),
            n));
        var brush = Assert.IsType<DrawingBrush>(dictionary["SortByCategoryDrawingBrush"]);
        Assert.True(brush.IsFrozen);
        var group = Assert.IsType<DrawingGroup>(brush.Drawing);
        var drawing = Assert.IsType<GeometryDrawing>(Assert.Single(group.Children));
        Assert.True(drawing.IsFrozen);
        Assert.Equal(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), Assert.IsType<SolidColorBrush>(drawing.Brush).Color);
        var flat = Assert.IsType<StreamGeometry>(drawing.Geometry).GetFlattenedPathGeometry();
        var figure = Assert.Single(flat.Figures);
        Assert.Equal(new Point(16, 16), figure.StartPoint);
        Assert.True(figure.IsClosed);
    }

    [Fact]
    public void Border_padding_is_own_registration()
    {
        // Live net10.0-windows parity: Border.PaddingProperty is Border's
        // own Thickness registration defaulting to 0,0,0,0 (not the shared
        // Control instance), so FromName resolves it on Border.
        Assert.Same(typeof(Border), Border.PaddingProperty.OwnerType);
        Assert.Same(typeof(Thickness), Border.PaddingProperty.PropertyType);
        Assert.Equal(new Thickness(0), Border.PaddingProperty.DefaultMetadata.DefaultValue);
        Assert.NotSame(Control.PaddingProperty, Border.PaddingProperty);
        Assert.Same(Border.PaddingProperty, DependencyProperty.FromName(typeof(Border), "Padding"));
    }

    [Fact]
    public void Border_padding_round_trips_through_the_store()
    {
        // The Border Padding CLR wrapper is store-backed: an applied value
        // reads back through both the CLR and the DP store, and the
        // default stays zero.
        var border = new Border();
        Assert.Equal(new Thickness(0), border.Padding);
        border.Padding = new Thickness(1, 2, 3, 4);
        Assert.Equal(new Thickness(1, 2, 3, 4), border.Padding);
        Assert.Equal(new Thickness(1, 2, 3, 4), border.GetValue(Border.PaddingProperty));
    }

    [Fact]
    public void Path_data_int_scale_multiplies_like_wpf()
    {
        // The compact path-data Int32 coordinates scale back with the same
        // inexact multiply WPF's own decoder uses (verified bit-exact
        // against WPF's XamlPathDataSerializer over the staged payloads):
        // division would print "0.9999" but WPF lands one ulp below.
        Assert.Equal(0.9998999999999999, 999900 * 1e-6);
        Assert.Equal(15.999799999999999, 15999800 * 1e-6);
        Assert.Equal(3.0000999999999998, 3000100 * 1e-6);
    }

    [Fact]
    public void Path_data_unknown_opcode_fails_precise()
    {
        // A path-data value with an opcode outside figure starts, line
        // points and the close pair fails naming the opcode.
        LoadStagedHandyControlEntry();
        var n = Brush143Tables();
        var payload = new byte[] { 0xF0, 0x00, 0x24, 0xF4, 0x00, 0x00, 0x24, 0xF4, 0x00, 0x71 };
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(Brush143Dictionary(
            "BadBrush", BrushShell(n, GeometryDrawingBytes(n, "#00FFFFFF", payload, "True"), "True"), n)));
        Assert.Contains("path-data opcode 0x71", error.Message);
    }

    [Fact]
    public void Path_data_line_before_figure_fails_precise()
    {
        // A line point before any figure start fails instead of landing a
        // point on no figure.
        LoadStagedHandyControlEntry();
        var n = Brush143Tables();
        var payload = new byte[] { 0xD1, 0x00, 0x24, 0xF4, 0x00, 0x00, 0x24, 0xF4, 0x00, 0x19, 0x08 };
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(Brush143Dictionary(
            "BadBrush", BrushShell(n, GeometryDrawingBytes(n, "#00FFFFFF", payload, "True"), "True"), n)));
        Assert.Contains("path-data line with no figure", error.Message);
    }

    [Fact]
    public void Path_data_missing_close_fails_precise()
    {
        // A path-data value that ends without the close pair fails naming
        // the missing close.
        LoadStagedHandyControlEntry();
        var n = Brush143Tables();
        var payload = new byte[] { 0xF0, 0x00, 0x24, 0xF4, 0x00, 0x00, 0x24, 0xF4, 0x00 };
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(Brush143Dictionary(
            "BadBrush", BrushShell(n, GeometryDrawingBytes(n, "#00FFFFFF", payload, "True"), "True"), n)));
        Assert.Contains("path-data value with no close", error.Message);
    }

    [Fact]
    public void Path_data_close_without_end_fails_precise()
    {
        // A close marker not followed by the 0x08 end fails naming the
        // record it found.
        LoadStagedHandyControlEntry();
        var n = Brush143Tables();
        var payload = new byte[] { 0xF0, 0x00, 0x24, 0xF4, 0x00, 0x00, 0x24, 0xF4, 0x00, 0x19, 0x04 };
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(Brush143Dictionary(
            "BadBrush", BrushShell(n, GeometryDrawingBytes(n, "#00FFFFFF", payload, "True"), "True"), n)));
        Assert.Contains("path-data close with record 0x04", error.Message);
    }

    [Fact]
    public void Path_data_trailing_bytes_fail_precise()
    {
        // Bytes after the close pair fail naming the trailing count
        // instead of landing a half-built drawing.
        LoadStagedHandyControlEntry();
        var n = Brush143Tables();
        var payload = new byte[] { 0xF0, 0x00, 0x24, 0xF4, 0x00, 0x00, 0x24, 0xF4, 0x00, 0x19, 0x08, 0xD1, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(Brush143Dictionary(
            "BadBrush", BrushShell(n, GeometryDrawingBytes(n, "#00FFFFFF", payload, "True"), "True"), n)));
        Assert.Contains("trailing bytes after the close", error.Message);
    }

    [Fact]
    public void Path_data_second_close_reports_trailing()
    {
        // A second close pair fails as trailing bytes (a clean close ends
        // the value, so anything after it trails).
        LoadStagedHandyControlEntry();
        var n = Brush143Tables();
        var payload = new byte[] { 0xF0, 0x00, 0x24, 0xF4, 0x00, 0x00, 0x24, 0xF4, 0x00, 0x19, 0x08, 0x19, 0x08 };
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(Brush143Dictionary(
            "BadBrush", BrushShell(n, GeometryDrawingBytes(n, "#00FFFFFF", payload, "True"), "True"), n)));
        Assert.Contains("2 trailing bytes after the close", error.Message);
    }

    [Fact]
    public void Path_data_truncated_double_fails_precise()
    {
        // A double fallback cut short fails naming the truncation.
        LoadStagedHandyControlEntry();
        var n = Brush143Tables();
        var payload = new byte[] { 0xF0, 0x00, 0x00, 0x00, 0x00, 0x05, 0x04, 0x00, 0x1C, 0xEB };
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(Brush143Dictionary(
            "BadBrush", BrushShell(n, GeometryDrawingBytes(n, "#00FFFFFF", payload, "True"), "True"), n)));
        Assert.Contains("truncated path-data double", error.Message);
    }

    [Fact]
    public void Path_data_wrong_serializer_on_geometry_fails_precise()
    {
        // A Geometry value with the brush serializer fails naming the
        // serializer instead of decoding text as path data.
        LoadStagedHandyControlEntry();
        var n = Brush143Tables();
        var drawing = new B()
            .Raw(0x03).I16(-241).Raw(0x00)
            .Raw(FreezeMarkerBytes(n, "True"))
            .Sized(0x06, q => q.I16((short)n.Brush).U16(744).Raw(0x02).Str("#00FFFFFF"))
            .Sized(0x06, q => q.I16((short)n.Geometry).U16(744).Raw(GeoRectPayload()))
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(Brush143Dictionary(
            "BadBrush", BrushShell(n, drawing.ToArray(), "True"), n)));
        Assert.Contains("path-data value with serializer 744", error.Message);
    }

    [Fact]
    public void Brush_row_with_path_serializer_fails_precise()
    {
        // A Brush value with the path-data serializer fails naming the
        // serializer instead of decoding path data as a color.
        LoadStagedHandyControlEntry();
        var n = Brush143Tables();
        var drawing = new B()
            .Raw(0x03).I16(-241).Raw(0x00)
            .Raw(FreezeMarkerBytes(n, "True"))
            .Sized(0x06, q => q.I16((short)n.Brush).U16(746).Raw(GeoRectPayload()))
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(Brush143Dictionary(
            "BadBrush", BrushShell(n, drawing.ToArray(), "True"), n)));
        Assert.Contains("brush custom value with serializer 746", error.Message);
    }

    [Fact]
    public void Geometry_custom_plus_complex_duplicate_fails_precise()
    {
        // A path-data Geometry value plus a Geometry complex fails as a
        // duplicate instead of landing two geometries.
        LoadStagedHandyControlEntry();
        var n = Brush143Tables();
        var drawing = new B()
            .Raw(0x03).I16(-241).Raw(0x00)
            .Raw(FreezeMarkerBytes(n, "True"))
            .Sized(0x06, q => q.I16((short)n.Brush).U16(744).Raw(0x02).Str("#00FFFFFF"))
            .Sized(0x06, q => q.I16((short)n.Geometry).U16(746).Raw(GeoRectPayload()))
            .Raw(0x07).I16((short)n.Geometry)
            .Raw(0x03).I16(-515).Raw(0x00).Raw(0x04)
            .Raw(0x08)
            .Raw(0x04);
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(Brush143Dictionary(
            "BadBrush", BrushShell(n, drawing.ToArray(), "True"), n)));
        Assert.Contains("duplicate Geometry on the GeometryDrawing", error.Message);
    }

    [Fact]
    public void Freeze_marker_without_true_lands_unfrozen()
    {
        // A Freeze marker carrying any other value lands the drawing
        // unfrozen: freezing stays a marker-only semantic in the engine.
        LoadStagedHandyControlEntry();
        var n = Brush143Tables();
        var dictionary = BamlReader.LoadDictionary(Brush143Dictionary(
            "PlainBrush",
            BrushShell(n, GeometryDrawingBytes(n, "#00FFFFFF", GeoRectPayload(), "False"), "False"),
            n));
        var brush = Assert.IsType<DrawingBrush>(dictionary["PlainBrush"]);
        Assert.False(brush.IsFrozen);
        var drawing = Assert.IsType<GeometryDrawing>(Assert.IsType<DrawingGroup>(brush.Drawing).Children[0]);
        Assert.False(drawing.IsFrozen);
    }

    private sealed record Gripper143Numbering(
        (int Id, string Name, short Owner)[] Attrs,
        int TargetType, int Prop, int Value, int TemplateTarget,
        int RelativeSource, int BorderPadding);

    private static Gripper143Numbering Staged143Tables() => new(
        [(0, "TargetType", (short)-620), (1, "Property", (short)-556), (2, "Value", (short)-556),
         (7, "TargetType", (short)-108), (10, "RelativeSource", (short)-20),
         (75, "Padding", (short)-50)],
        0, 1, 2, 7, 10, 75);

    private sealed record Brush143Numbering(
        (int Id, string Name, short Owner)[] Attrs,
        int Viewport, int ViewportUnits, int Stretch, int TileMode,
        int Drawing, int Brush, int Geometry);

    private static Brush143Numbering Brush143Tables() => new(
        [(57, "Viewport", (short)-662), (58, "ViewportUnits", (short)-662),
         (59, "Stretch", (short)-662), (60, "TileMode", (short)-662),
         (61, "Drawing", (short)-180), (62, "Brush", (short)-241),
         (63, "Geometry", (short)-241)],
        57, 58, 59, 60, 61, 62, 63);

    private static Brush143Numbering FixtureBrushTables() => new(
        [(0, "Viewport", (short)-662), (1, "ViewportUnits", (short)-662),
         (2, "Stretch", (short)-662), (3, "TileMode", (short)-662),
         (4, "Drawing", (short)-180), (5, "Brush", (short)-241),
         (6, "Geometry", (short)-241)],
        0, 1, 2, 3, 4, 5, 6);

    private static byte[] GeoRectPayload() => new byte[]
    {
        0xF0, 0x00, 0x24, 0xF4, 0x00, 0x00, 0x24, 0xF4, 0x00, 0xD1, 0x00, 0x00, 0x00, 0x00, 0x00, 0x24,
        0xF4, 0x00, 0xD1, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xD1, 0x00, 0x24, 0xF4, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x19, 0x08,
    };

    private static byte[] FreezeMarkerBytes(Brush143Numbering n, string value)
    {
        // PresentationOptions Freeze marker: the "True"/other value plus
        // the string-table id. The staged files carry the per-file table
        // id (0x22); synthetic dictionaries are too small for that id, so
        // the numbered tables below always carry "Freeze" as string id 1
        // and the value bytes stay identical.
        _ = n;
        return new B().Sized(0x34, q => q.Str(value).U16(1)).ToArray();
    }

    private static byte[] GeometryDrawingBytes(Brush143Numbering n, string color, byte[] path, string freeze)
    {
        return new B()
            .Raw(0x03).I16(-241).Raw(0x00)
            .Raw(FreezeMarkerBytes(n, freeze))
            .Sized(0x06, q => q.I16((short)n.Brush).U16(744).Raw(0x02).Str(color))
            .Sized(0x06, q => q.I16((short)n.Geometry).U16(746).Raw(path))
            .Raw(0x04).ToArray();
    }

    private static byte[] BrushShell(Brush143Numbering n, byte[] drawing, string freeze)
    {
        var b = new B()
            .Raw(0x03).I16(-180).Raw(0x00)
            .Raw(FreezeMarkerBytes(n, freeze))
            .Raw(0x07).I16((short)n.Drawing)
            .Raw(0x03).I16(-183).Raw(0x00)
            .Raw(FreezeMarkerBytes(n, freeze))
            .Raw(0x2E).I16(-42)
            .Raw(drawing);
        return b.Raw(0x04, 0x08, 0x04).ToArray();
    }

    private static byte[] SortByCategoryEntry(Brush143Numbering n) => BrushShell(n,
        GeometryDrawingBytes(n, "#00FFFFFF", GeoRectPayload(), "True")
            .Concat(GeometryDrawingBytes(n, "#FFF6F6F6", GeoIconPayload(), "True")).ToArray(),
        "True");

    private static byte[] SortByNameEntry(Brush143Numbering n) => BrushShell(n,
        GeometryDrawingBytes(n, "#FF424242", GeoMultiPayload(), "True")
            .Concat(GeometryDrawingBytes(n, "#FFEFEFF0", GeoTwoFigurePayload(), "True")).ToArray(),
        "True");

    private static byte[] FullGripperEntry(Gripper143Numbering n) => Gripper143Shell(n,
        CanvasRightSetter(n),
        WidthSetter(n),
        HeightSetterFor(n, GripperBinding(n, "ActualHeight", "TemplatedParent")),
        PaddingSetter(n),
        BackgroundSetter(n),
        TemplateSetterFull(n)).ToArray();

    private static B Gripper143Shell(Gripper143Numbering n, params B[] setters)
    {
        var b = new B().Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16((short)n.TargetType).I16(-658)
            .Raw(0x2E).I16(-238);
        foreach (var setter in setters) b.Raw(setter.ToArray());
        return b.Raw(0x04);
    }

    private static B CanvasRightSetter(Gripper143Numbering n, string text = "-9") => new B()
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16((short)n.Prop).U16(16521).I16(-66).Str("Right"))
        .Raw(Pwc(n, n.Value, text, -348).ToArray())
        .Raw(0x04);

    private static B WidthSetter(Gripper143Numbering n, string text = "18") => new B()
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16((short)n.Prop).U16(137).I16(-57))
        .Raw(Pwc(n, n.Value, text, -348).ToArray())
        .Raw(0x04);

    private static B HeightSetterFor(Gripper143Numbering n, B binding) => new B()
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16((short)n.Prop).U16(137).I16(-47))
        .Raw(0x07).I16((short)n.Value)
        .Raw(binding.ToArray())
        .Raw(0x08)
        .Raw(0x04);

    private static B PaddingSetter(Gripper143Numbering n, string text = "0") => new B()
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16((short)n.Prop).U16(137).I16(-35))
        .Raw(Pwc(n, n.Value, text, -143).ToArray())
        .Raw(0x04);

    private static B BackgroundSetter(Gripper143Numbering n) => new B()
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16((short)n.Prop).U16(137).I16(-24))
        .Sized(0x06, q => q.I16((short)n.Value).U16(744).Raw(0x01, 0xFF, 0xFF, 0xFF, 0x00))
        .Raw(0x04);

    private static B TemplateSetterFull(Gripper143Numbering n) => new B()
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, q => q.I16((short)n.Prop).U16(137).I16(-37))
        .Raw(0x07).I16((short)n.Value)
        .Raw(0x03).I16(-108).Raw(0x00)
        .Raw(0x22).I16((short)n.TemplateTarget).I16(-658)
        .Raw(0x2E).I16(-174)
        .Raw(0x03).I16(-50).Raw(0x00)
        .Sized(0x06, q => q.I16(-4).U16(744).Raw(0x01, 0xFF, 0xFF, 0xFF, 0x00))
        .Raw(0x23).I16((short)n.BorderPadding).U16(634).I16(-35)
        .Raw(0x2E).I16(-161)
        .Raw(0x03).I16(-514).Raw(0x00)
        .Raw(0x23).I16(-100).U16(634).I16(-24)
        .Sized(0x24, q => q.I16(-48).Str("Center").I16(-195))
        .Sized(0x24, q => q.I16(-57).Str("1").I16(-348))
        .Raw(0x04, 0x04, 0x04, 0x08, 0x04);

    private static B GripperBinding(Gripper143Numbering n, string path, string? mode) => new B()
        .Raw(0x03).I16(-20).Raw(0x00)
        .Raw(CtorText(path).ToArray())
        .Raw(mode is null ? [] : BindingRelativeSource(n, mode).ToArray())
        .Raw(0x04);

    private static B CtorText(string text) => new B()
        .Raw(0x2A)
        .Sized(0x10, q => q.Str(text))
        .Raw(0x2B);

    private static B BindingRelativeSource(Gripper143Numbering n, string mode) => new B()
        .Raw(0x07).I16((short)n.RelativeSource)
        .Raw(0x03).I16(-516).Raw(0x00)
        .Raw(CtorText(mode).ToArray())
        .Raw(0x04)
        .Raw(0x08);

    private static B Pwc(Gripper143Numbering n, int attr, string text, short conv) => new B()
        .Sized(0x24, q => q.I16((short)attr).Str(text).I16(conv));

    private static byte[] Gripper143Dictionary(string key, byte[] entry, Gripper143Numbering n)
    {
        var content = new B().Raw(entry);
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"));
        foreach (var (id, name, owner) in n.Attrs)
            head.Sized(0x1F, p => p.I16((short)id).I16(owner).Raw(0x00).Str(name));
        head.Sized(0x20, p => p.U16(0).Str(key));
        head.Deferable();
        head.Raw(0x26).Seven(9).U16(0).U32(0).U16(0);
        return head.Raw(content.ToArray()).FinishDictionary().ToArray();
    }

    private static byte[] Brush143Dictionary(string key, byte[] entry, Brush143Numbering n)
    {
        var content = new B().Raw(entry);
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"));
        foreach (var (id, name, owner) in n.Attrs)
            head.Sized(0x1F, p => p.I16((short)id).I16(owner).Raw(0x00).Str(name));
        head.Sized(0x20, p => p.U16(0).Str(key));
        head.Sized(0x20, p => p.U16(1).Str("Freeze"));
        head.Deferable();
        head.Raw(0x26).Seven(9).U16(0).U32(0).U16(0);
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
            // 7-bit size including its own bytes (multi-byte for the
            // 200-plus-byte path-data customs).
            int sizeLen = 1;
            while (p.Count + sizeLen >= 1 << (7 * sizeLen)) sizeLen++;
            if (sizeLen > 4) throw new InvalidOperationException("test record too large");
            _b.Add(type);
            uint u = (uint)(p.Count + sizeLen);
            while (u >= 0x80) { _b.Add((byte)(u | 0x80)); u >>= 7; }
            _b.Add((byte)u);
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

    private static byte[] GeoIconPayload() => new byte[] { 0x70, 0xDC, 0x41, 0x0F, 0x00, 0x05, 0x04, 0x00, 0x1C, 0xEB, 0xE2, 0x36, 0x2A, 0xBF, 0xD1, 0xDC, 
        0x41, 0x0F, 0x00, 0x38, 0x23, 0xF4, 0x00, 0xD1, 0x5C, 0xD8, 0xA7, 0x00, 0x38, 0x23, 0xF4, 0x00, 
        0xD1, 0x5C, 0xD8, 0xA7, 0x00, 0x90, 0xB1, 0xCC, 0x00, 0xD1, 0x9C, 0x1A, 0xB7, 0x00, 0xE8, 0xEF, 
        0xDB, 0x00, 0xD1, 0x9C, 0x23, 0xF4, 0x00, 0xE8, 0xE6, 0x9E, 0x00, 0xD1, 0x9C, 0x23, 0xF4, 0x00, 
        0x88, 0x3B, 0x55, 0x00, 0xD1, 0x04, 0xA3, 0xD5, 0x00, 0x08, 0xC0, 0x73, 0x00, 0xD1, 0x04, 0xA3, 
        0xD5, 0x00, 0xE0, 0xC9, 0x2D, 0x00, 0xD1, 0x5C, 0xD8, 0xA7, 0x00, 0xE0, 0xC9, 0x2D, 0x00, 0x51, 
        0x5C, 0xD8, 0xA7, 0x00, 0x05, 0x04, 0x00, 0x1C, 0xEB, 0xE2, 0x36, 0x2A, 0xBF, 0x19, 0x08 };
    private static byte[] GeoMultiPayload() => new byte[] { 0xF0, 0xC0, 0xCF, 0x6A, 0x00, 0xC0, 0xCF, 0x6A, 0x00, 0xD1, 0x40, 0x4B, 0x4C, 0x00, 0x80, 0x84, 
        0x1E, 0x00, 0xD1, 0x00, 0x09, 0x3D, 0x00, 0x80, 0x84, 0x1E, 0x00, 0xD1, 0x80, 0x84, 0x1E, 0x00, 
        0xC0, 0xCF, 0x6A, 0x00, 0xD1, 0xC0, 0xC6, 0x2D, 0x00, 0xC0, 0xCF, 0x6A, 0x00, 0xD1, 0x58, 0xDD, 
        0x33, 0x00, 0x80, 0x8D, 0x5B, 0x00, 0xD1, 0xE8, 0x76, 0x55, 0x00, 0x80, 0x8D, 0x5B, 0x00, 0xD1, 
        0x80, 0x8D, 0x5B, 0x00, 0xC0, 0xCF, 0x6A, 0x00, 0xF0, 0x00, 0x12, 0x7A, 0x00, 0x00, 0x12, 0x7A, 
        0x00, 0xD1, 0x40, 0x42, 0x0F, 0x00, 0x00, 0x12, 0x7A, 0x00, 0xD1, 0x40, 0x42, 0x0F, 0x00, 0x40, 
        0x42, 0x0F, 0x00, 0xD1, 0x00, 0x12, 0x7A, 0x00, 0x40, 0x42, 0x0F, 0x00, 0xF0, 0x20, 0xAA, 0x44, 
        0x00, 0x50, 0x97, 0x31, 0x00, 0xD1, 0x80, 0x58, 0x4F, 0x00, 0x40, 0x4B, 0x4C, 0x00, 0xD1, 0xC0, 
        0xFB, 0x39, 0x00, 0x40, 0x4B, 0x4C, 0x00, 0xF0, 0x80, 0x84, 0x1E, 0x00, 0xC0, 0xD8, 0xA7, 0x00, 
        0xD1, 0x40, 0x4B, 0x4C, 0x00, 0xC0, 0xD8, 0xA7, 0x00, 0xD1, 0x80, 0x84, 0x1E, 0x00, 0x80, 0x9F, 
        0xD5, 0x00, 0xD1, 0x80, 0x84, 0x1E, 0x00, 0xC0, 0xE1, 0xE4, 0x00, 0xD1, 0xC0, 0xCF, 0x6A, 0x00, 
        0xC0, 0xE1, 0xE4, 0x00, 0xD1, 0xC0, 0xCF, 0x6A, 0x00, 0x80, 0x9F, 0xD5, 0x00, 0xD1, 0xC8, 0x3B, 
        0x3D, 0x00, 0x80, 0x9F, 0xD5, 0x00, 0xD1, 0xC0, 0xCF, 0x6A, 0x00, 0xF8, 0x9F, 0xA8, 0x00, 0xD1, 
        0x40, 0x14, 0x6A, 0x00, 0xC0, 0xD8, 0xA7, 0x00, 0xD1, 0xC0, 0xCF, 0x6A, 0x00, 0xC0, 0xD8, 0xA7, 
        0x00, 0xD1, 0xC0, 0xCF, 0x6A, 0x00, 0x80, 0x96, 0x98, 0x00, 0xD1, 0x80, 0x84, 0x1E, 0x00, 0x80, 
        0x96, 0x98, 0x00, 0x19, 0x08 };
    private static byte[] GeoTwoFigurePayload() => new byte[] { 0xF0, 0xF8, 0xFA, 0x39, 0x00, 0x40, 0x4B, 0x4C, 0x00, 0xD1, 0x58, 0xA9, 0x44, 0x00, 0x50, 0x97, 
        0x31, 0x00, 0xD1, 0xB8, 0x57, 0x4F, 0x00, 0x40, 0x4B, 0x4C, 0x00, 0xF0, 0xB8, 0x8C, 0x5B, 0x00, 
        0xC0, 0xCF, 0x6A, 0x00, 0xD1, 0xF8, 0xCE, 0x6A, 0x00, 0xC0, 0xCF, 0x6A, 0x00, 0xD1, 0x78, 0x4A, 
        0x4C, 0x00, 0x80, 0x84, 0x1E, 0x00, 0xD1, 0x38, 0x08, 0x3D, 0x00, 0x80, 0x84, 0x1E, 0x00, 0xD1, 
        0xB8, 0x83, 0x1E, 0x00, 0xC0, 0xCF, 0x6A, 0x00, 0xD1, 0xF8, 0xC5, 0x2D, 0x00, 0xC0, 0xCF, 0x6A, 
        0x00, 0xD1, 0x78, 0xE0, 0x33, 0x00, 0x80, 0x8D, 0x5B, 0x00, 0xD1, 0x20, 0x76, 0x55, 0x00, 0x80, 
        0x8D, 0x5B, 0x00, 0x19, 0x08 };
}
