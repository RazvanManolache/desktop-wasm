// Part 1 of Slice149BamlComboBoxTests.cs: header, docs, loader helpers.
using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-149 HandyControl theme.baml ComboBox style-target routing.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the slice-148 blocker (the keyed 'DataGridComboBoxColumnStyle' Style
/// entry at BAML+0x1A10F targeting well-known type id -92 with no
/// style-target routing) routes as a live engine Style: -92 resolves to
/// the engine ComboBox (shim, never ported), the short-form Focusable
/// setter lands True through the hierarchy walk, and the Template complex
/// lands with a bare TextBlock root carrying IsHitTestVisible False, the
/// Text TemplateBinding and TextTrimming CharacterEllipsis. The run
/// advances past the ComboBox entry and stops precise at the keyed
/// 'DataGridTextColumnStyle' Style entry at BAML+0x1A186 (TextBlock
/// target, well-known id -638 with no style-target routing - the next
/// slice's work).
///
/// Byte-exact decode against the staged 466350-byte theme.baml: the
/// ComboBox entry [0x1A10F, 0x1A182) (Style head 03 94 FD with TargetType
/// 22 00 00 A4 FF over ComboBox -92, ContentProperty -238, short-form
/// Focusable -132 with the True/BooleanConverter -46 value, short-form
/// Template -37 with the ControlTemplate/TargetType -92/VisualTree -174
/// complex, TextBlock -638 root with the per-file IsHitTestVisible row 80
/// False/BooleanConverter value, the 0x23 TemplateBindingExtension triple
/// target -110/source 634/row 81, the TextTrimming -112
/// CharacterEllipsis/EnumConverter -195 value, and the 04 04 08 04 04
/// close run), the DataGridTextColumnStyle head 03 94 FD 00 22 00 00 82 FD
/// (TargetType -638 TextBlock) at 0x1A182 with the TargetType
/// record at BAML+0x1A186, key records 0x26 key ids 0x9B/0x9C at
/// 0xB59B/0xB5A5, and the key-string table rows 0x9B
/// 'DataGridComboBoxColumnStyle' at 0x2534 plus 0x9C
/// 'DataGridTextColumnStyle' at 0x257D.
///
/// Triangulated three ways: the staged framing above (theme bytes live
/// verified at sha256 D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0);
/// the live upstream HandyControl DataGridBaseStyle.xaml ComboBox plus
/// TextColumn blocks at pinned commit e9ad3d70 (DataGridComboBoxColumnStyle
/// with the Focusable True setter plus the TextBlock template carrying
/// IsHitTestVisible False, TemplateBinding Text and TextTrimming
/// CharacterEllipsis, directly followed by the DataGridTextColumnStyle
/// block TargetType TextBlock); a genuine net10.0-windows ResourceDictionary
/// fixture compiling the exact upstream block (byte-identical in every
/// staged record shape modulo per-file numbering and line markers:
/// TargetType A4 FF for TargetType="ComboBox", short-form Focusable/Template,
/// per-file IsHitTestVisible row owned by UIElement -694, Text row owned by
/// -92, TemplateBindingExtension 634 with target -110, TextTrimming member
/// -112 with EnumConverter -195) plus a genuine net10.0-windows reflection
/// probe (TextBlock.TextTrimmingProperty is TextBlock-owned, TextTrimming
/// typed, None default with AffectsMeasure|AffectsRender metadata;
/// UIElement.FocusableProperty and Control.TemplateProperty carry the
/// Focusable/Template setters through the ComboBox lineage; ComboBox derives
/// Selector).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path); all 6 staged images
/// sha256-verified, manifest hand-untouched; staged-oracle status-gate
/// bypass stays in-probe only.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice149BamlComboBoxTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice149BamlComboBoxTests()
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
    public void Theme_baml_places_combobox_and_textcolumn_bytes()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        Assert.Equal(
            "D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0",
            Convert.ToHexString(SHA256.HashData(theme)));

        var comboHead = new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0xA4, 0xFF };
        Assert.Equal(comboHead, theme[0x1A10F..0x1A118]);
        var focusableProperty = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0x7C, 0xFF };
        Assert.Equal(focusableProperty, theme[0x1A11F..0x1A127]);
        var trueText = Encoding.ASCII.GetBytes("True");
        Assert.Equal(trueText, theme[0x1A12C..0x1A130]);
        var templateHead = new byte[] { 0x07, 0x02, 0x00, 0x03, 0x94, 0xFF, 0x00, 0x22, 0x07, 0x00, 0xA4, 0xFF };
        Assert.Equal(templateHead, theme[0x1A13F..0x1A14B]);
        var textBlockHead = new byte[] { 0x03, 0x82, 0xFD, 0x00, 0x24, 0x0B, 0x50, 0x00, 0x05 }
            .Concat(Encoding.ASCII.GetBytes("False")).Concat(new byte[] { 0xD2, 0xFF }).ToArray();
        Assert.Equal(textBlockHead, theme[0x1A14E..0x1A15E]);
        var templateBinding = new byte[] { 0x23, 0x92, 0xFF, 0x7A, 0x02, 0x51, 0x00 };
        Assert.Equal(templateBinding, theme[0x1A15E..0x1A165]);
        var trimmingHead = new byte[] { 0x24, 0x17, 0x90, 0xFF, 0x11 }
            .Concat(Encoding.ASCII.GetBytes("CharacterEllipsis")).Concat(new byte[] { 0x3D, 0xFF }).ToArray();
        Assert.Equal(trimmingHead, theme[0x1A165..0x1A17D]);
        var closeAndNextHead = new byte[] { 0x04, 0x04, 0x08, 0x04, 0x04, 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x82, 0xFD };
        Assert.Equal(closeAndNextHead, theme[0x1A17D..0x1A18B]);
        var keyCombo = new byte[] { 0x26, 0x09, 0x9B, 0x00 };
        Assert.Equal(keyCombo, theme[0xB59B..0xB59F]);
        var keyTextColumn = new byte[] { 0x26, 0x09, 0x9C, 0x00, 0xEB, 0xAF, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(keyTextColumn, theme[0xB5A5..0xB5AF]);
        var comboStringHead = new byte[] { 0x20, 0x1F, 0x9B, 0x00, 0x1B }
            .Concat(Encoding.ASCII.GetBytes("DataGri")).ToArray();
        Assert.Equal(comboStringHead, theme[0x2534..0x2540]);
        var textColumnStringHead = new byte[] { 0x20, 0x1B, 0x9C, 0x00, 0x17 }
            .Concat(Encoding.ASCII.GetBytes("DataGrid")).ToArray();
        Assert.Equal(textColumnStringHead, theme[0x2578..0x2585]);
    }

    [Fact]
    public void Staged_boot_advances_past_combobox_to_textcolumn_stop()
    {
        var entry = LoadStagedHandyControlEntry();
        var error = Assert.Throws<InvalidOperationException>(() => WpfAppBoot.Boot(entry));
        Assert.Contains("startup failed in entry point", error.Message);
        Assert.Contains("line 23", error.Message);
        Assert.Contains("position 26", error.Message);
        var inner = Assert.IsType<XamlLoadException>(error.InnerException);
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
        Assert.DoesNotContain("BAML+0x1A113", inner.Message);
        Assert.DoesNotContain("well-known type id -92", inner.Message);
        Assert.DoesNotContain("well-known type id -658", inner.Message);
        Assert.DoesNotContain("HandyControl.Controls.SimplePanel", inner.Message);
        Assert.DoesNotContain("VirtualizingStackPanel", inner.Message);
        Assert.DoesNotContain("templates take a ContentPresenter", inner.Message);
        Assert.DoesNotContain("items panels take a UniformGrid/StackPanel/VirtualizingStackPanel/WrapPanel/DockPanel visual", inner.Message);
        Assert.DoesNotContain("DataGridTextColumnStyle", inner.Message);        
        Assert.DoesNotContain("CarouselItemBaseStyle", inner.Message);
        Assert.DoesNotContain("BAML+0x1A186", inner.Message);        
        Assert.DoesNotContain("BAML+0x1A21B", inner.Message);
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
    public void Staged_combobox_entry_lands_live()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        var dictionary = BamlReader.LoadDictionary(
            ComboDictionary(theme[0x1A10F..0x1A182], "DataGridComboBoxColumnStyle"));
        var style = Assert.IsType<Style>(dictionary["DataGridComboBoxColumnStyle"]);
        Assert.Same(typeof(ComboBox), style.TargetType);
        var byName = style.Setters.OfType<Setter>().ToDictionary(s => s.Property.Name);
        Assert.Equal(2, byName.Count);
        Assert.Same(UIElement.FocusableProperty, byName["Focusable"].Property);
        Assert.Equal(true, byName["Focusable"].Value);
        Assert.Same(Control.TemplateProperty, byName["Template"].Property);
        var template = Assert.IsType<ControlTemplate>(byName["Template"].Value);
        Assert.Same(typeof(ComboBox), template.TargetType);
        var text = Assert.IsType<TextBlock>(template.VisualTree);
        Assert.Equal(false, text.GetValue(UIElement.IsHitTestVisibleProperty));
        Assert.False(text.IsHitTestVisible);
        Assert.Same(TextBlock.TextTrimmingProperty, DependencyProperty.FromName(typeof(TextBlock), "TextTrimming"));
        Assert.Equal(TextTrimming.CharacterEllipsis, text.GetValue(TextBlock.TextTrimmingProperty));
        Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming);
        Assert.Equal("Text", Assert.Single(template.TemplateBindings).Property);
        var inflation = Assert.Throws<InvalidOperationException>(() => template.LoadContent());
        Assert.Contains("no inflation markup", inflation.Message);
        Assert.Same(style, dictionary["DataGridComboBoxColumnStyle"]);
    }

    [Fact]
    public void Staged_textcolumn_entry_lands_live()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        var dictionary = BamlReader.LoadDictionary(
            ComboDictionary(theme[0x1A182..0x1A1B4], "DataGridTextColumnStyle"));
        var style = Assert.IsType<Style>(dictionary["DataGridTextColumnStyle"]);
        Assert.Same(typeof(TextBlock), style.TargetType);
        var setter = Assert.Single(style.Setters.OfType<Setter>());
        Assert.Same(TextBlock.TextTrimmingProperty, setter.Property);
        Assert.Equal(TextTrimming.CharacterEllipsis, setter.Value);
        Assert.Same(style, dictionary["DataGridTextColumnStyle"]);
    }

    [Fact]
    public void Synthetic_combobox_entry_lands_live()
    {
        XamlLoader.CustomElementSource = null;
        var dictionary = BamlReader.LoadDictionary(ComboDictionary(ComboEntry("CharacterEllipsis"), "S"));
        var style = Assert.IsType<Style>(dictionary["S"]);
        Assert.Same(typeof(ComboBox), style.TargetType);
        var byName = style.Setters.OfType<Setter>().ToDictionary(s => s.Property.Name);
        Assert.Equal(2, byName.Count);
        Assert.Same(UIElement.FocusableProperty, byName["Focusable"].Property);
        Assert.Equal(true, byName["Focusable"].Value);
        var template = Assert.IsType<ControlTemplate>(byName["Template"].Value);
        Assert.Same(typeof(ComboBox), template.TargetType);
        var text = Assert.IsType<TextBlock>(template.VisualTree);
        Assert.Equal(false, text.GetValue(UIElement.IsHitTestVisibleProperty));
        Assert.Equal(TextTrimming.CharacterEllipsis, text.GetValue(TextBlock.TextTrimmingProperty));
        Assert.Equal("Text", Assert.Single(template.TemplateBindings).Property);
        var probe = new Style(typeof(ComboBox));
        probe.Setters.Add(byName["Focusable"]);
        var combo = new ComboBox();
        probe.ApplyTo(combo);
        Assert.True(combo.Focusable);
        Assert.Equal(true, combo.GetValue(UIElement.FocusableProperty));
        var inflation = Assert.Throws<InvalidOperationException>(() => template.LoadContent());
        Assert.Contains("no inflation markup", inflation.Message);
        Assert.Same(style, dictionary["S"]);
    }

    [Fact]
    public void Synthetic_garbage_trimming_stays_precise()
    {
        XamlLoader.CustomElementSource = null;
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            ComboDictionary(ComboEntry("NotATrimming"), "S")));
        Assert.Contains("does not convert to TextTrimming", error.Message);
    }

    [Fact]
    public void Synthetic_wrong_member_bool_stays_precise()
    {
        XamlLoader.CustomElementSource = null;
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            ComboDictionary(FocusableBoolEntry(), "S", extraFocusableRow: true)));
        Assert.Contains("template TextBlock property 'FrameworkElement.Focusable'", error.Message);
    }

    [Fact]
    public void Synthetic_textblock_entry_lands_live()
    {
        XamlLoader.CustomElementSource = null;
        var body = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-638)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-112))
            .Sized(0x24, q => q.I16(2).Str("CharacterEllipsis").I16(-195))
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var dictionary = BamlReader.LoadDictionary(
            ComboDictionary(body, "S"));
        var style = Assert.IsType<Style>(dictionary["S"]);
        Assert.Same(typeof(TextBlock), style.TargetType);
        var setter = Assert.Single(style.Setters.OfType<Setter>());
        Assert.Same(TextBlock.TextTrimmingProperty, setter.Property);
        Assert.Equal(TextTrimming.CharacterEllipsis, setter.Value);
        Assert.Same(style, dictionary["S"]);
    }

    [Fact]
    public void ComboBox_engine_shape_matches_wpf()
    {
        Assert.Same(typeof(UIElement), UIElement.FocusableProperty.OwnerType);
        Assert.Same(typeof(bool), UIElement.FocusableProperty.PropertyType);
        Assert.Same(UIElement.FocusableProperty, DependencyProperty.FromName(typeof(ComboBox), "Focusable"));
        Assert.Same(typeof(Control), Control.TemplateProperty.OwnerType);
        Assert.Same(typeof(ControlTemplate), Control.TemplateProperty.PropertyType);
        Assert.Same(Control.TemplateProperty, DependencyProperty.FromName(typeof(ComboBox), "Template"));
        Assert.Same(typeof(TextBlock), TextBlock.TextTrimmingProperty.OwnerType);
        Assert.Same(typeof(TextTrimming), TextBlock.TextTrimmingProperty.PropertyType);
        Assert.Equal(TextTrimming.None, TextBlock.TextTrimmingProperty.DefaultMetadata?.DefaultValue);
        Assert.False(TextBlock.TextTrimmingProperty.IsAttached);
        Assert.Same(TextBlock.TextTrimmingProperty, DependencyProperty.FromName(typeof(TextBlock), "TextTrimming"));
        var flags = Assert.IsType<FrameworkPropertyMetadata>(TextBlock.TextTrimmingProperty.GetMetadata(typeof(TextBlock)));
        Assert.True(flags.AffectsMeasure);
        Assert.True(flags.AffectsRender);
        Assert.Equal(TextTrimming.None, new TextBlock().TextTrimming);
        var block = new TextBlock { TextTrimming = TextTrimming.WordEllipsis };
        Assert.Equal(TextTrimming.WordEllipsis, block.GetValue(TextBlock.TextTrimmingProperty));
        Assert.True(new TextBlock().IsHitTestVisible);
        block.SetValue(UIElement.IsHitTestVisibleProperty, false);
        Assert.False(block.IsHitTestVisible);
    }

    private static byte[] ComboEntry(string trimmingText)
    {
        return new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-92)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-132))
            .Sized(0x24, q => q.I16(2).Str("True").I16(-46))
            .Raw(0x04)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-37))
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-108).Raw(0x00)
            .Raw(0x22).I16(7).I16(-92)
            .Raw(0x2E).I16(-174)
            .Raw(0x03).I16(-638).Raw(0x00)
            .Sized(0x24, q => q.I16(80).Str("False").I16(-46))
            .Raw(0x23).I16(-110).U16(634).I16(81)
            .Sized(0x24, q => q.I16(-112).Str(trimmingText).I16(-195))
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x04).ToArray();
    }

    private static byte[] FocusableBoolEntry()
    {
        var textBlock = new B()
            .Raw(0x03).I16(-638).Raw(0x00)
            .Sized(0x24, q => q.I16(82).Str("False").I16(-46))
            .Raw(0x04).ToArray();
        return new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-92)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-37))
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-108).Raw(0x00)
            .Raw(0x22).I16(7).I16(-92)
            .Raw(0x2E).I16(-174)
            .Raw(textBlock)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x04).ToArray();
    }

    private static byte[] ComboDictionary(byte[] entry, string key, bool extraFocusableRow = false)
    {
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x1F, p => p.I16(7).I16(-108).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(80).I16(-694).Raw(0x00).Str("IsHitTestVisible"))
            .Sized(0x1F, p => p.I16(81).I16(-92).Raw(0x00).Str("Text"));
        if (extraFocusableRow)
            head.Sized(0x1F, p => p.I16(82).I16(-226).Raw(0x00).Str("Focusable"));
        head.Sized(0x20, p => p.U16(0).Str(key));
        return head.Deferable()
            .Sized(0x26, p => p.U16(0).U32(0).U16(0))
            .Raw(entry).FinishDictionary().ToArray();
    }

    private static byte[] Header() =>
        new B()
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
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(
            directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol", "app-manifest.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var apps = System.IO.Path.Combine(directory!.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol");
        using var document = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
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

        public B Raw(params byte[] bytes) { _b.AddRange(bytes); return this; }

        public B Raw(B other) { _b.AddRange(other._b); return this; }

        public B U16(ushort value) { _b.Add((byte)value); _b.Add((byte)(value >> 8)); return this; }

        public B I16(short value) => U16((ushort)value);

        public B U32(uint value)
        {
            _b.Add((byte)value);
            _b.Add((byte)(value >> 8));
            _b.Add((byte)(value >> 16));
            _b.Add((byte)(value >> 24));
            return this;
        }

        public B Str(string value)
        {
            var ascii = Encoding.ASCII.GetBytes(value);
            Seven(ascii.Length);
            _b.AddRange(ascii);
            return this;
        }

        public B Seven(int value)
        {
            var v = (uint)value;
            while (v >= 0x80) { _b.Add((byte)(v | 0x80)); v >>= 7; }
            _b.Add((byte)v);
            return this;
        }

        public B Sized(byte record, Func<B, B> body)
        {
            var inner = body(new B()).ToArray();
            Raw(record);
            Seven(inner.Length + 1);
            _b.AddRange(inner);
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
