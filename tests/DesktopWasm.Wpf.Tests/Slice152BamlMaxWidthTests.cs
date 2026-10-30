// Copyright (c) 2026 The WPF on WASM Contributors.
// Licensed under the MIT License. See LICENSE in the project root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-152: the short-form MaxWidth member (well-known member id
/// -51) routes through the new FrameworkElement.MaxWidthProperty DP, so the
/// staged implicit GrowlWindow entry (converted GrowlWindow target with the
/// WindowAttach IgnoreAltF4/ShowInTaskManager setters, the Width/MaxWidth
/// 340 LengthConverter setters, the Transparent Background brush value and
/// the Window ShowActivated/ShowInTaskbar/Topmost setters) lands as a live
/// engine Style, and the staged boot stops precise at the next entry: the
/// keyed 'ListBoxItemCustom' style targeting well-known type id -374
/// (ListBoxItem) at BAML+0x1A368 (slice-153 routes ListBoxItem plus the
/// BorderBrush/BorderThickness short-forms, the NullExtension
/// FocusVisualStyle and the AncestorType ItemsControl binding, so the entry
/// lands live and boot advances to the implicit RibbonTab stop).
/// Triangulated against the staged
/// 466350-byte theme.baml (sha256 D305CC52..., entry/key/string/attribute
/// spans pinned below: the GrowlWindow entry [0x1A254, 0x1A364) plus the
/// ListBoxItemCustom head, implicit/type keys and string rows), the pinned
/// upstream HandyControl Growl.xaml at e9ad3d70 (the implicit GrowlWindow
/// block naming Width/MaxWidth 340 plus Background/ShowActivated/
/// ShowInTaskbar/Topmost, fetched live), a genuine net10.0-windows fixture
/// compiling the same Width/MaxWidth shapes on the framework Window
/// (MaxWidth.baml, byte-identical short-form CD FF plus A4 FE value shapes;
/// fixture retained at E:/tmp/bamlfix/MaxWidth.xaml outside the tree) plus a
/// genuine net10.0-windows reflection probe (FrameworkElement.MaxWidthProperty
/// FrameworkElement-owned, double-typed, PositiveInfinity default, read-write,
/// plain PropertyMetadata). Behind the new stop (implicit RibbonTab, then the keyed 'Growl' style,
/// the CarouselPateButton/implicit Carousel styles, the GroupBox styles,
/// EventTriggers, the line-info trailer, per-file (Type,Name) x:Name rows and
/// converted-lib elements (all still precise follow-ups). Package stays
/// conversion-failed (6 staged, 2 gaps), so no browser proof is claimed.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice152BamlMaxWidthTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice152BamlMaxWidthTests()
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
    public void Theme_baml_places_growl_and_listbox_bytes()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        Assert.Equal(
            "D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0",
            Convert.ToHexString(SHA256.HashData(theme)));

        var growlHead = new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x39, 0x00 };
        Assert.Equal(growlHead, theme[0x1A254..0x1A25D]);
        var widthShort = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xC7, 0xFF };
        Assert.Equal(widthShort, theme[0x1A2B3..0x1A2BB]);
        var widthValue = new byte[] { 0x24, 0x09, 0x02, 0x00, 0x03, 0x33, 0x34, 0x30, 0xA4, 0xFE };
        Assert.Equal(widthValue, theme[0x1A2BB..0x1A2C5]);
        var maxWidthShort = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xCD, 0xFF };
        Assert.Equal(maxWidthShort, theme[0x1A2CA..0x1A2D2]);
        var maxWidthValue = new byte[] { 0x24, 0x09, 0x02, 0x00, 0x03, 0x33, 0x34, 0x30, 0xA4, 0xFE };
        Assert.Equal(maxWidthValue, theme[0x1A2D2..0x1A2DC]);
        var backgroundShort = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xE8, 0xFF };
        Assert.Equal(backgroundShort, theme[0x1A2E1..0x1A2E9]);
        var backgroundBrush = new byte[] { 0x06, 0x0A, 0x02, 0x00, 0xE8, 0x02, 0x01, 0xFF, 0xFF, 0xFF, 0x00 };
        Assert.Equal(backgroundBrush, theme[0x1A2E9..0x1A2F4]);
        var showActivatedHead = new byte[] { 0x03, 0xD4, 0xFD, 0x00, 0x06, 0x15, 0x01, 0x00, 0x89, 0x40, 0x1D, 0xFD, 0x0D };
        Assert.Equal(showActivatedHead, theme[0x1A2F5..0x1A302]);
        var showActivatedName = Encoding.ASCII.GetBytes("ShowActivated");
        Assert.Equal(showActivatedName, theme[0x1A302..0x1A30F]);
        var topmostTail = new byte[] { 0x24, 0x0A, 0x02, 0x00, 0x04, 0x54, 0x72, 0x75, 0x65, 0xD2, 0xFF, 0x04, 0x04 };
        Assert.Equal(topmostTail, theme[0x1A357..0x1A364]);
        var listBoxHead = new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x8A, 0xFE, 0x2E };
        Assert.Equal(listBoxHead, theme[0x1A364..0x1A36E]);
        var growlImplicitKey = new byte[] { 0x27, 0x39, 0x00, 0x00, 0xBD, 0xB0, 0x00, 0x00, 0x00 };
        Assert.Equal(growlImplicitKey, theme[0xB5B9..0xB5C2]);
        var listBoxKey = new byte[] { 0x26, 0x09, 0x9E, 0x00, 0xCD, 0xB1, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(listBoxKey, theme[0xB5C3..0xB5CD]);
        var listBoxKeyString = new byte[] { 0x20, 0x15, 0x9E, 0x00, 0x11 };
        Assert.Equal(listBoxKeyString, theme[0x2613..0x2618]);
        Assert.Equal(Encoding.ASCII.GetBytes("ListBoxItemCustom"), theme[0x2618..0x2629]);
        var growlTypeHead = new byte[] { 0x1D, 0x27, 0x39, 0x00, 0x00, 0x00, 0x21 };
        Assert.Equal(growlTypeHead, theme[0x25EB..0x25F2]);
        var handyControlHead = Encoding.ASCII.GetBytes("HandyControl");
        Assert.Equal(handyControlHead, theme[0x25F2..0x25FE]);
    }

    [Fact]
    public void Staged_boot_advances_past_growl_to_listbox_stop()
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
    public void Staged_growl_entry_lands_live()
    {
        // The staged implicit GrowlWindow entry [0x1A254, 0x1A364) lands as a
        // live engine Style: the converted target plus all eight setters.
        LoadStagedHandyControlEntry();
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var growlWindow = handyControl.GetType("HandyControl.Controls.GrowlWindow");
        Assert.NotNull(growlWindow);
        var attach = handyControl.GetType("HandyControl.Controls.WindowAttach");
        Assert.NotNull(attach);
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        var dictionary = BamlReader.LoadDictionary(
            GrowlDictionary(theme[0x1A254..0x1A364], "GrowlWindow"));
        var style = Assert.IsType<Style>(dictionary["GrowlWindow"]);
        Assert.Same(growlWindow, style.TargetType);
        Assert.NotSame(typeof(FrameworkElement).Assembly, growlWindow!.Assembly);
        var byName = style.Setters.OfType<Setter>().ToDictionary(s => s.Property.Name);
        Assert.Equal(8, byName.Count);
        Assert.Same(DependencyProperty.FromName(attach!, "IgnoreAltF4"), byName["IgnoreAltF4"].Property);
        Assert.Equal(true, byName["IgnoreAltF4"].Value);
        Assert.Same(DependencyProperty.FromName(attach!, "ShowInTaskManager"), byName["ShowInTaskManager"].Property);
        Assert.Equal(false, byName["ShowInTaskManager"].Value);
        Assert.Same(FrameworkElement.WidthProperty, byName["Width"].Property);
        Assert.Equal(340.0, Assert.IsType<double>(byName["Width"].Value));
        Assert.Same(FrameworkElement.MaxWidthProperty, byName["MaxWidth"].Property);
        Assert.Same(DependencyProperty.FromName(growlWindow, "MaxWidth"), byName["MaxWidth"].Property);
        Assert.Equal(340.0, Assert.IsType<double>(byName["MaxWidth"].Value));
        Assert.Same(Control.BackgroundProperty, byName["Background"].Property);
        Assert.Equal(Color.FromArgb(0, 255, 255, 255), Assert.IsType<SolidColorBrush>(byName["Background"].Value).Color);
        Assert.Same(Window.ShowActivatedProperty, byName["ShowActivated"].Property);
        Assert.Equal(false, byName["ShowActivated"].Value);
        Assert.Same(Window.ShowInTaskbarProperty, byName["ShowInTaskbar"].Property);
        Assert.Equal(false, byName["ShowInTaskbar"].Value);
        Assert.Same(Window.TopmostProperty, byName["Topmost"].Property);
        Assert.Equal(true, byName["Topmost"].Value);
        Assert.Same(style, dictionary["GrowlWindow"]);
    }

    [Fact]
    public void Staged_listbox_entry_lands_live()
    {
        // Slice-153: the former stop (keyed 'ListBoxItemCustom' at
        // BAML+0x1A368) lands as a live engine Style now that the
        // ListBoxItem target routes through the Control hierarchy.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(
            new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x8A, 0xFE, 0x2E },
            theme[0x1A364..0x1A36E]);
        var dictionary = BamlReader.LoadDictionary(
            GrowlDictionary(theme[0x1A364..0x1A4BC], "ListBoxItemCustom"));
        var style = Assert.IsType<Style>(dictionary["ListBoxItemCustom"]);
        Assert.Same(typeof(ListBoxItem), style.TargetType);
        var byName = style.Setters.OfType<Setter>().ToDictionary(s => s.Property.Name);
        Assert.Equal(9, byName.Count);
        Assert.Same(UIElement.SnapsToDevicePixelsProperty, byName["SnapsToDevicePixels"].Property);
        Assert.Equal(true, byName["SnapsToDevicePixels"].Value);
        Assert.Same(FrameworkElement.FocusVisualStyleProperty, byName["FocusVisualStyle"].Property);
        Assert.Null(byName["FocusVisualStyle"].Value);
        Assert.Same(Control.PaddingProperty, byName["Padding"].Property);
        Assert.Equal(new Thickness(0), Assert.IsType<Thickness>(byName["Padding"].Value));
        Assert.Same(Control.HorizontalContentAlignmentProperty, byName["HorizontalContentAlignment"].Property);
        var alignment = Assert.IsType<Binding>(byName["HorizontalContentAlignment"].Value);
        Assert.Equal("HorizontalContentAlignment", alignment.Path.Path);
        Assert.NotNull(alignment.RelativeSource);
        Assert.Equal(RelativeSourceMode.FindAncestor, alignment.RelativeSource.Mode);
        Assert.Same(typeof(ItemsControl), alignment.RelativeSource.AncestorType);
        Assert.Same(Control.VerticalContentAlignmentProperty, byName["VerticalContentAlignment"].Property);
        Assert.Equal(VerticalAlignment.Top, byName["VerticalContentAlignment"].Value);
        Assert.Same(Control.BackgroundProperty, byName["Background"].Property);
        Assert.Equal(Color.FromArgb(0, 255, 255, 255), Assert.IsType<SolidColorBrush>(byName["Background"].Value).Color);
        Assert.Same(Control.BorderBrushProperty, byName["BorderBrush"].Property);
        Assert.Equal(Color.FromArgb(0, 255, 255, 255), Assert.IsType<SolidColorBrush>(byName["BorderBrush"].Value).Color);
        Assert.Same(Control.BorderThicknessProperty, byName["BorderThickness"].Property);
        Assert.Equal(new Thickness(0), Assert.IsType<Thickness>(byName["BorderThickness"].Value));
        var template = Assert.IsType<ControlTemplate>(byName["Template"].Value);
        Assert.Same(typeof(ListBoxItem), template.TargetType);
        var presenter = Assert.IsType<ContentPresenter>(template.VisualTree);
        Assert.Equal(3, template.TemplateBindings.Count);
        Assert.Contains((presenter, "HorizontalContentAlignment"), template.TemplateBindings);
        Assert.Contains((presenter, "SnapsToDevicePixels"), template.TemplateBindings);
        Assert.Contains((presenter, "VerticalContentAlignment"), template.TemplateBindings);
        Assert.Same(style, dictionary["ListBoxItemCustom"]);
    }

    [Fact]
    public void Synthetic_maxwidth_lands_on_border()
    {
        // A short-form MaxWidth setter on an engine target lands through
        // FrameworkElement.MaxWidthProperty and applies visibly.
        XamlLoader.CustomElementSource = null;
        var dictionary = BamlReader.LoadDictionary(
            MaxWidthDictionary(MaxWidthEntry(-50, "340"), "S"));
        var style = Assert.IsType<Style>(dictionary["S"]);
        Assert.Same(typeof(Border), style.TargetType);
        var setter = Assert.Single(style.Setters.OfType<Setter>());
        Assert.Same(FrameworkElement.MaxWidthProperty, setter.Property);
        Assert.Equal("MaxWidth", setter.Property.Name);
        Assert.Equal(340.0, Assert.IsType<double>(setter.Value));
        var probe = new Style(typeof(Border));
        probe.Setters.Add(setter);
        var border = new Border();
        probe.ApplyTo(border);
        Assert.Equal(340.0, border.MaxWidth);
        Assert.Equal(340.0, border.GetValue(FrameworkElement.MaxWidthProperty));
        Assert.Same(style, dictionary["S"]);
    }

    [Fact]
    public void Synthetic_maxwidth_walks_the_hierarchy()
    {
        // MaxWidth resolves through the TargetType hierarchy like WPF: a
        // TextBlock target lands the same FrameworkElement DP.
        XamlLoader.CustomElementSource = null;
        var dictionary = BamlReader.LoadDictionary(
            MaxWidthDictionary(MaxWidthEntry(-638, "200"), "S"));
        var style = Assert.IsType<Style>(dictionary["S"]);
        Assert.Same(typeof(TextBlock), style.TargetType);
        var setter = Assert.Single(style.Setters.OfType<Setter>());
        Assert.Same(FrameworkElement.MaxWidthProperty, setter.Property);
        Assert.Equal(200.0, Assert.IsType<double>(setter.Value));
        var probe = new Style(typeof(TextBlock));
        probe.Setters.Add(setter);
        var block = new TextBlock();
        probe.ApplyTo(block);
        Assert.Equal(200.0, block.MaxWidth);
        Assert.Same(style, dictionary["S"]);
    }

    [Fact]
    public void Synthetic_garbage_short_member_stays_precise()
    {
        XamlLoader.CustomElementSource = null;
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            MaxWidthDictionary(MaxWidthEntry(-50, "340", memberId: -777), "S")));
        Assert.Contains("well-known member id -777", error.Message);
        Assert.Contains("short-form properties beyond FlowDirection/Template/", error.Message);
    }

    [Fact]
    public void Synthetic_maxwidth_bad_text_stays_precise()
    {
        XamlLoader.CustomElementSource = null;
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            MaxWidthDictionary(MaxWidthEntry(-50, "abc"), "S")));
        Assert.Contains("'abc' is not a Double for setter 'MaxWidth' in style entry 'S'", error.Message);
    }

    [Fact]
    public void FrameworkElement_MaxWidth_engine_shape_matches_wpf()
    {
        // The new DP mirrors the genuine net10 probe: FrameworkElement-owned,
        // double-typed, PositiveInfinity default, read-write, plain
        // PropertyMetadata (no AffectsMeasure in metadata; the CLR keeps the
        // engine InvalidateMeasure like before).
        Assert.Same(typeof(FrameworkElement), FrameworkElement.MaxWidthProperty.OwnerType);
        Assert.Same(typeof(double), FrameworkElement.MaxWidthProperty.PropertyType);
        Assert.Equal(double.PositiveInfinity, FrameworkElement.MaxWidthProperty.DefaultMetadata?.DefaultValue);
        Assert.False(FrameworkElement.MaxWidthProperty.ReadOnly);
        Assert.False(FrameworkElement.MaxWidthProperty.IsAttached);
        Assert.Equal(typeof(PropertyMetadata), FrameworkElement.MaxWidthProperty.DefaultMetadata?.GetType());
        Assert.Same(FrameworkElement.MaxWidthProperty, DependencyProperty.FromName(typeof(FrameworkElement), "MaxWidth"));
        Assert.Same(FrameworkElement.MaxWidthProperty, DependencyProperty.FromName(typeof(Border), "MaxWidth"));
        Assert.Null(DependencyProperty.FromName(typeof(FrameworkElement), "MaxHeight"));
        Assert.Equal(double.PositiveInfinity, new Border().MaxWidth);
        var border = new Border { MaxWidth = 340 };
        Assert.Equal(340.0, border.GetValue(FrameworkElement.MaxWidthProperty));
        Assert.Equal(340.0, border.MaxWidth);
        Assert.Equal(0.0, new Border().MinWidth);
    }

    private static byte[] MaxWidthEntry(short targetId, string text, short memberId = -51, short converter = -348)
    {
        return new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(targetId)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(memberId))
            .Sized(0x24, q => q.I16(2).Str(text).I16(converter))
            .Raw(0x04)
            .Raw(0x04).ToArray();
    }

    private static byte[] MaxWidthDictionary(byte[] entry, string key)
    {
        return new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x20, p => p.U16(0).Str(key))
            .Deferable()
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
            .Sized(0x1F, p => p.I16(7).I16(-108).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(10).I16(-20).Raw(0x00).Str("RelativeSource"))
            .Sized(0x1F, p => p.I16(3).I16(-20).Raw(0x00).Str("RelativeSource"))
            .Sized(0x1F, p => p.I16(4).I16(-516).Raw(0x00).Str("AncestorType"))
            .Sized(0x1F, p => p.I16(32).I16(-694).Raw(0x00).Str("SnapsToDevicePixels"))
            .Sized(0x1F, p => p.I16(82).I16(-516).Raw(0x00).Str("AncestorType"))
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
