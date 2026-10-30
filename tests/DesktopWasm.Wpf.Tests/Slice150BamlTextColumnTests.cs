// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-150 HandyControl theme.baml DataGridTextColumnStyle TextBlock style-target routing.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the slice-149 blocker (the keyed 'DataGridTextColumnStyle' Style entry at
/// BAML+0x1A182 targeting well-known type id -638 with no style-target
/// routing) routes as a live engine Style: -638 resolves to the engine
/// TextBlock (real routing, never ported), and the short-form TextTrimming
/// setter (-112) lands CharacterEllipsis through TextBlock.TextTrimmingProperty.
/// The run advances past the TextColumn entry and stops precise at the keyed
/// 'CarouselItemBaseStyle' Style entry at BAML+0x1A21B (template complex
/// ContentTemplate member, well-known id -21 with no template-member routing
/// - the next slice's work).
///
/// Byte-exact decode against the staged 466350-byte theme.baml: the
/// TextColumn entry [0x1A182, 0x1A1B4) (Style head 03 94 FD with TargetType
/// 22 00 00 82 FD over TextBlock -638, ContentProperty -238, Setter with the
/// short-form TextTrimming PropertyCustom 06 07 01 00 89 00 90 FF plus the
/// CharacterEllipsis/EnumConverter -195 value 24 17 02 00 11 ... 3D FF, and
/// the 04 04 close run), the CarouselItemBaseStyle head 03 94 FD 00 22 00 00
/// 38 00 (TargetType per-file TypeInfo 56 = HandyControl.Controls.CarouselItem)
/// at 0x1A1B4 with the Focusable False/BooleanConverter setter, the Template
/// complex holding the ControlTemplate/Border/ContentPresenter chain whose
/// 07 EB FF complex (ContentTemplate -21) at BAML+0x1A21B holds the ItemTemplate
/// Binding with the hc:Carousel AncestorType RelativeSource, key records 0x26
/// key ids 0x9C/0x9D at 0xB5A5/0xB5AF, and the key-string table rows 0x9C
/// 'DataGridTextColumnStyle' at 0x2578 plus 0x9D 'CarouselItemBaseStyle'
/// at 0x2594.
///
/// Triangulated three ways: the staged framing above (theme bytes live
/// verified at sha256 D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0);
/// the live upstream HandyControl DataGridBaseStyle.xaml TextColumn block at
/// pinned commit e9ad3d70 (Style x:Key DataGridTextColumnStyle TargetType
/// TextBlock with the single TextTrimming CharacterEllipsis setter, directly
/// followed by the DataGridEditingComboBoxColumnStyle block); a genuine
/// net10.0-windows ResourceDictionary fixture compiling the exact upstream
/// block (byte-identical in every staged record shape modulo per-file
/// numbering and line markers: TargetType 82 FD for TargetType TextBlock,
/// short-form TextTrimming member 90 FF with EnumConverter 3D FF) plus a
/// genuine net10.0-windows reflection probe (TextBlock.TextTrimmingProperty is
/// TextBlock-owned, TextTrimming typed, None default, read-write;
/// UIElement.FocusableProperty and Control.TemplateProperty carry the
/// Focusable/Template setters through the TextBlock lineage).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path); all 6 staged images
/// sha256-verified, manifest hand-untouched; staged-oracle status-gate
/// bypass stays in-probe only.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice150BamlTextColumnTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice150BamlTextColumnTests()
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
    public void Theme_baml_places_textcolumn_and_carousel_bytes()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        Assert.Equal(
            "D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0",
            Convert.ToHexString(SHA256.HashData(theme)));

        var textHead = new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x82, 0xFD };
        Assert.Equal(textHead, theme[0x1A182..0x1A18B]);
        var trimmingProperty = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0x90, 0xFF };
        Assert.Equal(trimmingProperty, theme[0x1A192..0x1A19A]);
        var trimmingText = Encoding.ASCII.GetBytes("CharacterEllipsis");
        Assert.Equal(trimmingText, theme[0x1A19F..0x1A1B0]);
        var trimmingConverter = new byte[] { 0x3D, 0xFF };
        Assert.Equal(trimmingConverter, theme[0x1A1B0..0x1A1B2]);
        var closeAndNextHead = new byte[] { 0x04, 0x04, 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x38, 0x00 };
        Assert.Equal(closeAndNextHead, theme[0x1A1B2..0x1A1BD]);
        var carouselFocusableHead = new byte[] { 0x03, 0xD4, 0xFD, 0x00, 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0x7C, 0xFF };
        Assert.Equal(carouselFocusableHead, theme[0x1A1C0..0x1A1CC]);
        var focusableFalse = new byte[] { 0x24, 0x0B, 0x02, 0x00, 0x05 }
            .Concat(Encoding.ASCII.GetBytes("False")).Concat(new byte[] { 0xD2, 0xFF }).ToArray();
        Assert.Equal(focusableFalse, theme[0x1A1CC..0x1A1D8]);
        var failPrefix = new byte[] { 0x23, 0xCF, 0xFF, 0x7A, 0x02, 0xDD, 0xFF, 0x07, 0xEB, 0xFF };
        Assert.Equal(failPrefix, theme[0x1A214..0x1A21E]);
        var keyTextColumn = new byte[] { 0x26, 0x09, 0x9C, 0x00, 0xEB, 0xAF, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(keyTextColumn, theme[0xB5A5..0xB5AF]);
        var keyCarousel = new byte[] { 0x26, 0x09, 0x9D, 0x00, 0x1D, 0xB0, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(keyCarousel, theme[0xB5AF..0xB5B9]);
        var textColumnStringHead = new byte[] { 0x20, 0x1B, 0x9C, 0x00, 0x17 }
            .Concat(Encoding.ASCII.GetBytes("DataGri")).ToArray();
        Assert.Equal(textColumnStringHead, theme[0x2578..0x2584]);
        var carouselStringHead = new byte[] { 0x20, 0x19, 0x9D, 0x00, 0x15 }
            .Concat(Encoding.ASCII.GetBytes("Carousel")).ToArray();
        Assert.Equal(carouselStringHead, theme[0x2594..0x25A1]);
    }

    [Fact]
    public void Staged_boot_advances_past_textcolumn_to_carousel_stop()
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
    public void Staged_textcolumn_entry_lands_live()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        var dictionary = BamlReader.LoadDictionary(
            TextDictionary(theme[0x1A182..0x1A1B4], "DataGridTextColumnStyle"));
        var style = Assert.IsType<Style>(dictionary["DataGridTextColumnStyle"]);
        Assert.Same(typeof(TextBlock), style.TargetType);
        var setter = Assert.Single(style.Setters.OfType<Setter>());
        Assert.Same(TextBlock.TextTrimmingProperty, setter.Property);
        Assert.Equal("TextTrimming", setter.Property.Name);
        Assert.Equal(TextTrimming.CharacterEllipsis, setter.Value);
        var probe = new Style(typeof(TextBlock));
        probe.Setters.Add(setter);
        var block = new TextBlock();
        probe.ApplyTo(block);
        Assert.Equal(TextTrimming.CharacterEllipsis, block.TextTrimming);
        Assert.Equal(TextTrimming.CharacterEllipsis, block.GetValue(TextBlock.TextTrimmingProperty));
        Assert.Same(style, dictionary["DataGridTextColumnStyle"]);
    }

    [Fact]
    public void Staged_carousel_entry_lands_live()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        var dictionary = BamlReader.LoadDictionary(
            CarouselDictionary(theme[0x1A1B4..0x1A254], "CarouselItemBaseStyle"));
        var style = Assert.IsType<Style>(dictionary["CarouselItemBaseStyle"]);
        Assert.Equal("HandyControl.Controls.CarouselItem", style.TargetType.FullName);
        Assert.NotSame(typeof(TextBlock).Assembly, style.TargetType.Assembly);
        var byName = style.Setters.OfType<Setter>().ToDictionary(s => s.Property.Name);
        Assert.Equal(2, byName.Count);
        Assert.Same(DependencyProperty.FromName(style.TargetType, "Focusable"), byName["Focusable"].Property);
        Assert.Equal(false, byName["Focusable"].Value);
        Assert.Same(DependencyProperty.FromName(style.TargetType, "Template"), byName["Template"].Property);
        var template = Assert.IsType<ControlTemplate>(byName["Template"].Value);
        Assert.Same(style.TargetType, template.TargetType);
        Assert.IsType<Border>(template.VisualTree);
        Assert.Same(style, dictionary["CarouselItemBaseStyle"]);
    }

    [Fact]
    public void Synthetic_textblock_entry_lands_live()
    {
        XamlLoader.CustomElementSource = null;
        var dictionary = BamlReader.LoadDictionary(TextDictionary(TextEntry("CharacterEllipsis"), "S"));
        var style = Assert.IsType<Style>(dictionary["S"]);
        Assert.Same(typeof(TextBlock), style.TargetType);
        var setter = Assert.Single(style.Setters.OfType<Setter>());
        Assert.Same(TextBlock.TextTrimmingProperty, setter.Property);
        Assert.Equal(TextTrimming.CharacterEllipsis, setter.Value);
        var probe = new Style(typeof(TextBlock));
        probe.Setters.Add(setter);
        var block = new TextBlock();
        probe.ApplyTo(block);
        Assert.Equal(TextTrimming.CharacterEllipsis, block.TextTrimming);
        Assert.Same(style, dictionary["S"]);
    }

    [Fact]
    public void Synthetic_garbage_trimming_stays_precise()
    {
        XamlLoader.CustomElementSource = null;
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            TextDictionary(TextEntry("NotATrimming"), "S")));
        Assert.Contains("is not a TextTrimming for setter 'TextTrimming' in style entry 'S'", error.Message);
    }

    [Fact]
    public void Synthetic_trimming_on_wrong_target_stays_precise()
    {
        XamlLoader.CustomElementSource = null;
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            TextDictionary(BorderEntry("CharacterEllipsis"), "S")));
        Assert.Contains("'TextTrimming' is not a dependency property of Border (setter in style entry 'S')", error.Message);
    }

    [Fact]
    public void Synthetic_carousel_complex_content_lands_live()
    {
        XamlLoader.CustomElementSource = null;
        var dictionary = BamlReader.LoadDictionary(
            TextDictionary(CarouselComplexEntry(), "S"));
        var style = Assert.IsType<Style>(dictionary["S"]);
        Assert.Same(typeof(ComboBox), style.TargetType);
        var template = Assert.IsType<ControlTemplate>(style.Setters.OfType<Setter>().Single(s => s.Property.Name == "Template").Value);
        Assert.Same(typeof(ComboBox), template.TargetType);
        var presenter = Assert.IsType<ContentPresenter>(Assert.IsType<Border>(template.VisualTree).Child);
        var binding = Assert.IsType<Binding>(BindingOperations.GetBinding(presenter, ContentPresenter.ContentTemplateProperty));
        Assert.Equal(string.Empty, binding.Path.Path);
        Assert.Null(binding.RelativeSource);
        Assert.Same(style, dictionary["S"]);
    }

    [Fact]
    public void Synthetic_unknown_style_target_stays_precise()
    {
        XamlLoader.CustomElementSource = null;
        var body = new B().Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-777)
            .Raw(0x2E).I16(-238)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            TextDictionary(body, "S")));
        Assert.Contains("style targets beyond Path/Border/ContentControl/GridSplitter/Rectangle/RepeatButton/GroupBox/Thumb/ToolBarTray/ComboBox/TextBlock/ListBoxItem are a style-surface follow-up", error.Message);
    }

    [Fact]
    public void TextBlock_engine_shape_matches_wpf()
    {
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
        Assert.Null(DependencyProperty.FromName(typeof(Border), "TextTrimming"));
        Assert.Null(DependencyProperty.FromName(typeof(TextBlock), "Template"));
        Assert.Same(UIElement.FocusableProperty, DependencyProperty.FromName(typeof(TextBlock), "Focusable"));
        Assert.Same(Control.TemplateProperty, DependencyProperty.FromName(typeof(ComboBox), "Template"));
    }
    private static byte[] TextEntry(string trimmingText)
    {
        return new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-638)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-112))
            .Sized(0x24, q => q.I16(2).Str(trimmingText).I16(-195))
            .Raw(0x04)
            .Raw(0x04).ToArray();
    }

    private static byte[] BorderEntry(string trimmingText)
    {
        return new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-50)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-112))
            .Sized(0x24, q => q.I16(2).Str(trimmingText).I16(-195))
            .Raw(0x04)
            .Raw(0x04).ToArray();
    }

    private static byte[] CarouselComplexEntry()
    {
        // The style target only needs Template: ComboBox carries it
        // (TextBlock has none, like real WPF), so the failure below pins
        // the complex ContentTemplate member, not target resolution.
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
            .Raw(0x03).I16(-50).Raw(0x00)
            .Raw(0x2E).I16(-161)
            .Raw(0x03).I16(-102).Raw(0x00)
            .Raw(0x07).I16(-21)
            .Raw(0x03).I16(-20).Raw(0x00)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x04).ToArray();
    }

    private static byte[] TextDictionary(byte[] entry, string key)
    {
        return new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x1F, p => p.I16(7).I16(-108).Raw(0x00).Str("TargetType"))
            .Sized(0x20, p => p.U16(0).Str(key))
            .Deferable()
            .Sized(0x26, p => p.U16(0).U32(0).U16(0))
            .Raw(entry).FinishDictionary().ToArray();
    }

    private static byte[] CarouselDictionary(byte[] entry, string key)
    {
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x1F, p => p.I16(7).I16(-108).Raw(0x00).Str("TargetType"))
            // TypeInfo rows land in id order like the real compiler output:
            .Sized(0x1F, p => p.I16(10).I16(-20).Raw(0x00).Str("RelativeSource"))
            .Sized(0x1F, p => p.I16(82).I16(-516).Raw(0x00).Str("AncestorType"))
            .Sized(0x1C, p => p.U16(1).Str("HandyControl"))
            .Sized(0x1B, p => p.Str("clr-namespace:HandyControl.Controls").Str("HandyControl.Controls").U16(1))
            .Sized(0x14, p => p.Str("hc").Str("clr-namespace:HandyControl.Controls").U16(1).U16(1))
            .Sized(0x20, p => p.U16(0).Str(key));
            // the staged 'CarouselItemBaseStyle' entry names TypeInfo 56.
            head.Sized(0x1D, p => p.U16(56).U16(0).Str("HandyControl.Controls.CarouselItem"));
        for (ushort i = 0; i < 56; i++)
        {
            var j = i;
            head.Sized(0x1D, p => p.U16(j).U16(0).Str("Dummy" + j));
        }
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
