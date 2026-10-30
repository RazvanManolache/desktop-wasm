using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Slice-131: the keyed 'GroupBoxTabTopTemplate' template entry keyidx 130
// at [BAML+0x187F5, BAML+0x18A8D) lands as a live engine ControlTemplate
// (GroupBox target, Grid visual with two RowDefinitions plus two Borders
// carrying TemplateBinding and TemplatedParent-bound values whose
// StaticResource Converters land as deferred converter keys plus
// ConverterParameter simples), and the keyed 'GroupBoxTabLeftTemplate'
// entry keyidx 131 lands the same way; slice-132 then landed the four
// keyed ButtonGroup ItemsPanelTemplate entries keyidx 132-135 at
// [BAML+0x18D3F, BAML+0x18DB8) as live engine ItemsPanelTemplates plus the
// converted 'ButtonGroupItemStyleSelector' entry keyidx 136 and the keyed
// 'ProgressBarIndicatorAnimatedFill' brush keyidx 137, advancing
// the staged boot stop past the 'ColorPickerSliderThumbTemplate' entry (keyidx 152) to the 'ColorPickerRainbowBrush' brush (keyidx 153) at BAML+0x192B2 (landed in slice-139 with WPF-default points and a zero first offset, and slice-140 landed the 'ColorPickerOpacityBrush' brush (keyidx 154) with its checker DrawingGroup; the staged boot stop now rests at the 'CoverFlowBaseStyle' Style (keyidx 155) at BAML+0x19497).
//
// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
// the 0x38 StaticResource-value arm inside template Bindings first (plus
// the BorderThickness template member the TabTop header border needs),
// with the rest pinned. The 0x38 decode is triangulated three ways: the
// staged bytes (attr 37/38 are the per-file Binding.Converter/
// ConverterParameter rows; value ids 0/1/2 index the entry's key-block
// StaticResource declarations per the BAML KeyList[].StaticResources
// shape), the upstream GroupBoxTabTopTemplate XAML (CornerRadius with
// CornerRadiusSplitConverter/'0,0,1,1', BorderThickness with
// ThicknessSplitConverter/'1,1,1,0', CornerRadius with
// CornerRadiusSplitConverter/'1,1,0,0'), and the dotnet/wpf BAML reader
// sources (PropertyWithStaticResourceId reads attribute plus resource id;
// the id indexes the current key's StaticResources; plain-string keys
// resolve from the string table while x:Type/x:Static flags stay opaque).
// The genuine-net10 fixture verification of the deferred converter keys
// stays a next-slice step; this slice pins staged bytes, staged boot
// advance and synthetic live landing.
//
// Live counts: staged 466350-byte theme.baml with 6 staged assemblies and
// the same 2 absent binaries (System.Security.Permissions and
// System.Windows.Extensions); HandyControl stays conversion-failed.

namespace DesktopWasm.Wpf.Tests;

[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice131BamlTabTopTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice131BamlTabTopTests()
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
        // The 466350-byte theme.baml length plus this manifest pin the
        // input; the sha256 map below pins every staged binary.
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
    public void Theme_baml_lands_groupbox_tabs_then_circle_template_stop()
    {
        // Staged framing pins for the TabTop entry plus the TabLeft head
        // and the ItemsPanelTemplate head it stops at: entry 130 spans
        // [0x187F5, 0x18A8D), entry 131 spans [0x18A8D, 0x18D3F), and the
        // per-file AttributeInfo rows 37/38 (Binding.Converter/
        // ConverterParameter) plus the three key-block statics
        // (CornerRadius/Thickness/Corner) decode the Converter sites.
        var entry = LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");

        var head = new byte[] { 0x03, 0x94, 0xFF, 0x00, 0x22, 0x07, 0x00, 0xF8, 0xFE, 0x2E, 0x52, 0xFF };
        Assert.Equal(head, theme[0x187F5..0x18801]);
        Assert.Equal(0x03, theme[0x18801]);
        var grid = new byte[] { 0x03, 0x02, 0xFF, 0x00, 0x0B, 0x1D, 0x00, 0x03, 0xE3, 0xFD, 0x00 };
        Assert.Equal(grid, theme[0x18801..0x1880C]);

        var static0 = new byte[] { 0x38, 0x25, 0x00, 0x00, 0x00 };
        Assert.Equal(static0, theme[0x1888C..0x18891]);
        var param1 = new byte[] { 0x24, 0x0D, 0x26, 0x00, 0x07, 0x30, 0x2C, 0x30, 0x2C, 0x31, 0x2C, 0x31, 0x99, 0xFD };
        Assert.Equal(param1, theme[0x18891..0x1889F]);
        Assert.Equal(0x04, theme[0x1889F]);

        var static1 = new byte[] { 0x38, 0x25, 0x00, 0x01, 0x00 };
        Assert.Equal(static1, theme[0x1894B..0x18950]);
        var param2 = new byte[] { 0x24, 0x0D, 0x26, 0x00, 0x07, 0x31, 0x2C, 0x31, 0x2C, 0x31, 0x2C, 0x30, 0x99, 0xFD };
        Assert.Equal(param2, theme[0x18950..0x1895E]);
        var thicknessHead = new byte[] { 0x07, 0xFA, 0xFF, 0x03, 0xEC, 0xFF, 0x00 };
        Assert.Equal(thicknessHead, theme[0x18913..0x1891A]);

        var static2 = new byte[] { 0x38, 0x25, 0x00, 0x02, 0x00 };
        Assert.Equal(static2, theme[0x189F3..0x189F8]);
        var param3 = new byte[] { 0x24, 0x0D, 0x26, 0x00, 0x07, 0x31, 0x2C, 0x31, 0x2C, 0x30, 0x2C, 0x30, 0x99, 0xFD };
        Assert.Equal(param3, theme[0x189F8..0x18A06]);

        var key130 = new byte[] { 0x26, 0x09, 0x78, 0x00, 0x5E, 0x96, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key130, theme[0xB41D..0xB427]);
        var statics130 = new byte[] { 0x37, 0x00, 0x17, 0x00, 0x37, 0x00, 0x16, 0x00, 0x37, 0x00, 0x17, 0x00 };
        Assert.Equal(statics130, theme[0xB427..0xB433]);
        var key131 = new byte[] { 0x26, 0x09, 0x79, 0x00, 0xF6, 0x98, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key131, theme[0xB433..0xB43D]);
        var stat131 = new byte[] { 0x37, 0x00, 0x17, 0x00 };
        Assert.Equal(stat131, theme[0xB43D..0xB441]);

        Assert.Equal(head, theme[0x18A8D..0x18A99]);
        var itemsHead = new byte[] { 0x03, 0xB6, 0xFE, 0x00, 0x2E, 0x52, 0xFF, 0x03, 0x41, 0xFD, 0x00, 0x24 };
        Assert.Equal(itemsHead, theme[0x18D3F..0x18D4B]);
    }

    [Fact]
    public void Staged_boot_advances_past_gradient_to_circle_template()
    {
        // The staged boot now materializes entries #87-136 in index order
        // (the four ButtonGroup ItemsPanelTemplates plus the converted
        // StyleSelector land, plus the 'ProgressBarIndicatorAnimatedFill' brush)
        // and stops at the 'CoverFlowBaseStyle' Style (template Border Viewport3D child well-known id -729 at BAML+0x19497): the precise
        // next blocker. Every earlier stop stays absent
        // (including the intermediate items-panel head stop and the -330
        // entry-type stop), and the new head asserts the exact frontier.
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
    }

    [Fact]
    public void Staged_shape_tab_top_lands_live_template()
    {
        // The staged GroupBoxTabTopTemplate entry shape (GroupBox target,
        // Grid visual with two RowDefinitions plus two Borders carrying
        // TemplateBinding and TemplatedParent-bound values with
        // StaticResource Converters plus ConverterParameter simples) lands
        // as a live engine ControlTemplate. Per-file ids 0-7 stand in for
        // the staged rows (TargetType/RowDefinitions/Path/RelativeSource/
        // CornerRadius/Converter/ConverterParameter/SnapsToDevicePixels);
        // the staged switch asserts pin the real numbers.
        var bytes = TabDictionary("GroupBoxTabTopTemplate", TabEntry().ToArray());
        var dictionary = BamlReader.LoadDictionary(bytes);
        var template = Assert.IsType<ControlTemplate>(dictionary["GroupBoxTabTopTemplate"]);
        Assert.Same(typeof(GroupBox), template.TargetType);
        Assert.Empty(template.Triggers);
        Assert.Empty(template.TemplateNames);
        var grid = Assert.IsType<Grid>(template.VisualTree);
        Assert.Equal(2, grid.RowDefinitions.Count);
        Assert.Equal(GridLength.Auto, grid.RowDefinitions[0].Height);
        Assert.Equal(new GridLength(1, GridUnitType.Star), grid.RowDefinitions[1].Height);
        Assert.Empty(grid.ColumnDefinitions);
        Assert.Equal(2, grid.Children.Count);

        var content = Assert.IsType<Border>(grid.Children[0]);
        Assert.Equal(1, Grid.GetRow(content));
        AssertCornerConverterBinding(content, "(hc:BorderElement.CornerRadius)", "CornerRadiusSplitConverter", "0,0,1,1");
        var contentPresenter = Assert.IsType<ContentPresenter>(content.Child);
        Assert.Null(BindingOperations.GetBinding(contentPresenter, FrameworkElement.MarginProperty));

        var header = Assert.IsType<Border>(grid.Children[1]);
        Assert.Equal(0, Grid.GetRow(header));
        var minHeight = BindingOperations.GetBinding(header, FrameworkElement.MinHeightProperty);
        Assert.NotNull(minHeight);
        Assert.Equal("(hc:TitleElement.MinHeight)", minHeight!.Path.Path);
        Assert.Equal(RelativeSourceMode.TemplatedParent, minHeight.RelativeSource!.Mode);
        Assert.Null(minHeight.DeferredConverterKey);
        Assert.Null(minHeight.ConverterParameter);
        var thickness = BindingOperations.GetBinding(header, Border.BorderThicknessProperty);
        Assert.NotNull(thickness);
        Assert.Equal("BorderThickness", thickness!.Path.Path);
        Assert.Equal(RelativeSourceMode.TemplatedParent, thickness.RelativeSource!.Mode);
        Assert.Equal("ThicknessSplitConverter", thickness.DeferredConverterKey);
        Assert.Equal("1,1,1,0", thickness.ConverterParameter);
        AssertCornerConverterBinding(header, "(hc:BorderElement.CornerRadius)", "CornerRadiusSplitConverter", "1,1,0,0");
        var background = BindingOperations.GetBinding(header, Control.BackgroundProperty);
        Assert.NotNull(background);
        Assert.Equal("(hc:TitleElement.Background)", background!.Path.Path);
        Assert.Equal(RelativeSourceMode.TemplatedParent, background.RelativeSource!.Mode);
        Assert.Null(background.DeferredConverterKey);
        var headerPresenter = Assert.IsType<ContentPresenter>(header.Child);
        Assert.Equal("Header", headerPresenter.ContentSource);
        Assert.True(headerPresenter.RecognizesAccessKey);
        var padding = BindingOperations.GetBinding(headerPresenter, FrameworkElement.MarginProperty);
        Assert.NotNull(padding);
        Assert.Equal("(hc:TitleElement.Padding)", padding!.Path.Path);
        Assert.Equal(RelativeSourceMode.TemplatedParent, padding.RelativeSource!.Mode);

        var bound = template.TemplateBindings.Select(b => (b.Element, b.Property)).ToList();
        Assert.Equal(
            new (object, string)[]
            {
                (content, "Background"),
                (content, "BorderThickness"),
                (content, "BorderBrush"),
                (contentPresenter, "Padding"),
                (contentPresenter, "SnapsToDevicePixels"),
                (header, "BorderBrush"),
                (headerPresenter, "Foreground"),
                (headerPresenter, "VerticalContentAlignment"),
                (headerPresenter, "HorizontalContentAlignment"),
            },
            bound);
    }

    [Fact]
    public void Staged_shape_tab_top_has_no_inflation_markup()
    {
        // The landed TabTop retains its visual root without inflation
        // markup, so inflating throws the precise BAML-tree inflation
        // boundary instead of sharing one instance.
        var bytes = TabDictionary("GroupBoxTabTopTemplate", TabEntry().ToArray());
        var dictionary = BamlReader.LoadDictionary(bytes);
        var template = Assert.IsType<ControlTemplate>(dictionary["GroupBoxTabTopTemplate"]);
        var error = Assert.Throws<InvalidOperationException>(() => template.LoadContent());
        Assert.Contains("no inflation markup", error.Message);
        Assert.Contains("BAML-tree inflation", error.Message);
    }

    [Fact]
    public void Border_thickness_backs_onto_shared_control_instance()
    {
        // Border.BorderThickness is the shared Control thickness DP like
        // WPF (BorderThickness complex bindings transfer onto it), so the
        // same instance resolves from both owner names.
        Assert.Same(Control.BorderThicknessProperty, Border.BorderThicknessProperty);
        Assert.Same(Control.BorderThicknessProperty, DependencyProperty.FromName(typeof(Border), "BorderThickness"));
    }

    [Fact]
    public void Converter_parameter_before_converter_lands()
    {
        // Attribute order inside the Binding element is irrelevant: a
        // ConverterParameter simple before the StaticResource Converter
        // lands the same deferred pair.
        XamlLoader.CustomElementSource = null;
        var binding = new B()
            .Raw(0x03).I16(-20).Raw(0x00)
            .Sized(0x24, q => q.I16(2).Str("(hc:BorderElement.CornerRadius)").I16(-492))
            .Sized(0x24, q => q.I16(6).Str("0,0,1,1").I16(-615))
            .Raw(0x07).I16(3)
            .Raw(RelativeSourceTemplatedParent().ToArray())
            .Raw(0x08)
            .Raw(0x38).U16(5).U16(0)
            .Raw(0x04);
        var visual = TabGridWith(TabBorderWith(binding.ToArray(), "0"));
        var dictionary = BamlReader.LoadDictionary(TabTemplateDictionary("T", visual));
        var template = Assert.IsType<ControlTemplate>(dictionary["T"]);
        var grid = Assert.IsType<Grid>(template.VisualTree);
        Assert.Equal(1, grid.Children.Count);
        var border = Assert.IsType<Border>(grid.Children[0]);
        var landed = BindingOperations.GetBinding(border, Border.CornerRadiusProperty);
        Assert.NotNull(landed);
        Assert.Equal("CornerRadiusSplitConverter", landed!.DeferredConverterKey);
        Assert.Equal("0,0,1,1", landed.ConverterParameter);
    }

    [Fact]
    public void Static_converter_wrong_attribute_fails_precise()
    {
        // A 0x38 over any other member (here the ConverterParameter row)
        // fails naming the member: template Bindings take the Converter
        // plus ConverterParameter, and the StaticResource value is the
        // Converter arm.
        XamlLoader.CustomElementSource = null;
        var binding = TabBindingBytes("(hc:BorderElement.CornerRadius)", 6, 0, "0,0,1,1");
        var visual = TabGridWith(TabBorderWith(binding, "0"));
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(TabTemplateDictionary("T", visual)));
        Assert.Contains("holds a StaticResource value for 'ConverterParameter'", error.Message);
        Assert.Contains("bindings take the Converter plus ConverterParameter", error.Message);
    }

    [Fact]
    public void Static_converter_unknown_index_fails_precise()
    {
        // A 0x38 value id past the entry's key-block StaticResource list
        // fails naming the id and the list length.
        XamlLoader.CustomElementSource = null;
        var binding = TabBindingBytes("(hc:BorderElement.CornerRadius)", 5, 7, "0,0,1,1");
        var visual = TabGridWith(TabBorderWith(binding, "0"));
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(TabTemplateDictionary("T", visual)));
        Assert.Contains("names static resource 7 (only 3 indexed static values", error.Message);
    }

    [Fact]
    public void Static_converter_without_index_fails_precise()
    {
        // A 0x38 in an entry whose key carries no StaticResource
        // declarations fails instead of resolving an empty index.
        XamlLoader.CustomElementSource = null;
        var binding = TabBindingBytes("(hc:BorderElement.CornerRadius)", 5, 0, "0,0,1,1");
        var visual = TabGridWith(TabBorderWith(binding, "0"));
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(TabTemplateDictionaryNoStatics("T", visual)));
        Assert.Contains("with no StaticResource index", error.Message);
    }

    [Fact]
    public void Static_converter_duplicate_fails_precise()
    {
        // Two 0x38 Converters on one template Binding fail instead of
        // overwriting the deferred key.
        XamlLoader.CustomElementSource = null;
        var binding = new B()
            .Raw(0x03).I16(-20).Raw(0x00)
            .Sized(0x24, q => q.I16(2).Str("(hc:BorderElement.CornerRadius)").I16(-492))
            .Raw(0x38).U16(5).U16(0)
            .Raw(0x38).U16(5).U16(1)
            .Raw(0x04);
        var visual = TabGridWith(TabBorderWith(binding.ToArray(), "0"));
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(TabTemplateDictionary("T", visual)));
        Assert.Contains("duplicate Converter on the template Binding", error.Message);
    }

    [Fact]
    public void Static_converter_non_string_key_fails_precise()
    {
        // A 0x38 naming a non-string static (here an x:Type-flagged index
        // entry) fails: template Binding Converters take string-keyed
        // converters; x:Type/x:Static static values are a follow-up.
        XamlLoader.CustomElementSource = null;
        var binding = TabBindingBytes("(hc:BorderElement.CornerRadius)", 5, 1, "0,0,1,1");
        var visual = TabGridWith(TabBorderWith(binding, "0"));
        var statics = new (byte Flags, ushort Id)[] { (0, 1), (2, 65326) };
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(TabTemplateDictionaryWith("T", visual, statics)));
        Assert.Contains("names a non-string static resource", error.Message);
        Assert.Contains("bindings take string-keyed converters", error.Message);
    }

    [Fact]
    public void Converter_parameter_wrong_converter_fails_precise()
    {
        // The ConverterParameter simple takes the StringConverter (the
        // staged '0,0,1,1' params carry -615); any other converter fails
        // naming the converter used.
        XamlLoader.CustomElementSource = null;
        var binding = new B()
            .Raw(0x03).I16(-20).Raw(0x00)
            .Sized(0x24, q => q.I16(2).Str("(hc:BorderElement.CornerRadius)").I16(-492))
            .Raw(0x38).U16(5).U16(0)
            .Sized(0x24, q => q.I16(6).Str("0,0,1,1").I16(-46))
            .Raw(0x04);
        var visual = TabGridWith(TabBorderWith(binding.ToArray(), "0"));
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(TabTemplateDictionary("T", visual)));
        Assert.Contains("uses converter -46", error.Message);
        Assert.Contains("take the StringConverter", error.Message);
    }

    [Fact]
    public void Converter_parameter_duplicate_fails_precise()
    {
        // Two ConverterParameter simples on one template Binding fail
        // instead of overwriting the parameter.
        XamlLoader.CustomElementSource = null;
        var binding = new B()
            .Raw(0x03).I16(-20).Raw(0x00)
            .Sized(0x24, q => q.I16(2).Str("(hc:BorderElement.CornerRadius)").I16(-492))
            .Sized(0x24, q => q.I16(6).Str("0,0,1,1").I16(-615))
            .Sized(0x24, q => q.I16(6).Str("1,1,1,0").I16(-615))
            .Raw(0x04);
        var visual = TabGridWith(TabBorderWith(binding.ToArray(), "0"));
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(TabTemplateDictionary("T", visual)));
        Assert.Contains("duplicate ConverterParameter on the template Binding", error.Message);
    }

    [Fact]
    public void Binding_unknown_static_record_fails_precise()
    {
        // A static-value record directly inside a template Binding (here
        // the 0x37 OptimizedStaticResource form) fails naming the record:
        // template Bindings take the 0x38 PropertyWithStaticResourceId
        // Converter form.
        XamlLoader.CustomElementSource = null;
        var binding = new B()
            .Raw(0x03).I16(-20).Raw(0x00)
            .Sized(0x24, q => q.I16(2).Str("(hc:BorderElement.CornerRadius)").I16(-492))
            .Raw(0x37, 0x00).U16(1)
            .Raw(0x04);
        var visual = TabGridWith(TabBorderWith(binding.ToArray(), "0"));
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(TabTemplateDictionary("T", visual)));
        Assert.Contains("holds record 0x37", error.Message);
        Assert.Contains("bindings take a path plus Converter plus ConverterParameter plus RelativeSource", error.Message);
    }

    [Fact]
    public void Static_resource_bad_key_id_fails_precise()
    {
        // A key-block 0x37 naming a string-table id the file does not have
        // fails instead of collecting a dangling static.
        XamlLoader.CustomElementSource = null;
        var visual = TabGridWith(TabBorderWith(
            TabBindingBytes("(hc:BorderElement.CornerRadius)", 5, 0, "0,0,1,1"), "0"));
        var statics = new (byte Flags, ushort Id)[] { (0, 500) };
        var error = Assert.Throws<XamlLoadException>(
            () => BamlReader.LoadDictionary(TabTemplateDictionaryWith("T", visual, statics)));
        Assert.Contains("has no StringInfo record", error.Message);
    }

    [Fact]
    public void Static_resource_without_key_fails_precise()
    {
        // A 0x37 before any dictionary key fails: static declarations ride
        // with a key.
        XamlLoader.CustomElementSource = null;
        var entry = TabTemplateBody(TabGridWith(TabBorderWith(
            TabBindingBytes("(hc:BorderElement.CornerRadius)", 5, 0, "0,0,1,1"), "0")));
        var bytes = new B().Raw(TabHeader()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(0).I16(-108).Raw(0x00).Str("TargetType"))
            .Sized(0x20, p => p.U16(0).Str("T"))
            .Sized(0x20, p => p.U16(1).Str("CornerRadiusSplitConverter"))
            .Deferable()
            .Raw(0x37, 0x00).U16(1)
            .Sized(0x26, p => TabKeyPair(p, shared: true))
            .Raw(entry).FinishDictionary().ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(bytes));
        Assert.Contains("without a dictionary key", error.Message);
    }

    private static void AssertCornerConverterBinding(Border border, string path, string key, string parameter)
    {
        var binding = BindingOperations.GetBinding(border, Border.CornerRadiusProperty);
        Assert.NotNull(binding);
        Assert.Equal(path, binding!.Path.Path);
        Assert.Equal(RelativeSourceMode.TemplatedParent, binding.RelativeSource!.Mode);
        Assert.Equal(key, binding.DeferredConverterKey);
        Assert.Equal(parameter, binding.ConverterParameter);
    }

    /// <summary>Builds the staged GroupBoxTabTopTemplate entry shape: a
    /// GroupBox-targeted ControlTemplate holding the Grid visual with two
    /// RowDefinitions plus two Borders carrying TemplateBinding and
    /// TemplatedParent-bound values with StaticResource Converters.
    /// Per-file ids 0-7 stand in for the staged rows (TargetType/
    /// RowDefinitions/Path/RelativeSource/CornerRadius/Converter/
    /// ConverterParameter/SnapsToDevicePixels).</summary>
    private static B TabEntry() => new B()
        .Raw(0x03).I16(-108).Raw(0x00)
        .Raw(0x22).I16(0).I16(-264)
        .Raw(0x2E).I16(-174)
        .Raw(0x03).I16(-254).Raw(0x00)
        .Raw(0x0B).U16(1)
        .Raw(TabRow("Auto").ToArray())
        .Raw(TabRow(null).ToArray())
        .Raw(0x0C)
        .Raw(0x2E).I16(-213)
        .Raw(TabContentBorder().ToArray())
        .Raw(TabHeaderBorder().ToArray())
        .Raw(0x04)
        .Raw(0x04);

    /// <summary>Builds one template RowDefinition with an optional Height
    /// simple (null keeps the star default).</summary>
    private static B TabRow(string? height)
    {
        var row = new B().Raw(0x03).I16(-541).Raw(0x00);
        if (height is not null)
            row.Sized(0x24, q => q.I16(-94).Str(height).I16(-256));
        return row.Raw(0x04);
    }

    /// <summary>Builds the staged content Border: Grid.Row '1', the
    /// CornerRadius TemplatedParent binding with a StaticResource
    /// Converter, Background/BorderThickness/BorderBrush TemplateBindings
    /// and the content presenter.</summary>
    private static B TabContentBorder() => new B()
        .Raw(0x03).I16(-50).Raw(0x00)
        .Sized(0x24, q => q.I16(-63).Str("1").I16(-315))
        .Raw(0x07).I16(4)
        .Raw(TabBindingBytes("(hc:BorderElement.CornerRadius)", 5, 0, "0,0,1,1"))
        .Raw(0x08)
        .Raw(0x23).I16(-4).U16(634).I16(-24)
        .Raw(0x23).I16(-6).U16(634).I16(-26)
        .Raw(0x23).I16(-5).U16(634).I16(-25)
        .Raw(0x2E).I16(-161)
        .Raw(TabContentPresenter().ToArray())
        .Raw(0x04);

    /// <summary>Builds the staged header Border: Grid.Row '0', the
    /// MinHeight TemplatedParent binding, the BorderThickness
    /// TemplatedParent binding with a StaticResource Converter, the
    /// BorderBrush TemplateBinding, the Background and CornerRadius
    /// TemplatedParent bindings (the latter with a StaticResource
    /// Converter) and the header presenter.</summary>
    private static B TabHeaderBorder() => new B()
        .Raw(0x03).I16(-50).Raw(0x00)
        .Sized(0x24, q => q.I16(-63).Str("0").I16(-315))
        .Raw(0x07).I16(-52)
        .Raw(TabPlainBindingBytes("(hc:TitleElement.MinHeight)"))
        .Raw(0x08)
        .Raw(0x07).I16(-6)
        .Raw(TabCtorBindingBytes("BorderThickness", 1, "1,1,1,0"))
        .Raw(0x08)
        .Raw(0x23).I16(-5).U16(634).I16(-25)
        .Raw(0x07).I16(-4)
        .Raw(TabPlainBindingBytes("(hc:TitleElement.Background)"))
        .Raw(0x08)
        .Raw(0x07).I16(4)
        .Raw(TabBindingBytes("(hc:BorderElement.CornerRadius)", 5, 2, "1,1,0,0"))
        .Raw(0x08)
        .Raw(0x2E).I16(-161)
        .Raw(TabHeaderPresenter().ToArray())
        .Raw(0x04);

    /// <summary>Builds one template Binding with a Path PWC, the
    /// TemplatedParent RelativeSource child, a StaticResource Converter
    /// 0x38 over the given Converter attribute row and a
    /// ConverterParameter simple.</summary>
    private static byte[] TabBindingBytes(string path, ushort converterAttr, ushort staticId, string parameter) => new B()
        .Raw(0x03).I16(-20).Raw(0x00)
        .Sized(0x24, q => q.I16(2).Str(path).I16(-492))
        .Raw(0x07).I16(3)
        .Raw(RelativeSourceTemplatedParent().ToArray())
        .Raw(0x08)
        .Raw(0x38).U16(converterAttr).U16(staticId)
        .Sized(0x24, q => q.I16(6).Str(parameter).I16(-615))
        .Raw(0x04).ToArray();

    /// <summary>Builds one template Binding with a constructor-form Path,
    /// the TemplatedParent RelativeSource child, a StaticResource
    /// Converter 0x38 and a ConverterParameter simple.</summary>
    private static byte[] TabCtorBindingBytes(string path, ushort staticId, string parameter) => new B()
        .Raw(0x03).I16(-20).Raw(0x00)
        .Raw(0x2A)
        .Sized(0x10, q => q.Str(path))
        .Raw(0x2B)
        .Raw(0x07).I16(3)
        .Raw(RelativeSourceTemplatedParent().ToArray())
        .Raw(0x08)
        .Raw(0x38).U16(5).U16(staticId)
        .Sized(0x24, q => q.I16(6).Str(parameter).I16(-615))
        .Raw(0x04).ToArray();

    /// <summary>Builds one template Binding with a Path PWC plus the
    /// TemplatedParent RelativeSource child (no Converter).</summary>
    private static byte[] TabPlainBindingBytes(string path) => new B()
        .Raw(0x03).I16(-20).Raw(0x00)
        .Sized(0x24, q => q.I16(2).Str(path).I16(-492))
        .Raw(0x07).I16(3)
        .Raw(RelativeSourceTemplatedParent().ToArray())
        .Raw(0x08)
        .Raw(0x04).ToArray();

    /// <summary>Builds the TemplatedParent RelativeSource element with its
    /// constructor argument and element end (the complex wrapper adds its
    /// own close).</summary>
    private static B RelativeSourceTemplatedParent() => new B()
        .Raw(0x03).I16(-516).Raw(0x00)
        .Raw(0x2A)
        .Sized(0x10, q => q.Str("TemplatedParent"))
        .Raw(0x2B)
        .Raw(0x04);

    /// <summary>Builds the staged content presenter (Margin/Padding plus
    /// SnapsToDevicePixels TemplateBindings).</summary>
    private static B TabContentPresenter() => new B()
        .Raw(0x03).I16(-102).Raw(0x00)
        .Raw(0x23).I16(-49).U16(634).I16(-35)
        .Raw(0x23).I16(7).U16(634).I16(7)
        .Raw(0x04);

    /// <summary>Builds the staged header presenter (Foreground/
    /// VerticalContentAlignment/HorizontalContentAlignment TemplateBindings,
    /// ContentSource/RecognizesAccessKey simples and the Padding Margin
    /// binding).</summary>
    private static B TabHeaderPresenter() => new B()
        .Raw(0x03).I16(-102).Raw(0x00)
        .Raw(0x23).I16(-121).U16(634).I16(-32)
        .Raw(0x23).I16(-56).U16(634).I16(-38)
        .Raw(0x23).I16(-48).U16(634).I16(-33)
        .Sized(0x24, q => q.I16(-20).Str("Header").I16(-615))
        .Sized(0x24, q => q.I16(-23).Str("True").I16(-46))
        .Raw(0x07).I16(-49)
        .Raw(TabPlainBindingBytes("(hc:TitleElement.Padding)"))
        .Raw(0x08)
        .Raw(0x04);

    /// <summary>Wraps one template Border in a Grid with its Children
    /// content and Grid.Row simple (for the binding dispatch tests).</summary>
    private static byte[] TabBorderWith(byte[] binding, string row) => new B()
        .Raw(0x03).I16(-50).Raw(0x00)
        .Sized(0x24, q => q.I16(-63).Str(row).I16(-315))
        .Raw(0x07).I16(4)
        .Raw(binding)
        .Raw(0x08)
        .Raw(0x04).ToArray();

    /// <summary>Wraps one template visual in a Grid with its Children
    /// content (the RowDefinitions list stays empty).</summary>
    private static byte[] TabGridWith(byte[] visual) => new B()
        .Raw(0x03).I16(-254).Raw(0x00)
        .Raw(0x2E).I16(-213)
        .Raw(visual)
        .Raw(0x04).ToArray();

    /// <summary>Wraps one template visual in a keyed GroupBox-targeted
    /// ControlTemplate entry body (for the binding dispatch tests).</summary>
    private static byte[] TabTemplateBody(byte[] visual) => new B()
        .Raw(0x03).I16(-108).Raw(0x00)
        .Raw(0x22).I16(0).I16(-264)
        .Raw(0x2E).I16(-174)
        .Raw(visual)
        .Raw(0x04).ToArray();

    /// <summary>Builds a single-entry keyed template dictionary over one
    /// visual with the staged TabTop static triple (for the binding
    /// dispatch tests).</summary>
    private static byte[] TabTemplateDictionary(string key, byte[] visual) =>
        TabTemplateDictionaryWith(key, visual, new (byte Flags, ushort Id)[] { (0, 1), (0, 2), (0, 1) });

    /// <summary>Builds a single-entry keyed template dictionary over one
    /// visual with no StaticResource declarations (for the missing-index
    /// test).</summary>
    private static byte[] TabTemplateDictionaryNoStatics(string key, byte[] visual) =>
        TabShapeDictionary(key, TabTemplateBody(visual), shared: true, statics: Array.Empty<(byte Flags, ushort Id)>());

    /// <summary>Builds a single-entry keyed template dictionary over one
    /// visual with caller-supplied StaticResource declarations (for the
    /// static-index tests).</summary>
    private static byte[] TabTemplateDictionaryWith(string key, byte[] visual, (byte Flags, ushort Id)[] statics) =>
        TabShapeDictionary(key, TabTemplateBody(visual), shared: true, statics: statics);

    /// <summary>Builds a single-entry keyed template dictionary over the
    /// engine GroupBox TargetType (well-known tables only, no TypeInfo rows;
    /// per-file AttributeInfo ids 0-7 stand in for the staged rows).</summary>
    private static byte[] TabDictionary(string key, byte[] entry) =>
        TabShapeDictionary(key, entry, shared: true, statics: new (byte Flags, ushort Id)[] { (0, 1), (0, 2), (0, 1) });

    private static B TabKeyPair(B p, bool shared)
    {
        p.U16(0).U32(0);
        return shared ? p.U16(0) : p.Raw(0x00, 0x01);
    }

    private static byte[] TabShapeDictionary(string key, byte[] entry, bool shared, (byte Flags, ushort Id)[] statics) => new B().Raw(TabHeader()).Raw(0x01)
        .Raw(0x03).I16(-524).Raw(0x00)
        .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
        .Sized(0x1F, p => p.I16(0).I16(-108).Raw(0x00).Str("TargetType"))
        .Sized(0x1F, p => p.I16(1).I16(-254).Raw(0x00).Str("RowDefinitions"))
        .Sized(0x1F, p => p.I16(2).I16(-20).Raw(0x00).Str("Path"))
        .Sized(0x1F, p => p.I16(3).I16(-20).Raw(0x00).Str("RelativeSource"))
        .Sized(0x1F, p => p.I16(4).I16(-50).Raw(0x00).Str("CornerRadius"))
        .Sized(0x1F, p => p.I16(5).I16(-20).Raw(0x00).Str("Converter"))
        .Sized(0x1F, p => p.I16(6).I16(-20).Raw(0x00).Str("ConverterParameter"))
        .Sized(0x1F, p => p.I16(7).I16(-102).Raw(0x00).Str("SnapsToDevicePixels"))
        .Sized(0x20, p => p.U16(0).Str(key))
        .Sized(0x20, p => p.U16(1).Str("CornerRadiusSplitConverter"))
        .Sized(0x20, p => p.U16(2).Str("ThicknessSplitConverter"))
        .Deferable()
        .Sized(0x26, p => TabKeyPair(p, shared))
        .Raw(TabStatics(statics).ToArray())
        .Raw(entry).FinishDictionary().ToArray();

    /// <summary>Emits key-block OptimizedStaticResource declarations.</summary>
    private static B TabStatics((byte Flags, ushort Id)[] statics)
    {
        var b = new B();
        foreach (var (flags, id) in statics)
            b.Raw(0x37, flags).U16(id);
        return b;
    }

    private static byte[] TabHeader() => new B()
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
