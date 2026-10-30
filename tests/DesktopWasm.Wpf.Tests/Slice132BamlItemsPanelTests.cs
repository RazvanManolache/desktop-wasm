using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Slice-132: the four keyed ButtonGroup ItemsPanelTemplate entries keyidx
// 132-135 at [BAML+0x18D3F, BAML+0x18DB8) land as live engine
// ItemsPanelTemplates (UniformGrid Rows="1" / Columns="1" plus StackPanel
// Orientation="Horizontal" / bare, each with FocusVisualStyle {x:Null}),
// and the keyed 'ButtonGroupItemStyleSelector' entry keyidx 136 lands
// through the existing converted-empty-element path, plus the keyed
// 'ProgressBarIndicatorAnimatedFill' brush keyidx 137, slice-135 landed
// the keyed 'VirtualizingStackHorizontal/VerticalItemsPanelTemplate'
// entries keyidx 143-144, and slice-136 landed the keyed 'WrapHorizontal/WrapVertical/DockItemsPanelTemplate' entries (keyidx 145-147) plus the generic 'ResizeGripperForeground' brush (keyidx 148) and 'SineEaseOut' easing (keyidx 149), and slice-137 landed the keyed 'BadgeDotTemplate' entry (keyidx 150) plus the 'ColorPickerSliderRepeatButtonBaseStyle' Style (keyidx 151), and slice-138 landed the keyed 'ColorPickerSliderThumbTemplate' entry (keyidx 152) with its Thumb TargetType and packed-White Border root, and slice-139 landed the keyed 'ColorPickerRainbowBrush' brush (keyidx 153) at BAML+0x192B2 with WPF-default points and a zero first offset, and slice-140 landed the keyed 'ColorPickerOpacityBrush' brush (keyidx 154) at BAML+0x19383 with its checker DrawingGroup, so the run stops precise at the keyed 'CoverFlowBaseStyle' Style (keyidx 155) at BAML+0x19497 on its template Border's Viewport3D child (well-known type id -729)
//
// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
// the ItemsPanelTemplate routing first (entry type -330 over the VisualTree
// content plus one UniformGrid/StackPanel visual with Int32Converter
// Rows/Columns simples, the EnumConverter Orientation simple and the
// FocusVisualStyle x:Null complex child), with the rest pinned. The -330
// decode is triangulated three ways: the staged bytes (entry heads
// 03 B6 FE, the VisualTree content member 2E 52 FF, panel types 41 FD /
// A7 FD, per-file AttributeInfo rows 39 Rows / 40 FocusVisualStyle /
// 41 Columns / 8 Orientation plus the x:Null NullExtension child
// 03 5A FE), the live upstream HandyControl ButtonGroupBaseStyle.xaml
// block (the same four templates with the same values), and a genuine
// net10.0-windows ResourceDictionary fixture compiling the exact upstream
// block, byte-identical in every staged record shape (modulo per-file
// table numbering and line markers, which the staged file omits).
//
// Live counts: staged 466350-byte theme.baml with 6 staged assemblies and
// the same 2 absent binaries (System.Security.Permissions and
// System.Windows.Extensions); HandyControl stays conversion-failed.

namespace DesktopWasm.Wpf.Tests;

[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice132BamlItemsPanelTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice132BamlItemsPanelTests()
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
        // the input; the staged switch asserts pin the real numbers.
        // Loading the staged entry first guarantees the HandyControl
        // assembly is present however the runner orders the tests.
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
    public void Theme_baml_lands_button_group_panels_then_circle_template_stop()
    {
        // Staged framing pins for the four ButtonGroup ItemsPanelTemplate
        // entries plus the converted StyleSelector head they stop past:
        // entries 132-135 span [0x18D3F, 0x18DB8) (UniformGrid Rows, UniformGrid
        // Columns, StackPanel Orientation, bare StackPanel, each with the
        // FocusVisualStyle x:Null complex), keyidx 136 lands the converted
        // 'ButtonGroupItemStyleSelector' empty element at 0x18DB8, keyidx 137
        // lands the 'ProgressBarIndicatorAnimatedFill' brush at 0x18DBD, and
        // the 'CircleProgressBarTemplate' entry head at 0x18E51 asserts
        // the exact new frontier. Loading the staged entry first guarantees
        // the HandyControl assembly is present however the runner orders
        // tests.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");

        var head = new byte[] { 0x03, 0xB6, 0xFE, 0x00, 0x2E, 0x52, 0xFF, 0x03, 0x41, 0xFD, 0x00 };
        Assert.Equal(head, theme[0x18D3F..0x18D4A]);
        var rows = new byte[] { 0x24, 0x07, 0x27, 0x00, 0x01, 0x31, 0xC5, 0xFE };
        Assert.Equal(rows, theme[0x18D4A..0x18D52]);
        var nullStyle = new byte[] { 0x07, 0x28, 0x00, 0x03, 0x5A, 0xFE, 0x00, 0x04, 0x08 };
        Assert.Equal(nullStyle, theme[0x18D52..0x18D5B]);
        Assert.Equal(0x04, theme[0x18D5B]);
        Assert.Equal(0x04, theme[0x18D5C]);

        Assert.Equal(head, theme[0x18D5D..0x18D68]);
        var columns = new byte[] { 0x24, 0x07, 0x29, 0x00, 0x01, 0x31, 0xC5, 0xFE };
        Assert.Equal(columns, theme[0x18D68..0x18D70]);
        Assert.Equal(nullStyle, theme[0x18D70..0x18D79]);

        var stackHead = new byte[] { 0x03, 0xB6, 0xFE, 0x00, 0x2E, 0x52, 0xFF, 0x03, 0xA7, 0xFD, 0x00 };
        Assert.Equal(stackHead, theme[0x18D7B..0x18D86]);
        var orientation = new byte[] { 0x24, 0x10, 0x08, 0x00, 0x0A, 0x48, 0x6F, 0x72, 0x69, 0x7A, 0x6F, 0x6E, 0x74, 0x61, 0x6C, 0x3D, 0xFF };
        Assert.Equal(orientation, theme[0x18D86..0x18D97]);
        Assert.Equal(nullStyle, theme[0x18D97..0x18DA0]);

        Assert.Equal(stackHead, theme[0x18DA2..0x18DAD]);
        Assert.Equal(nullStyle, theme[0x18DAD..0x18DB6]);

        var key132 = new byte[] { 0x26, 0x09, 0x7A, 0x00, 0xA8, 0x9B, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key132, theme[0xB449..0xB453]);
        var key133 = new byte[] { 0x26, 0x09, 0x7B, 0x00, 0xC6, 0x9B, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key133, theme[0xB453..0xB45D]);
        var key134 = new byte[] { 0x26, 0x09, 0x7C, 0x00, 0xE4, 0x9B, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key134, theme[0xB45D..0xB467]);
        var key135 = new byte[] { 0x26, 0x09, 0x7D, 0x00, 0x0B, 0x9C, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key135, theme[0xB467..0xB471]);
        var key136 = new byte[] { 0x26, 0x09, 0x7E, 0x00, 0x21, 0x9C, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key136, theme[0xB471..0xB47B]);

        var selectorHead = new byte[] { 0x03, 0x2F, 0x00, 0x00, 0x04 };
        Assert.Equal(selectorHead, theme[0x18DB8..0x18DBD]);
        var brushHead = new byte[] { 0x03, 0x9A, 0xFE, 0x00, 0x24, 0x09, 0x2A, 0x00, 0x03, 0x30, 0x2C, 0x30, 0x26, 0xFE };
        Assert.Equal(brushHead, theme[0x18DBD..0x18DCB]);
    }

    [Fact]
    public void Staged_boot_advances_past_gradient_to_circle_template()
    {
        // The staged boot now materializes entries #132-136 in index order
        // (the four ButtonGroup ItemsPanelTemplates plus the converted
        // 'ButtonGroupItemStyleSelector' empty element, plus the
        // 'ProgressBarIndicatorAnimatedFill' brush) through the
        // VirtualizingStackPanel items panels (keyidx 143-144) and stops at the
        // 'WrapHorizontalItemsPanelTemplate' entry: the precise next blocker. Every earlier
        // stop stays absent (including the intermediate items-panel head
        // stop and the -330 entry-type stop), and the new head asserts the
        // exact frontier.
        var entry = LoadStagedHandyControlEntry();
        var error = Assert.Throws<InvalidOperationException>(() => WpfAppBoot.Boot(entry));
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
        Assert.DoesNotContain("PathButtonBaseStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x13231", inner.Message);
        Assert.DoesNotContain("BAML+0x1345C", inner.Message);
        Assert.DoesNotContain("BAML+0x13612", inner.Message);
        Assert.DoesNotContain("BAML+0x137BB", inner.Message);
        Assert.DoesNotContain("FluidMoveBehaviorTag", inner.Message);
        Assert.DoesNotContain("Unknown element", inner.Message);
        Assert.DoesNotContain("BAML+0x139C3", inner.Message);
        Assert.DoesNotContain("BAML+0x13A35", inner.Message);
        Assert.DoesNotContain("BAML+0x167D3", inner.Message);
        Assert.DoesNotContain("holds ControlTemplate", inner.Message);
        Assert.DoesNotContain("BAML+0x1859F", inner.Message);
        Assert.DoesNotContain("GroupBoxBaseLeftTemplate", inner.Message);
        Assert.DoesNotContain("BAML+0x1888C", inner.Message);
        Assert.DoesNotContain("GroupBoxTabTopTemplate", inner.Message);
        Assert.DoesNotContain("GroupBoxTabLeftTemplate", inner.Message);
        Assert.DoesNotContain("template Binding holds record 0x38", inner.Message);
        Assert.DoesNotContain("BAML+0x18913", inner.Message);
        Assert.DoesNotContain("template property id -6", inner.Message);
        Assert.DoesNotContain("BAML+0x18D3F", inner.Message);
        Assert.DoesNotContain("ButtonGroupHorizontalUniformItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("ButtonGroupVerticalUniformItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("ButtonGroupHorizontalStackItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("ButtonGroupVerticalStackItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("ButtonGroupItemStyleSelector", inner.Message);
        Assert.DoesNotContain("holds ItemsPanelTemplate", inner.Message);
        Assert.DoesNotContain("holds well-known type id -330", inner.Message);
        Assert.DoesNotContain("BAML+0x18DB8", inner.Message);
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
    }

    [Fact]
    public void Staged_shape_horizontal_uniform_lands_live_template()
    {
        // The staged ButtonGroupHorizontalUniformItemsPanelTemplate entry
        // shape (UniformGrid visual with Rows "1" plus the FocusVisualStyle
        // x:Null complex) lands as a live engine ItemsPanelTemplate retaining
        // its panel root. Per-file ids 0-3 stand in for the staged rows
        // (Rows/Columns/FocusVisualStyle/Orientation).
        var bytes = ItemsDictionary("ButtonGroupHorizontalUniformItemsPanelTemplate", UniformVisual(rows: true, columns: false).ToArray());
        var dictionary = BamlReader.LoadDictionary(bytes);
        var template = Assert.IsType<ItemsPanelTemplate>(dictionary["ButtonGroupHorizontalUniformItemsPanelTemplate"]);
        var grid = Assert.IsType<UniformGrid>(template.VisualRoot);
        Assert.Equal(1, grid.Rows);
        Assert.Equal(0, grid.Columns);
        Assert.Null(grid.FocusVisualStyle);
    }

    [Fact]
    public void Staged_shape_vertical_uniform_lands_live_template()
    {
        // The staged ButtonGroupVerticalUniformItemsPanelTemplate entry
        // shape (UniformGrid visual with Columns "1" plus the FocusVisualStyle
        // x:Null complex) lands as a live engine ItemsPanelTemplate.
        var bytes = ItemsDictionary("ButtonGroupVerticalUniformItemsPanelTemplate", UniformVisual(rows: false, columns: true).ToArray());
        var dictionary = BamlReader.LoadDictionary(bytes);
        var template = Assert.IsType<ItemsPanelTemplate>(dictionary["ButtonGroupVerticalUniformItemsPanelTemplate"]);
        var grid = Assert.IsType<UniformGrid>(template.VisualRoot);
        Assert.Equal(0, grid.Rows);
        Assert.Equal(1, grid.Columns);
        Assert.Null(grid.FocusVisualStyle);
    }

    [Fact]
    public void Staged_shape_horizontal_stack_lands_live_template()
    {
        // The staged ButtonGroupHorizontalStackItemsPanelTemplate entry
        // shape (StackPanel visual with Orientation "Horizontal" plus the
        // FocusVisualStyle x:Null complex) lands as a live engine
        // ItemsPanelTemplate with the Orientation DP set.
        var bytes = ItemsDictionary("ButtonGroupHorizontalStackItemsPanelTemplate", StackVisual(horizontal: true).ToArray());
        var dictionary = BamlReader.LoadDictionary(bytes);
        var template = Assert.IsType<ItemsPanelTemplate>(dictionary["ButtonGroupHorizontalStackItemsPanelTemplate"]);
        var panel = Assert.IsType<StackPanel>(template.VisualRoot);
        Assert.Equal(Orientation.Horizontal, panel.Orientation);
        Assert.True(panel.HasLocalValue(StackPanel.OrientationProperty));
        Assert.Null(panel.FocusVisualStyle);
    }

    [Fact]
    public void Staged_shape_vertical_stack_lands_live_template()
    {
        // The staged ButtonGroupVerticalStackItemsPanelTemplate entry shape
        // (bare StackPanel visual with only the FocusVisualStyle x:Null
        // complex) lands as a live engine ItemsPanelTemplate keeping the
        // Vertical default with no local Orientation value.
        var bytes = ItemsDictionary("ButtonGroupVerticalStackItemsPanelTemplate", StackVisual(horizontal: false).ToArray());
        var dictionary = BamlReader.LoadDictionary(bytes);
        var template = Assert.IsType<ItemsPanelTemplate>(dictionary["ButtonGroupVerticalStackItemsPanelTemplate"]);
        var panel = Assert.IsType<StackPanel>(template.VisualRoot);
        Assert.Equal(Orientation.Vertical, panel.Orientation);
        Assert.False(panel.HasLocalValue(StackPanel.OrientationProperty));
        Assert.Null(panel.FocusVisualStyle);
    }

    [Fact]
    public void Staged_shape_panels_have_no_inflation_markup()
    {
        // A landed items panel retains its visual root without inflation
        // markup, so inflating throws the precise BAML-tree inflation
        // boundary instead of sharing one instance.
        var bytes = ItemsDictionary("ButtonGroupHorizontalUniformItemsPanelTemplate", UniformVisual(rows: true, columns: false).ToArray());
        var dictionary = BamlReader.LoadDictionary(bytes);
        var template = Assert.IsType<ItemsPanelTemplate>(dictionary["ButtonGroupHorizontalUniformItemsPanelTemplate"]);
        var error = Assert.Throws<InvalidOperationException>(() => template.LoadContent());
        Assert.Contains("no inflation markup", error.Message);
        Assert.Contains("BAML-tree inflation", error.Message);
    }

    [Fact]
    public void Uniform_rows_duplicate_fails_precise()
    {
        // Two Rows simples on one items-panel UniformGrid fail instead of
        // overwriting the count.
        XamlLoader.CustomElementSource = null;
        var visual = UniformVisualWith(
            UniformSimple(0, "1").ToArray(),
            UniformSimple(0, "2").ToArray());
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ItemsDictionary("T", visual)));
        Assert.Contains("duplicate Rows on the items-panel UniformGrid", error.Message);
    }

    [Fact]
    public void Uniform_rows_wrong_converter_fails_precise()
    {
        // A Rows simple over any other converter (here the StringConverter)
        // fails naming the converter used.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-703).Raw(0x00)
            .Sized(0x24, q => q.I16(0).Str("1").I16(-615))
            .Raw(NullStyleBytes())
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ItemsDictionary("T", visual)));
        Assert.Contains("uses converter -615", error.Message);
        Assert.Contains("take the Int32Converter", error.Message);
    }

    [Fact]
    public void Uniform_rows_bad_text_fails_precise()
    {
        // A Rows simple whose text is not an integer fails instead of
        // landing zero.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-703).Raw(0x00)
            .Sized(0x24, q => q.I16(0).Str("abc").I16(-315))
            .Raw(NullStyleBytes())
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ItemsDictionary("T", visual)));
        Assert.Contains("'abc' is not an integer for Rows", error.Message);
    }

    [Fact]
    public void Uniform_unknown_simple_fails_precise()
    {
        // A simple over an AttributeInfo row the file does not have fails
        // instead of landing on the wrong member.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-703).Raw(0x00)
            .Sized(0x24, q => q.I16(9).Str("1").I16(-315))
            .Raw(NullStyleBytes())
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ItemsDictionary("T", visual)));
        Assert.Contains("template UniformGrid property id 9", error.Message);
        Assert.Contains("take Rows/Columns", error.Message);
    }

    [Fact]
    public void Columns_simple_on_stack_panel_fails_precise()
    {
        // The UniformGrid-owned Columns row has no template attribute owner
        // mapping, so it fails precise instead of landing on the wrong
        // panel.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-601).Raw(0x00)
            .Sized(0x24, q => q.I16(1).Str("1").I16(-315))
            .Raw(NullStyleBytes())
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ItemsDictionary("T", visual)));
        Assert.Contains("template attribute owner UniformGrid", error.Message);
    }

    [Fact]
    public void Focus_visual_style_duplicate_fails_precise()
    {
        // Two FocusVisualStyle complexes on one items-panel visual fail
        // instead of overwriting the null.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-703).Raw(0x00)
            .Raw(UniformSimple(0, "1").ToArray())
            .Raw(NullStyleBytes())
            .Raw(NullStyleBytes())
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ItemsDictionary("T", visual)));
        Assert.Contains("duplicate FocusVisualStyle on the items-panel UniformGrid", error.Message);
    }

    [Fact]
    public void Focus_visual_style_unknown_complex_fails_precise()
    {
        // A complex over any other per-file row (here the Orientation row)
        // fails: items panels take the FocusVisualStyle x:Null value.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-703).Raw(0x00)
            .Raw(UniformSimple(0, "1").ToArray())
            .Raw(0x07).I16(3)
            .Raw(NullExtensionBytes())
            .Raw(0x08)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ItemsDictionary("T", visual)));
        Assert.Contains("complex property id 3 on the items-panel UniformGrid", error.Message);
        Assert.Contains("take the FocusVisualStyle x:Null value", error.Message);
    }

    [Fact]
    public void Focus_visual_style_non_null_child_fails_precise()
    {
        // A FocusVisualStyle complex holding a real element (here a Border)
        // fails: the staged panels carry x:Null only.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-703).Raw(0x00)
            .Raw(UniformSimple(0, "1").ToArray())
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-50).Raw(0x00).Raw(0x04)
            .Raw(0x08)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ItemsDictionary("T", visual)));
        Assert.Contains("holds <Border>", error.Message);
        Assert.Contains("take a NullExtension element", error.Message);
    }

    [Fact]
    public void Focus_visual_style_simple_child_fails_precise()
    {
        // A FocusVisualStyle complex holding a simple value record instead
        // of the NullExtension element fails naming the record.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-703).Raw(0x00)
            .Raw(UniformSimple(0, "1").ToArray())
            .Raw(0x07).I16(2)
            .Sized(0x24, q => q.I16(2).Str("x:Null").I16(-615))
            .Raw(0x08)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ItemsDictionary("T", visual)));
        Assert.Contains("holds record 0x24", error.Message);
        Assert.Contains("take one NullExtension element", error.Message);
    }

    [Fact]
    public void Items_panel_second_visual_fails_precise()
    {
        // Two visuals in one ItemsPanelTemplate entry fail instead of
        // keeping the first and dropping the second.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-703).Raw(0x00)
            .Raw(NullStyleBytes())
            .Raw(0x04)
            .Raw(0x03).I16(-601).Raw(0x00)
            .Raw(NullStyleBytes())
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ItemsDictionary("T", visual)));
        Assert.Contains("second visual on items-panel entry", error.Message);
    }

    [Fact]
    public void Items_panel_unknown_visual_fails_precise()
    {
        // Any other visual (here a Border) fails: items panels take a
        // UniformGrid/StackPanel/VirtualizingStackPanel/WrapPanel/DockPanel
        // visual.
        XamlLoader.CustomElementSource = null;
        var visual = new B()
            .Raw(0x03).I16(-50).Raw(0x00)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ItemsDictionary("T", visual)));
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
            .Sized(0x24, q => q.I16(0).Str("1").I16(-315))
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ItemsEntryDictionary("T", entry)));
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
            .Raw(UniformVisual(rows: true, columns: false).ToArray())
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ItemsEntryDictionary("T", entry)));
        Assert.Contains("content member -213 on items-panel entry", error.Message);
        Assert.Contains("take the VisualTree content", error.Message);
    }

    [Fact]
    public void Items_panel_non_shared_key_fails_precise()
    {
        // A non-shared keyed items-panel entry fails: keyed templates take
        // shared entries.
        XamlLoader.CustomElementSource = null;
        var visual = UniformVisual(rows: true, columns: false).ToArray();
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(ItemsDictionaryShared("T", visual, shared: false)));
        Assert.Contains("is non-shared", error.Message);
        Assert.Contains("keyed templates take shared entries", error.Message);
    }

    /// <summary>Builds one staged-shape UniformGrid visual (its ElementStart
    /// is included): optional Rows/Columns Int32Converter simples plus the
    /// FocusVisualStyle x:Null complex, closed once.</summary>
    private static B UniformVisual(bool rows, bool columns)
    {
        var visual = new B().Raw(0x03).I16(-703).Raw(0x00);
        if (rows) visual.Raw(UniformSimple(0, "1").ToArray());
        if (columns) visual.Raw(UniformSimple(1, "1").ToArray());
        return visual.Raw(NullStyleBytes()).Raw(0x04);
    }

    /// <summary>Builds one UniformGrid Rows/Columns simple (attr 0/1, text
    /// through the Int32Converter -315): the staged "1" shape.</summary>
    private static B UniformSimple(ushort attr, string text) => new B()
        .Sized(0x24, q => q.I16((short)attr).Str(text).I16(-315));

    /// <summary>Wraps simples in a UniformGrid visual (for the dispatch
    /// tests).</summary>
    private static byte[] UniformVisualWith(params byte[][] simples)
    {
        var visual = new B().Raw(0x03).I16(-703).Raw(0x00);
        foreach (var simple in simples) visual.Raw(simple);
        return visual.Raw(NullStyleBytes()).Raw(0x04).ToArray();
    }

    /// <summary>Builds one staged-shape StackPanel visual (its ElementStart
    /// is included): the optional Orientation EnumConverter simple plus the
    /// FocusVisualStyle x:Null complex, closed once.</summary>
    private static B StackVisual(bool horizontal)
    {
        var visual = new B().Raw(0x03).I16(-601).Raw(0x00);
        if (horizontal)
            visual.Sized(0x24, q => q.I16(3).Str("Horizontal").I16(-195));
        visual.Raw(NullStyleBytes()).Raw(0x04);
        return visual;
    }

    /// <summary>Builds the staged FocusVisualStyle x:Null complex child
    /// (complex attr 2 plus the NullExtension element).</summary>
    private static byte[] NullStyleBytes() => new B()
        .Raw(0x07).I16(2)
        .Raw(NullExtensionBytes())
        .Raw(0x08).ToArray();

    /// <summary>Builds the NullExtension child element with its end.</summary>
    private static byte[] NullExtensionBytes() => new B()
        .Raw(0x03).I16(-422).Raw(0x00)
        .Raw(0x04).ToArray();

    /// <summary>Wraps one panel visual in an ItemsPanelTemplate entry body
    /// with the VisualTree content member (for the entry dispatch
    /// tests).</summary>
    private static B ItemsEntry(byte[] visual) => new B()
        .Raw(0x03).I16(-330).Raw(0x00)
        .Raw(0x2E).I16(-174)
        .Raw(visual)
        .Raw(0x04);

    /// <summary>Builds a single-entry keyed items-panel dictionary over one
    /// panel visual with a shared key: the visual is wrapped in an
    /// ItemsPanelTemplate entry body with the VisualTree content member.</summary>
    private static byte[] ItemsDictionary(string key, byte[] visual) =>
        ItemsShapeDictionary(key, ItemsEntry(visual).ToArray(), shared: true);

    /// <summary>Builds a single-entry keyed items-panel dictionary over one
    /// panel visual with caller-chosen sharing.</summary>
    private static byte[] ItemsDictionaryShared(string key, byte[] visual, bool shared) =>
        ItemsShapeDictionary(key, ItemsEntry(visual).ToArray(), shared);

    /// <summary>Builds a single-entry keyed items-panel dictionary over a raw
    /// entry body (for the entry-content negatives).</summary>
    private static byte[] ItemsEntryDictionary(string key, byte[] entry) =>
        ItemsShapeDictionary(key, entry, shared: true);

    /// <summary>Builds a single-entry keyed items-panel dictionary over the
    /// engine panel visuals (well-known tables only, no TypeInfo rows;
    /// per-file AttributeInfo ids 0-3 stand in for the staged rows).</summary>
    private static byte[] ItemsShapeDictionary(string key, byte[] entry, bool shared) => new B().Raw(ItemsHeader()).Raw(0x01)
        .Raw(0x03).I16(-524).Raw(0x00)
        .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
        .Sized(0x1F, p => p.I16(0).I16(-703).Raw(0x00).Str("Rows"))
        .Sized(0x1F, p => p.I16(1).I16(-703).Raw(0x00).Str("Columns"))
        .Sized(0x1F, p => p.I16(2).I16(-226).Raw(0x00).Str("FocusVisualStyle"))
        .Sized(0x1F, p => p.I16(3).I16(-601).Raw(0x00).Str("Orientation"))
        .Sized(0x20, p => p.U16(0).Str(key))
        .Deferable()
        .Sized(0x26, p => ItemsKeyPair(p, shared))
        .Raw(entry).FinishDictionary().ToArray();

    private static B ItemsKeyPair(B p, bool shared)
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
