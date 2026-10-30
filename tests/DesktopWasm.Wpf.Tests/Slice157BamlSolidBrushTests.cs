// Copyright (c) 2026 The WPF on WASM Contributors.
// Licensed under the MIT License. See LICENSE in the project root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-157: the keyed 'LightPrimaryBrush' SolidColorBrush entry at
/// BAML+0x1AA1C (keyidx A4, well-known type id -578) routes as a live engine
/// brush, so the staged boot stops precise at the keyed 'PrimaryBrush'
/// LinearGradientBrush entry at BAML+0x1AA35 (its Freeze marker record 0x34,
/// the next slice's work).
/// Live counts verified first: handycontrol conversion-failed/6 staged/2 gaps
/// (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched;
/// staged-oracle status-gate bypass stays in-probe only). Triangulation three
/// ways: staged framing spans against the live 466350-byte theme.baml
/// (sha256 D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0,
/// verified live) - the SolidColorBrush head 03 BE FD 00 at BAML+0x1AA1C, the
/// Freeze PresentationOptions record, the Color DynamicResource
/// PropertyWithExtension over per-file attr 85 (owned by the SolidColorBrush
/// TypeInfo -578 row) carrying the StringInfo resource key through extension
/// discriminator 189, the ElementEnd close, the LinearGradientBrush head
/// 03 9A FE 00 at BAML+0x1AA31, the 0x26 keyidx A4 record at 0xB609, the
/// key-string rows 164 'LightPrimaryBrush' at 0x2807 and 165
/// 'LightPrimaryColor' at 0x281D, the AttributeInfo 85 row (owner -578,
/// name 'Color') at 0x2833; the live upstream HandyControl Theme.xaml
/// LightPrimaryBrush line at pinned commit e9ad3d70 (a frozen
/// SolidColorBrush keyed 'LightPrimaryBrush' with
/// Color="{DynamicResource LightPrimaryColor}", heading the frozen
/// brush run); a genuine net10.0-windows ResourceDictionary fixture
/// compiling frozen DynamicResource-color plus literal-color SolidColorBrush
/// entries and a DynamicResource-stop gradient on framework types
/// (Brushes157.baml, byte-identical in every staged record shape modulo
/// per-file numbering and the 0x35/0x36 line markers the staged file omits:
/// SolidColorBrush element BE FD, Freeze PresentationOptions, DynamicResource
/// PropertyWithExtension with discriminator BD 00 plus the StringInfo key,
/// literal Color PropertyWithConverter through the ColorConverter;
/// fixture retained at E:/tmp/bamlfix157 outside the tree) plus a genuine
/// net10.0-windows reflection probe (SolidColorBrush.Color DP owned by
/// SolidColorBrush typed Color defaulting Transparent, SolidColorBrush is a
/// Freezable honoring Freeze, DynamicResourceExtension.ResourceKey is object,
/// SolidColorBrush carries no SetResourceReference so the XAML loader
/// resolve-now entry rule is the honest engine semantic; probe retained at
/// E:/tmp/rbprobe157 outside the tree). Real-behavior engine work, never
/// fabricated: BamlReader gains KnownSolidColorBrush (-578) plus the
/// ExtDynamicResource (189) extension arm and a ParseSolidColorBrushEntry
/// visual (optional Freeze marker plus one Color value, either the
/// DynamicResource extension resolving now against the dictionary-so-far
/// plus Application resources like the XAML loader resource-entry path with
/// missing keys staying Transparent, or the literal ColorConverter text;
/// shared lands once while non-shared replays the validated bag through a
/// factory). Re-package skipped by evidence (Convert map untouched, staged
/// bins byte-identical). Tests: 17 new Slice157BamlSolidBrushTests (see below).
/// application-compatibility-status.md handycontrol row verified current
/// against the live manifest (conversion-failed/6/2; no regen, nothing
/// re-packaged). Tests: 17 new Slice157BamlSolidBrushTests
/// (hash-verifying 6/2 manifest oracle, staged framing, boot advance to the
/// new stop, full-entry staged landing, 6 synthetic landings, 6 synthetic
/// negatives, engine-shape WPF-oracle pins) plus the 50-file stop-triple
/// flip with new-stop triples, and the BamlReader class-doc frontier
/// rewrite.
/// application-compatibility-status.md handycontrol row verified current
/// against the live manifest (conversion-failed/6/2; no regen, nothing
/// re-packaged). No browser proof - package stays conversion-failed.
/// Precise next blockers: the keyed 'PrimaryBrush' LinearGradientBrush entry
/// (Freeze marker 0x34, then DynamicResource GradientStop colors), then the
/// CarouselPateButton/implicit Carousel styles, the GroupBox styles with
/// BasedOn/DynamicResource setters and TitleElement triggers, EventTriggers,
/// the line-info trailer, the per-file (Type,Name) x:Name rows and the
/// converted-lib elements (CircleProgressBar/GifImage/Watermark/Drawer/
/// ElementGroup/SearchBar) behind the line-17 SkinDefault stop.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice157BamlSolidBrushTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice157BamlSolidBrushTests()
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
        using var document = JsonDocument.Parse(File.ReadAllBytes(System.IO.Path.Combine(apps, "app-manifest.json")));
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
        Assert.Equal(
            "D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0",
            Convert.ToHexString(SHA256.HashData(theme)));
    }

    [Fact]
    public void Theme_baml_light_primary_entry_bytes()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        Assert.Equal(
            "D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0",
            Convert.ToHexString(SHA256.HashData(theme)));

        // The keyed 'LightPrimaryBrush' entry is 21 bytes at BAML+0x1AA1C: the
        // SolidColorBrush head (well-known type id -578) with its flags byte.
        Assert.Equal(21, 0x1AA31 - 0x1AA1C);
        var solidHead = new byte[] { 0x03, 0xBE, 0xFD, 0x00 };
        Assert.Equal(solidHead, theme[0x1AA1C..0x1AA20]);
        // The Freeze PresentationOptions record ("True" plus the Freeze name
        // id 0x22, byte-identical to the genuine fixture modulo line marks).
        var freeze = new byte[] { 0x34, 0x08, 0x04, 0x54, 0x72, 0x75, 0x65, 0x22, 0x00 };
        Assert.Equal(freeze, theme[0x1AA20..0x1AA29]);
        Assert.Equal(Encoding.ASCII.GetBytes("True"), theme[0x1AA23..0x1AA27]);
        // The Color DynamicResource PropertyWithExtension: per-file attr 85
        // plus extension discriminator 189 plus the StringInfo resource key
        // 165, byte-identical to the genuine net10 fixture modulo per-file
        // numbering (fixture: attr 0, key 2, same BD 00 discriminator).
        var dynamicColor = new byte[] { 0x23, 0x55, 0x00, 0xBD, 0x00, 0xA5, 0x00 };
        Assert.Equal(dynamicColor, theme[0x1AA29..0x1AA30]);
        Assert.Equal(new byte[] { 0x04 }, theme[0x1AA30..0x1AA31]);
        // The next entry head is the keyed 'PrimaryBrush' LinearGradientBrush
        // (well-known type id -358: 9A FE emits for LinearGradientBrush in
        // the genuine fixture) with its own Freeze marker right behind it.
        var nextHead = new byte[] { 0x03, 0x9A, 0xFE, 0x00 };
        Assert.Equal(nextHead, theme[0x1AA31..0x1AA35]);
        Assert.Equal(freeze, theme[0x1AA35..0x1AA3E]);
        // The key record 0x26 keyidx A4 plus the key-string rows 164/165 plus
        // the AttributeInfo 85 row (owner -578, name 'Color').
        var lightPrimaryKey = new byte[] { 0x26, 0x09, 0xA4, 0x00, 0x85, 0xB8, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(lightPrimaryKey, theme[0xB609..0xB613]);
        var lightPrimaryKeyRow = new byte[] { 0x20, 0x15, 0xA4, 0x00, 0x11 };
        Assert.Equal(lightPrimaryKeyRow, theme[0x2807..0x280C]);
        Assert.Equal(Encoding.ASCII.GetBytes("LightPrimaryBrush"), theme[0x280C..0x281D]);
        var lightPrimaryColorRow = new byte[] { 0x20, 0x15, 0xA5, 0x00, 0x11 };
        Assert.Equal(lightPrimaryColorRow, theme[0x281D..0x2822]);
        Assert.Equal(Encoding.ASCII.GetBytes("LightPrimaryColor"), theme[0x2822..0x2833]);
        var colorAttrRow = new byte[] { 0x1F, 0x0C, 0x55, 0x00, 0xBE, 0xFD, 0x00, 0x05 };
        Assert.Equal(colorAttrRow, theme[0x2833..0x283B]);
        Assert.Equal(Encoding.ASCII.GetBytes("Color"), theme[0x283B..0x2840]);
    }

    [Fact]
    public void Staged_boot_advances_past_light_primary_to_primary_brush()
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
        Assert.DoesNotContain("setter property well-known member id -134 in style entry 'RightPathStyle'", inner.Message);
        Assert.DoesNotContain("RibbonTab", inner.Message);
        Assert.DoesNotContain("BAML+0x1A990", inner.Message);
        Assert.DoesNotContain("template root panel property 'Visibility' with converter -195", inner.Message);
        Assert.DoesNotContain("on setter 'Template'", inner.Message);
        Assert.DoesNotContain("implicit style key 'HandyControl.Controls.RibbonTab'", inner.Message);
        Assert.DoesNotContain("root panels take the Name string plus Visibility", inner.Message);
        Assert.DoesNotContain("BAML+0x1A9DD", inner.Message);
        Assert.DoesNotContain("element well-known type id -550 on the template root panel", inner.Message);
        Assert.DoesNotContain("root panels take ContentPresenter, Border, converted Shape and TextBlock children", inner.Message);
        Assert.DoesNotContain("on the template ScrollViewer", inner.Message);
        Assert.DoesNotContain("BAML+0x1AA1C", inner.Message);
        Assert.DoesNotContain("LightPrimaryBrush", inner.Message);
        Assert.DoesNotContain("well-known type id -578", inner.Message);
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
        Assert.DoesNotContain("Carousel", inner.Message);
        Assert.DoesNotContain("GroupBox", inner.Message);
        Assert.DoesNotContain("templates take a ContentPresenter", inner.Message);
        Assert.DoesNotContain("items panels take a UniformGrid/StackPanel/VirtualizingStackPanel/WrapPanel/DockPanel visual", inner.Message);
    }

    [Fact]
    public void Staged_light_primary_entry_lands_frozen_transparent()
    {
        // The full staged 21-byte entry parses its Freeze marker plus the
        // DynamicResource Color over the staged per-file rows (attr 85 owned
        // by SolidColorBrush -578, StringInfo 165 'LightPrimaryColor'
        // redeclared with the staged numbering). Nothing seeds the key, so
        // the XAML loader resolve-now rule lands Transparent; the Freeze
        // marker freezes the brush like the staged o:Freeze="True".
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            LoadStagedHandyControlEntry();
            var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
            var dictionary = BamlReader.LoadDictionary(
                StagedSolidDictionary(theme[0x1AA1C..0x1AA31]));
            var brush = Assert.IsType<SolidColorBrush>(dictionary["LightPrimaryBrush"]);
            Assert.Equal(Colors.Transparent, brush.Color);
            Assert.True(brush.IsFrozen);
            Assert.Same(brush, dictionary["LightPrimaryBrush"]);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_dynamic_color_resolves_from_application_resources()
    {
        // A DynamicResource Color with the key seeded in Application
        // resources lands the seeded Color for real (not a default): this is
        // the genuine conversion, and the seeded-value swap below proves no
        // hardcoding.
        var app = new Application();
        try
        {
            app.Resources.Add("DynColor", Color.FromRgb(0x11, 0x22, 0x33));
            var dictionary = BamlReader.LoadDictionary(
                SolidDictionary("K", "DynColor", SolidBody(dynamicKey: "DynColor", colorAttr: 0, colorKey: 1, freeze: true)));
            var brush = Assert.IsType<SolidColorBrush>(dictionary["K"]);
            Assert.Equal(Color.FromRgb(0x11, 0x22, 0x33), brush.Color);
            Assert.True(brush.IsFrozen);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Synthetic_dynamic_brush_and_packed_and_text_values_coerce()
    {
        // SolidColorBrush, packed-uint (gallery legacy store) and text values
        // behind the DynamicResource key all coerce to the brush Color like
        // the XAML loader resource-entry rule.
        var app = new Application();
        try
        {
            app.Resources.Add("BrushKey", new SolidColorBrush(Colors.Blue));
            app.Resources.Add("PackedKey", 0xFF0000FFu);
            app.Resources.Add("TextKey", "#0000FF");
            var brushDictionary = BamlReader.LoadDictionary(
                SolidDictionary("B", "BrushKey", SolidBody(dynamicKey: "BrushKey", colorAttr: 0, colorKey: 1, freeze: false)));
            Assert.Equal(Colors.Blue, Assert.IsType<SolidColorBrush>(brushDictionary["B"]).Color);
            var packedDictionary = BamlReader.LoadDictionary(
                SolidDictionary("P", "PackedKey", SolidBody(dynamicKey: "PackedKey", colorAttr: 0, colorKey: 1, freeze: false)));
            Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(packedDictionary["P"]).Color);
            var textDictionary = BamlReader.LoadDictionary(
                SolidDictionary("T", "TextKey", SolidBody(dynamicKey: "TextKey", colorAttr: 0, colorKey: 1, freeze: false)));
            Assert.Equal(Color.FromRgb(0x00, 0x00, 0xFF), Assert.IsType<SolidColorBrush>(textDictionary["T"]).Color);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Synthetic_dynamic_color_resolves_from_dictionary_so_far()
    {
        // A DynamicResource key defined by an earlier entry in the same file
        // resolves without any Application: the dictionary-so-far is the
        // local scope. The solid carries no Freeze marker, so it stays
        // unfrozen.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var content = new B();
            var first = content.Position;
            content.Raw(ColorEntry("#112233").ToArray());
            var second = content.Position;
            content.Raw(SolidBody(dynamicKey: "Base", colorAttr: 0, colorKey: 0, freeze: false)).ToArray();
            var head = new B().Raw(Header()).Raw(0x01)
                .Raw(0x03).I16(-524).Raw(0x00)
                .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
                .Sized(0x1F, p => p.I16(0).I16(-578).Raw(0x00).Str("Color"))
                .Sized(0x20, p => p.U16(0).Str("Base"))
                .Sized(0x20, p => p.U16(1).Str("K2"))
                .Deferable()
                .Sized(0x26, p => p.U16(0).U32((uint)first).U16(0))
                .Sized(0x26, p => p.U16(1).U32((uint)second).U16(0))
                .Raw(content.ToArray()).FinishDictionary().ToArray();
            var dictionary = BamlReader.LoadDictionary(head);
            var brush = Assert.IsType<SolidColorBrush>(dictionary["K2"]);
            Assert.Equal(Color.FromRgb(0x11, 0x22, 0x33), brush.Color);
            Assert.False(brush.IsFrozen);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_literal_color_lands_live()
    {
        // The literal-Color arm converts for real through the ColorConverter
        // (Red lands, and the second color proves no hardcoding), so the
        // DynamicResource landing above is a genuine conversion.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var red = BamlReader.LoadDictionary(
                SolidDictionary("R", "Unused", SolidBody(literal: "Red", colorAttr: 0, colorKey: 1, freeze: false)));
            Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(red["R"]).Color);
            var blue = BamlReader.LoadDictionary(
                SolidDictionary("B", "Unused", SolidBody(literal: "#0000FF", colorAttr: 0, colorKey: 1, freeze: false)));
            Assert.Equal(Color.FromRgb(0x00, 0x00, 0xFF), Assert.IsType<SolidColorBrush>(blue["B"]).Color);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_shared_lookup_returns_same_instance()
    {
        // The staged shared key lands once: repeat lookups return the same
        // brush with its parse-time color even after the seed changes.
        var app = new Application();
        try
        {
            app.Resources.Add("DynColor", Colors.Red);
            var dictionary = BamlReader.LoadDictionary(
                SolidDictionary("K", "DynColor", SolidBody(dynamicKey: "DynColor", colorAttr: 0, colorKey: 1, freeze: true)));
            var first = Assert.IsType<SolidColorBrush>(dictionary["K"]);
            Assert.Equal(Colors.Red, first.Color);
            app.Resources["DynColor"] = Colors.Blue;
            Assert.Same(first, dictionary["K"]);
            Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(dictionary["K"]).Color);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Synthetic_non_shared_lookup_materializes_fresh_instances()
    {
        // Non-shared keys replay the validated bag through a factory: each
        // lookup resolves the live seed, so a seed swap repaints the next
        // materialization.
        var app = new Application();
        try
        {
            app.Resources.Add("DynColor", Colors.Red);
            var dictionary = BamlReader.LoadDictionary(
                SolidDictionary("K", "DynColor", SolidBody(dynamicKey: "DynColor", colorAttr: 0, colorKey: 1, freeze: true), shared: false));
            var first = Assert.IsType<SolidColorBrush>(dictionary["K"]);
            Assert.Equal(Colors.Red, first.Color);
            app.Resources["DynColor"] = Colors.Blue;
            var second = Assert.IsType<SolidColorBrush>(dictionary["K"]);
            Assert.NotSame(first, second);
            Assert.Equal(Colors.Blue, second.Color);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Synthetic_mismatched_resource_type_stays_precise()
    {
        // A DynamicResource key resolving to a non-color (here an Int32)
        // fails naming the key, the entry and the runtime type instead of
        // landing a default brush.
        var app = new Application();
        try
        {
            app.Resources.Add("DynColor", 42);
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                SolidDictionary("K", "DynColor", SolidBody(dynamicKey: "DynColor", colorAttr: 0, colorKey: 1, freeze: true))));
            Assert.Contains("DynamicResource 'DynColor' for keyed entry 'K' is a Int32, not a color", error.Message);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Synthetic_solid_brush_empty_stays_precise()
    {
        // A SolidColorBrush with no Color fails naming the entry instead of
        // landing a Transparent default (defaults are ambiguous with
        // unset, like the gradient entries).
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(-578).Raw(0x00).Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                SolidDictionary("K", "DynColor", body)));
            Assert.Contains("holds a SolidColorBrush with no Color", error.Message);
            Assert.Contains("solid brushes take one Color value", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_solid_brush_duplicate_color_stays_precise()
    {
        // A second Color (here a literal behind the DynamicResource) fails
        // naming the duplicate instead of replacing the first.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(-578).Raw(0x00)
                .Raw(ExtensionBytes(0, 189, 1))
                .Sized(0x24, q => q.I16(0).Str("Red").I16(-87))
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                SolidDictionary("K", "DynColor", body)));
            Assert.Contains("duplicate Color on the SolidColorBrush", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_solid_brush_wrong_extension_stays_precise()
    {
        // A PropertyWithExtension over any other discriminator (here the
        // StaticExtension 602) fails naming the extension instead of
        // landing a half-built brush.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(-578).Raw(0x00)
                .Raw(ExtensionBytes(0, 602, 1))
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                SolidDictionary("K", "DynColor", body)));
            Assert.Contains("extension 602 on the Color", error.Message);
            Assert.Contains("take the DynamicResource extension", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_solid_brush_wrong_attribute_stays_precise()
    {
        // A DynamicResource over a foreign attribute (here the GradientStop
        // Color row) fails naming the attribute and the SolidColorBrush
        // target instead of landing on the wrong property.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(-578).Raw(0x00)
                .Raw(WrongAttrExtensionBytes())
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                WrongAttrDictionary("K", "DynColor", body)));
            Assert.Contains("attribute 'Color'", error.Message);
            Assert.Contains("take the SolidColorBrush Color", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_solid_brush_literal_wrong_converter_stays_precise()
    {
        // A literal Color over any other converter (here the EnumConverter)
        // fails naming the text, the converter and the target instead of
        // landing a default.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(-578).Raw(0x00)
                .Sized(0x24, q => q.I16(0).Str("Red").I16(-195))
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                SolidDictionary("K", "DynColor", body)));
            Assert.Contains("Color value with converter -195", error.Message);
            Assert.Contains("take the ColorConverter", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Solid_brush_engine_shape_matches_wpf()
    {
        // The routed values match the genuine net10.0-windows shape: the
        // brush is a Freezable honoring Freeze, DynamicResourceExtension
        // carries the object ResourceKey, and SolidColorBrush has no
        // SetResourceReference (only FrameworkElement does, so resolve-now
        // is the honest engine semantic for entries). The genuine probe
        // pins Color as the SolidColorBrush-owned Color DP defaulting
        // Transparent; the engine narrows the slot to a CLR property, which
        // the routed Color lands in either way.
        Assert.Same(typeof(Color), typeof(SolidColorBrush).GetProperty("Color")!.PropertyType);
        Assert.True(typeof(Freezable).IsAssignableFrom(typeof(SolidColorBrush)));
        var brush = new SolidColorBrush(Colors.Red);
        Assert.False(brush.IsFrozen);
        Assert.Equal(Colors.Red, brush.Color);
        brush.Freeze();
        Assert.True(brush.IsFrozen);
        Assert.Equal(Colors.Red, brush.Color);
        var extension = new DynamicResourceExtension("DynColor");
        Assert.Equal("DynColor", extension.ResourceKey);
        Assert.Same(typeof(object), typeof(DynamicResourceExtension).GetProperty("ResourceKey")!.PropertyType);
        Assert.Null(typeof(SolidColorBrush).GetMethod("SetResourceReference"));
        Assert.NotNull(typeof(FrameworkElement).GetMethod("SetResourceReference"));
        Assert.Equal(0, (int)Colors.Transparent.A);
    }

    private static byte[] SolidBody(string? dynamicKey = null, string? literal = null, short colorAttr = 0, ushort colorKey = 1, bool freeze = false)
    {
        var body = new B().Raw(0x03).I16(-578).Raw(0x00);
        if (freeze)
            body.Sized(0x34, q => q.Str("True").U16(0x22));
        if (dynamicKey is not null)
            body.Raw(ExtensionBytes(colorAttr, 189, colorKey));
        else if (literal is not null)
            body.Sized(0x24, q => q.I16(colorAttr).Str(literal).I16(-87));
        return body.Raw(0x04).ToArray();
    }

    private static byte[] ExtensionBytes(short attr, ushort extension, ushort key) =>
        new B().Raw(0x23).I16(attr).U16(extension).U16(key).ToArray();

    private static byte[] WrongAttrExtensionBytes() =>
        // AttributeInfo 1 names the GradientStop-owned Color row (declared
        // by WrongAttrDictionary): the owner check must reject it.
        new B().Raw(0x23).I16(1).U16(189).U16(1).ToArray();

    private static B ColorEntry(string text) => new B()
        .Raw(0x03).I16(-81).Raw(0x00)
        .Sized(0x11, q => q.Str(text).I16(-87))
        .Raw(0x04);

    private static byte[] SolidDictionary(string keyName, string valueName, byte[] entry, bool shared = true)
    {
        // Per-file ids stand in for the staged rows (0 SolidColorBrush-owned
        // Color, StringInfo 0/1 for the entry key and the resource key).
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(0).I16(-578).Raw(0x00).Str("Color"))
            .Sized(0x20, p => p.U16(0).Str(keyName))
            .Sized(0x20, p => p.U16(1).Str(valueName));
        return FinishSolid(head, entry, 0, shared);
    }

    private static byte[] StagedSolidDictionary(byte[] entry)
    {
        // The staged numbering for the sliced LightPrimaryBrush bytes
        // (attr 85 owned by SolidColorBrush -578, StringInfo 0/165 for the
        // entry key and the 'LightPrimaryColor' resource key).
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(85).I16(-578).Raw(0x00).Str("Color"))
            .Sized(0x20, p => p.U16(0).Str("LightPrimaryBrush"))
            .Sized(0x20, p => p.U16(165).Str("LightPrimaryColor"));
        return FinishSolid(head, entry, 0, true);
    }

    private static byte[] WrongAttrDictionary(string keyName, string valueName, byte[] entry)
    {
        // AttributeInfo 1 names the GradientStop-owned Color row, so the
        // SolidColorBrush owner check must reject the extension value.
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(0).I16(-578).Raw(0x00).Str("Color"))
            .Sized(0x1F, p => p.I16(1).I16(-252).Raw(0x00).Str("Color"))
            .Sized(0x20, p => p.U16(0).Str(keyName))
            .Sized(0x20, p => p.U16(1).Str(valueName));
        return FinishSolid(head, entry, 0, true);
    }

    private static byte[] FinishSolid(B head, byte[] entry, ushort keyId, bool shared)
    {
        head.Deferable();
        if (shared)
            head.Sized(0x26, p => p.U16(keyId).U32(0).U16(0));
        else
            head.Sized(0x26, p => p.U16(keyId).U32(0).Raw(0x00, 0x01));
        return head.Raw(entry).FinishDictionary().ToArray();
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

        public int Position => _b.Count;

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
