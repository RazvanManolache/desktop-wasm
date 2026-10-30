// Copyright (c) 2026 The WPF on WASM Contributors.
// Licensed under the MIT License. See LICENSE in the project root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-151: the template complex ContentTemplate member (well-known
/// member id -21) routes as ContentPresenter.ContentTemplate, so the staged
/// 'CarouselItemBaseStyle' entry (converted CarouselItem target, Focusable
/// False, Template holding the Border/ContentPresenter chain whose
/// ContentPresenter carries the ItemTemplate Binding over the hc:Carousel
/// AncestorType FindAncestor RelativeSource at BAML+0x1A21B) lands as a live
/// engine Style; slice-152 routes the implicit GrowlWindow short-form
/// MaxWidth member (well-known id -51) at BAML+0x1A2CA through the new
/// FrameworkElement.MaxWidthProperty DP, so the staged boot stops precise
/// at the next entry: the keyed 'ListBoxItemCustom' style targeting
/// well-known type id -374 at BAML+0x1A368. Triangulated against the staged
/// 466350-byte theme.baml (sha256 D305CC52..., entry/key/string/attribute
/// spans pinned below), the pinned upstream HandyControl XAML at e9ad3d70
/// (CarouselItemBaseStyle block plus the Growl implicit style naming -51 as
/// MaxWidth), a genuine net10.0-windows fixture compiling the same
/// ContentTemplate/RelativeSource record shapes with framework-only types
/// (ContentTemplate complex EB FF, Binding ctor Path plus RelativeSource
/// complex with the AncestorType Property shape), and net10 reflection
/// (RelativeSource FindAncestor/null/1 defaults plus the DataTemplate-typed
/// ContentTemplate DP). Behind the new stop sit the keyed 'Growl' style,
/// the keyed 'CarouselPateButton' and implicit Carousel styles
/// (BasedOn/StaticResource and DynamicResource setters,
/// EventTriggers/Storyboard/TargetName), the GroupBox styles
/// (BasedOn/DynamicResource setters, TitleElement triggers), EventTriggers,
/// the line-info trailer, per-file (Type,Name) x:Name rows and converted-lib
/// elements (all still precise follow-ups). Package stays conversion-failed
/// (6 staged assemblies, gaps HandyControlDemo/HandyControlDemoCode), so no
/// browser proof is claimed.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice151BamlCarouselTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice151BamlCarouselTests()
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
    public void Theme_baml_places_carousel_and_growl_bytes()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        Assert.Equal(
            "D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0",
            Convert.ToHexString(SHA256.HashData(theme)));

        var carouselHead = new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x38, 0x00 };
        Assert.Equal(carouselHead, theme[0x1A1B4..0x1A1BD]);
        var failPrefix = new byte[] { 0x23, 0xCF, 0xFF, 0x7A, 0x02, 0xDD, 0xFF, 0x07, 0xEB, 0xFF };
        Assert.Equal(failPrefix, theme[0x1A214..0x1A21E]);
        var relativeSourceComplex = new byte[] { 0x07, 0x0A, 0x00, 0x03, 0xFC, 0xFD, 0x00 };
        Assert.Equal(relativeSourceComplex, theme[0x1A233..0x1A23A]);
        var ancestorType = new byte[] { 0x05, 0x0F, 0x52, 0x00, 0x0B }
            .Concat(Encoding.ASCII.GetBytes("hc:Carousel")).ToArray();
        Assert.Equal(ancestorType, theme[0x1A23A..0x1A24A]);
        var growlHead = new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x39, 0x00 };
        Assert.Equal(growlHead, theme[0x1A254..0x1A25D]);
        var growlFailSite = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xCD, 0xFF };
        Assert.Equal(growlFailSite, theme[0x1A2CA..0x1A2D2]);
        var keyCarousel = new byte[] { 0x26, 0x09, 0x9D, 0x00, 0x1D, 0xB0, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(keyCarousel, theme[0xB5AF..0xB5B9]);
        var keyGrowlWindow = new byte[] { 0x27, 0x39, 0x00, 0x00, 0xBD, 0xB0, 0x00, 0x00, 0x00 };
        Assert.Equal(keyGrowlWindow, theme[0xB5B9..0xB5C2]);
        var carouselTypeHead = new byte[] { 0x1D, 0x28, 0x38, 0x00, 0x00, 0x00, 0x22 };
        Assert.Equal(carouselTypeHead, theme[0x25AE..0x25B5]);
        var handyControlHead = Encoding.ASCII.GetBytes("HandyControl");
        Assert.Equal(handyControlHead, theme[0x25B5..0x25C1]);
        var ancestorTypeAttribute = new byte[] { 0x1F, 0x13, 0x52, 0x00, 0xFC, 0xFD, 0x00, 0x0C };
        Assert.Equal(ancestorTypeAttribute, theme[0x25D7..0x25DF]);
        var ancestorTypeName = new byte[] { 0x0C }.Concat(Encoding.ASCII.GetBytes("AncestorType")).ToArray();
        Assert.Equal(ancestorTypeName, theme[0x25DE..0x25EB]);
        var growlTypeHead = new byte[] { 0x1D, 0x27, 0x39, 0x00, 0x00, 0x00, 0x21 };
        Assert.Equal(growlTypeHead, theme[0x25EB..0x25F2]);
        Assert.Equal(handyControlHead, theme[0x25F2..0x25FE]);
        var listBoxItemCustomKey = new byte[] { 0x20, 0x15, 0x9E, 0x00, 0x11 };
        Assert.Equal(listBoxItemCustomKey, theme[0x2613..0x2618]);
    }

    [Fact]
    public void Staged_boot_advances_past_carousel_to_growl_stop()
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
        var border = Assert.IsType<Border>(template.VisualTree);
        var presenter = Assert.IsType<ContentPresenter>(border.Child);
        Assert.Contains((presenter, "Padding"), template.TemplateBindings);
        var binding = Assert.IsType<Binding>(BindingOperations.GetBinding(presenter, ContentPresenter.ContentTemplateProperty));
        Assert.Equal("ItemTemplate", binding.Path.Path);
        Assert.NotNull(binding.RelativeSource);
        Assert.Equal(RelativeSourceMode.FindAncestor, binding.RelativeSource.Mode);
        Assert.Equal("HandyControl.Controls.Carousel", binding.RelativeSource.AncestorType!.FullName);
        Assert.Same(style.TargetType.Assembly, binding.RelativeSource.AncestorType.Assembly);
        Assert.Same(style, dictionary["CarouselItemBaseStyle"]);
    }

    [Fact]
    public void Staged_growl_entry_lands_live()
    {
        // The staged implicit GrowlWindow entry (converted GrowlWindow target
        // with the two WindowAttach attached setters, the Width/MaxWidth
        // short-form LengthConverter setters, the Transparent Background
        // brush value and the three Window-qualified chrome setters) lands
        // as a live engine Style against the staged converted lib.
        LoadStagedHandyControlEntry();
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var growlWindow = handyControl.GetType("HandyControl.Controls.GrowlWindow");
        Assert.NotNull(growlWindow);
        var attach = handyControl.GetType("HandyControl.Controls.WindowAttach");
        Assert.NotNull(attach);
        var ignoreAltF4 = Assert.IsType<DependencyProperty>(attach!.GetField("IgnoreAltF4Property")!.GetValue(null));
        var showInTaskManager = Assert.IsType<DependencyProperty>(attach.GetField("ShowInTaskManagerProperty")!.GetValue(null));
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        var dictionary = BamlReader.LoadDictionary(
            GrowlDictionary(theme[0x1A254..0x1A364], "GrowlWindow"));
        var style = Assert.IsType<Style>(dictionary["GrowlWindow"]);
        Assert.Same(growlWindow, style.TargetType);
        Assert.NotSame(typeof(FrameworkElement).Assembly, growlWindow!.Assembly);
        Assert.Equal(8, style.Setters.Count);
        var ignore = Assert.IsType<Setter>(style.Setters[0]);
        Assert.Same(ignoreAltF4, ignore.Property);
        Assert.Equal(true, ignore.Value);
        var manager = Assert.IsType<Setter>(style.Setters[1]);
        Assert.Same(showInTaskManager, manager.Property);
        Assert.Equal(false, manager.Value);
        var width = Assert.IsType<Setter>(style.Setters[2]);
        Assert.Same(FrameworkElement.WidthProperty, width.Property);
        Assert.Equal(340.0, Assert.IsType<double>(width.Value));
        var maxWidth = Assert.IsType<Setter>(style.Setters[3]);
        Assert.Same(FrameworkElement.MaxWidthProperty, maxWidth.Property);
        Assert.Same(DependencyProperty.FromName(growlWindow, "MaxWidth"), maxWidth.Property);
        Assert.Equal(340.0, Assert.IsType<double>(maxWidth.Value));
        var background = Assert.IsType<Setter>(style.Setters[4]);
        Assert.Same(Control.BackgroundProperty, background.Property);
        Assert.Equal(Color.FromArgb(0, 255, 255, 255), Assert.IsType<SolidColorBrush>(background.Value).Color);
        var showActivated = Assert.IsType<Setter>(style.Setters[5]);
        Assert.Same(Window.ShowActivatedProperty, showActivated.Property);
        Assert.Equal(false, showActivated.Value);
        var showInTaskbar = Assert.IsType<Setter>(style.Setters[6]);
        Assert.Same(Window.ShowInTaskbarProperty, showInTaskbar.Property);
        Assert.Equal(false, showInTaskbar.Value);
        var topmost = Assert.IsType<Setter>(style.Setters[7]);
        Assert.Same(Window.TopmostProperty, topmost.Property);
        Assert.Equal(true, topmost.Value);
        Assert.Same(style, dictionary["GrowlWindow"]);
    }

    [Fact]
    public void Synthetic_carousel_contenttemplate_lands_live()
    {
        XamlLoader.CustomElementSource = null;
        var dictionary = BamlReader.LoadDictionary(
            ComboDictionary(CarouselAncestorEntry("Border"), "S"));
        var style = Assert.IsType<Style>(dictionary["S"]);
        Assert.Same(typeof(ComboBox), style.TargetType);
        var template = Assert.IsType<ControlTemplate>(style.Setters.OfType<Setter>().Single(s => s.Property.Name == "Template").Value);
        Assert.Same(typeof(ComboBox), template.TargetType);
        var border = Assert.IsType<Border>(template.VisualTree);
        var presenter = Assert.IsType<ContentPresenter>(border.Child);
        Assert.Empty(template.TemplateBindings);
        var binding = Assert.IsType<Binding>(BindingOperations.GetBinding(presenter, ContentPresenter.ContentTemplateProperty));
        Assert.Equal("ItemTemplate", binding.Path.Path);
        Assert.NotNull(binding.RelativeSource);
        Assert.Equal(RelativeSourceMode.FindAncestor, binding.RelativeSource.Mode);
        Assert.Same(typeof(Border), binding.RelativeSource.AncestorType);
        Assert.Equal(1, binding.RelativeSource.AncestorLevel);
        Assert.Same(style, dictionary["S"]);
    }

    [Fact]
    public void Synthetic_relativesource_without_ancestortype_lands_live()
    {
        XamlLoader.CustomElementSource = null;
        var dictionary = BamlReader.LoadDictionary(
            ComboDictionary(CarouselModeOnlyEntry(), "S"));
        var style = Assert.IsType<Style>(dictionary["S"]);
        var template = Assert.IsType<ControlTemplate>(style.Setters.OfType<Setter>().Single(s => s.Property.Name == "Template").Value);
        var presenter = Assert.IsType<ContentPresenter>(Assert.IsType<Border>(template.VisualTree).Child);
        var binding = Assert.IsType<Binding>(BindingOperations.GetBinding(presenter, ContentPresenter.ContentTemplateProperty));
        Assert.Equal("ItemTemplate", binding.Path.Path);
        Assert.NotNull(binding.RelativeSource);
        Assert.Equal(RelativeSourceMode.FindAncestor, binding.RelativeSource.Mode);
        Assert.Null(binding.RelativeSource.AncestorType);
        Assert.Same(style, dictionary["S"]);
    }

    [Fact]
    public void Synthetic_ancestorType_unknown_prefix_fails()
    {
        XamlLoader.CustomElementSource = null;
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            ComboDictionary(CarouselAncestorEntry("zz:Border"), "S")));
        Assert.Contains("names unknown xmlns prefix 'zz'", error.Message);
    }

    [Fact]
    public void Synthetic_ancestorType_unknown_engine_type_fails()
    {
        XamlLoader.CustomElementSource = null;
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            ComboDictionary(CarouselAncestorEntry("Carousel"), "S")));
        Assert.Contains("relative-source ancestor types take prefix:Type or an engine type", error.Message);
    }

    [Fact]
    public void Synthetic_ancestorType_wrong_attribute_fails()
    {
        XamlLoader.CustomElementSource = null;
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            ComboDictionary(CarouselWrongAttributeEntry(), "S", -556, "Value")));
        Assert.Contains("(relative sources take AncestorType; style-surface follow-up)", error.Message);
    }

    [Fact]
    public void Synthetic_ancestorType_duplicate_fails()
    {
        XamlLoader.CustomElementSource = null;
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            ComboDictionary(CarouselDuplicateAncestorEntry(), "S")));
        Assert.Contains("duplicate AncestorType on the template RelativeSource", error.Message);
    }

    [Fact]
    public void ContentPresenter_ContentTemplate_DependencyProperty_shape()
    {
        Assert.Same(typeof(DataTemplate), ContentPresenter.ContentTemplateProperty.PropertyType);
        Assert.Null(ContentPresenter.ContentTemplateProperty.DefaultMetadata?.DefaultValue);
        Assert.False(ContentPresenter.ContentTemplateProperty.ReadOnly);
        Assert.Same(ContentPresenter.ContentTemplateProperty, DependencyProperty.FromName(typeof(ContentPresenter), "ContentTemplate"));
        // The engine registers ContentTemplate directly on ContentPresenter;
        // desktop WPF registers the same DP on ContentControl and shares it
        // through ContentPresenter, with identical value semantics.
        var source = new RelativeSource(RelativeSourceMode.FindAncestor);
        Assert.Equal(RelativeSourceMode.FindAncestor, source.Mode);
        Assert.Null(source.AncestorType);
        Assert.Equal(1, source.AncestorLevel);
    }

    private static byte[] CarouselAncestorEntry(string ancestorText)
    {
        return new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-92)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-37))
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-108).Raw(0x00)
            .Raw(0x22).I16(3).I16(-92)
            .Raw(0x2E).I16(-174)
            .Raw(0x03).I16(-50).Raw(0x00)
            .Raw(0x2E).I16(-161)
            .Raw(0x03).I16(-102).Raw(0x00)
            .Raw(0x07).I16(-21)
            .Raw(0x03).I16(-20).Raw(0x00)
            .Raw(0x2A).Sized(0x10, q => q.Str("ItemTemplate")).Raw(0x2B)
            .Raw(0x07).I16(4)
            .Raw(0x03).I16(-516).Raw(0x00)
            .Sized(0x05, q => q.I16(5).Str(ancestorText))
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x08).Raw(0x04).Raw(0x04).ToArray();
    }

    private static byte[] CarouselModeOnlyEntry()
    {
        return new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-92)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-37))
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-108).Raw(0x00)
            .Raw(0x22).I16(3).I16(-92)
            .Raw(0x2E).I16(-174)
            .Raw(0x03).I16(-50).Raw(0x00)
            .Raw(0x2E).I16(-161)
            .Raw(0x03).I16(-102).Raw(0x00)
            .Raw(0x07).I16(-21)
            .Raw(0x03).I16(-20).Raw(0x00)
            .Raw(0x2A).Sized(0x10, q => q.Str("ItemTemplate")).Raw(0x2B)
            .Raw(0x07).I16(4)
            .Raw(0x03).I16(-516).Raw(0x00)
            .Raw(0x2A).Sized(0x10, q => q.Str("FindAncestor")).Raw(0x2B)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x08).Raw(0x04).Raw(0x04).ToArray();
    }

    private static byte[] CarouselWrongAttributeEntry()
    {
        return CarouselAncestorEntry("Border");
    }

    private static byte[] CarouselDuplicateAncestorEntry()
    {
        return new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-92)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-37))
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-108).Raw(0x00)
            .Raw(0x22).I16(3).I16(-92)
            .Raw(0x2E).I16(-174)
            .Raw(0x03).I16(-50).Raw(0x00)
            .Raw(0x2E).I16(-161)
            .Raw(0x03).I16(-102).Raw(0x00)
            .Raw(0x07).I16(-21)
            .Raw(0x03).I16(-20).Raw(0x00)
            .Raw(0x2A).Sized(0x10, q => q.Str("ItemTemplate")).Raw(0x2B)
            .Raw(0x07).I16(4)
            .Raw(0x03).I16(-516).Raw(0x00)
            .Sized(0x05, q => q.I16(5).Str("Border"))
            .Sized(0x05, q => q.I16(5).Str("Border"))
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04).Raw(0x04).Raw(0x04).Raw(0x08).Raw(0x04).Raw(0x04).ToArray();
    }

    private static byte[] ComboDictionary(byte[] entry, string key, short ancestorOwner = -516, string ancestorName = "AncestorType")
    {
        return new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x1F, p => p.I16(3).I16(-108).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(4).I16(-20).Raw(0x00).Str("RelativeSource"))
            .Sized(0x1F, p => p.I16(5).I16(ancestorOwner).Raw(0x00).Str(ancestorName))
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
            .Sized(0x1F, p => p.I16(10).I16(-20).Raw(0x00).Str("RelativeSource"))
            .Sized(0x1F, p => p.I16(82).I16(-516).Raw(0x00).Str("AncestorType"))
            .Sized(0x1C, p => p.U16(1).Str("HandyControl"))
            .Sized(0x1B, p => p.Str("clr-namespace:HandyControl.Controls").Str("HandyControl.Controls").U16(1))
            .Sized(0x14, p => p.Str("hc").Str("clr-namespace:HandyControl.Controls").U16(1).U16(1))
            .Sized(0x20, p => p.U16(0).Str(key));
            // TypeInfo rows land in id order like the real compiler output:
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

    private static byte[] GrowlDictionary(byte[] entry, string key)
    {
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x20, p => p.U16(0).Str(key));
        for (ushort i = 0; i < 58; i++)
        {
            var j = i;
            var name = j == 37 ? "HandyControl.Controls.WindowAttach" : j == 57 ? "HandyControl.Controls.GrowlWindow" : "Dummy" + j;
            head.Sized(0x1D, p => p.U16(j).U16(0).Str(name));
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
