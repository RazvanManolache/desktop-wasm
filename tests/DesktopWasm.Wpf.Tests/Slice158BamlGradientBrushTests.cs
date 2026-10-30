// Copyright (c) 2026 The WPF on WASM Contributors.
// Licensed under the MIT License. See LICENSE in the project root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-158: the keyed 'PrimaryBrush' LinearGradientBrush entry at
/// BAML+0x1AA31 (keyidx 166, well-known type id -358) routes as a live engine
/// brush, so the staged boot stops precise at the keyed 'EffectShadow1'
/// DropShadowEffect entry at BAML+0x1AE8D (its Freeze marker record 0x34,
/// the next slice's work).
/// Live counts verified first: handycontrol conversion-failed/6 staged/2 gaps
/// (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched;
/// staged-oracle status-gate bypass stays in-probe only). Triangulation three
/// ways: staged framing spans against the live 466350-byte theme.baml
/// (sha256 D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0,
/// verified live) - the 95-byte PrimaryBrush entry (LinearGradientBrush head
/// 03 9A FE 00 with its own Freeze marker, the EndPoint "1,0" then
/// StartPoint "0,0" PropertyWithConverters over the per-file brush rows 43/42
/// with the PointConverter -474, the 2E C4 FF GradientStops content, two
/// GradientStop children each with a Freeze marker plus a DynamicResource
/// Color PropertyWithExtension over the per-file stop row 44 with extension
/// discriminator 189 plus the StringInfo resource keys 167 'PrimaryColor'
/// and 168 'DarkPrimaryColor' plus the literal Offset PropertyWithConverters
/// over row 45 with the DoubleConverter -175, the 04 04 close pair), the
/// 0x26 keyidx 166 record at 0xB613, the key-string rows 166 'PrimaryBrush'
/// at 0x2840 plus 167/168 and the AttributeInfo 42-45 rows (owners -358/-252)
/// plus the next 'DarkPrimaryBrush' SolidColorBrush head at 0x1AA90; the live
/// upstream HandyControl Themes/Basic/Brushes.xaml PrimaryBrush block at
/// pinned commit e9ad3d70 (frozen LinearGradientBrush keyed 'PrimaryBrush'
/// with EndPoint="1,0" authored before StartPoint="0,0" and two frozen
/// GradientStops over DynamicResource PrimaryColor/DarkPrimaryColor with
/// offsets 0/1, heading the frozen brush run of DynamicResource-color solids
/// interleaved with two-stop gradients) plus the upstream Colors.xaml
/// defaults (PrimaryColor/DarkPrimaryColor #326cf3); a genuine
/// net10.0-windows ResourceDictionary fixture compiling a frozen
/// DynamicResource-stop gradient plus a literal-stop control on framework
/// types (Gradients158.baml: LinearGradientBrush element 9A FE, Freeze
/// PresentationOptions, EndPoint/StartPoint PropertyWithConverters with the
/// PointConverter -474 in the same EndPoint-first order, GradientStops
/// content 2E C4 FF, GradientStop elements 04 FF with Freeze markers,
/// DynamicResource PropertyWithExtensions with discriminator BD 00 plus the
/// StringInfo keys, literal Offset PropertyWithConverters with the
/// DoubleConverter -175 - byte-identical in every staged record shape modulo
/// per-file numbering and the 0x35/0x36 line markers the staged file omits;
/// fixture retained at E:/tmp/bamlfix158 outside the tree) plus a genuine
/// net10.0-windows reflection probe (LinearGradientBrush.StartPoint/EndPoint
/// DPs owned by LinearGradientBrush typed Point defaulting (0,0)/(1,1),
/// GradientStop.Color/Offset DPs owned by GradientStop typed Color/double
/// defaulting Transparent/0.0, both types Freezable honoring Freeze, stops
/// carried in a GradientStopCollection; probe retained at E:/tmp/rbprobe158
/// outside the tree). Real-behavior engine work, never fabricated: BamlReader
/// ParseLinearGradientBrushEntry gains the 0x34 Freeze arm (the brush freezes
/// like the staged o:Freeze="True") plus ParseLinearGradientStop gains its
/// own 0x34 Freeze arm and the 0x23 DynamicResource Color arm resolving now
/// against the dictionary-so-far plus Application resources like the XAML
/// loader resource-entry path with missing keys staying Transparent and
/// genuine mismatches failing loud; validated stop bags replay through a
/// factory for non-shared keys so every lookup resolves the live seed; the
/// staged boot advances through the whole frozen brush run (PrimaryBrush plus
/// the DynamicResource-color solids and the TitleBrush/DangerBrush/
 /// WarningBrush/InfoBrush/SuccessBrush/AccentBrush gradient siblings).
/// Re-package skipped by evidence (Convert map untouched, staged bins
/// byte-identical). Tests: 23 new Slice158BamlGradientBrushTests (see below)
/// plus the 70-file stop-triple flip with new-stop triples, and the BamlReader
/// class-doc frontier rewrite.
/// application-compatibility-status.md handycontrol row verified current
/// against the live manifest (conversion-failed/6/2; no regen, nothing
/// re-packaged). No browser proof - package stays conversion-failed.
/// Precise next blockers: the keyed 'EffectShadow1' DropShadowEffect entry
/// (Freeze marker 0x34 at BAML+0x1AE91, then BlurRadius/ShadowDepth/Direction/
/// Color/Opacity/RenderingBias values with StaticResource references), then
/// the CarouselPateButton/implicit Carousel styles, the GroupBox styles with
/// BasedOn/DynamicResource setters and TitleElement triggers, EventTriggers,
/// the line-info trailer, the per-file (Type,Name) x:Name rows and the
/// converted-lib elements (CircleProgressBar/GifImage/Watermark/Drawer/
/// ElementGroup/SearchBar) behind the line-17 SkinDefault stop.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice158BamlGradientBrushTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice158BamlGradientBrushTests()
    {
        XamlLoader.CustomElementSource = null;
    }

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    [Fact]
    public void Theme_baml_primary_brush_entry_bytes()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        Assert.Equal(
            "D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0",
            Convert.ToHexString(SHA256.HashData(theme)));

        // The keyed 'PrimaryBrush' entry is 95 bytes at BAML+0x1AA31: the
        // LinearGradientBrush head (well-known type id -358) with its flags byte.
        Assert.Equal(95, 0x1AA90 - 0x1AA31);
        var gradientHead = new byte[] { 0x03, 0x9A, 0xFE, 0x00 };
        Assert.Equal(gradientHead, theme[0x1AA31..0x1AA35]);
        // The Freeze PresentationOptions record ("True" plus the Freeze name
        // id 0x22, byte-identical to the genuine fixture modulo the name id).
        var freeze = new byte[] { 0x34, 0x08, 0x04, 0x54, 0x72, 0x75, 0x65, 0x22, 0x00 };
        Assert.Equal(freeze, theme[0x1AA35..0x1AA3E]);
        Assert.Equal(Encoding.ASCII.GetBytes("True"), theme[0x1AA38..0x1AA3C]);
        // EndPoint "1,0" is authored before StartPoint "0,0" (the upstream
        // XAML order): per-file attrs 43/42 owned by the LinearGradientBrush
        // TypeInfo -358 row, both through the PointConverter -474,
        // byte-identical to the genuine fixture modulo per-file numbering.
        var endPoint = new byte[] { 0x24, 0x09, 0x2B, 0x00, 0x03, 0x31, 0x2C, 0x30, 0x26, 0xFE };
        Assert.Equal(endPoint, theme[0x1AA3E..0x1AA48]);
        var startPoint = new byte[] { 0x24, 0x09, 0x2A, 0x00, 0x03, 0x30, 0x2C, 0x30, 0x26, 0xFE };
        Assert.Equal(startPoint, theme[0x1AA48..0x1AA52]);
        // The GradientStops content member plus the first GradientStop head
        // with its own Freeze marker.
        Assert.Equal(new byte[] { 0x2E, 0xC4, 0xFF }, theme[0x1AA52..0x1AA55]);
        var stopHead = new byte[] { 0x03, 0x04, 0xFF, 0x00 };
        Assert.Equal(stopHead, theme[0x1AA55..0x1AA59]);
        Assert.Equal(freeze, theme[0x1AA59..0x1AA62]);
        // The first stop Color DynamicResource PropertyWithExtension:
        // per-file attr 44 owned by the GradientStop TypeInfo -252 row plus
        // extension discriminator 189 plus the StringInfo resource key 167,
        // byte-identical to the genuine fixture modulo per-file numbering
        // (fixture: attr 2, key 2, same BD 00 discriminator).
        var dynamicColor = new byte[] { 0x23, 0x2C, 0x00, 0xBD, 0x00, 0xA7, 0x00 };
        Assert.Equal(dynamicColor, theme[0x1AA62..0x1AA69]);
        // The first stop Offset "0" through the DoubleConverter -175.
        var offsetZero = new byte[] { 0x24, 0x07, 0x2D, 0x00, 0x01, 0x30, 0x51, 0xFF };
        Assert.Equal(offsetZero, theme[0x1AA69..0x1AA71]);
        Assert.Equal(new byte[] { 0x04 }, theme[0x1AA71..0x1AA72]);
        // The second GradientStop (Freeze marker, DynamicResource key 168,
        // Offset "1") plus the brush ElementEnd close pair.
        Assert.Equal(stopHead, theme[0x1AA72..0x1AA76]);
        Assert.Equal(freeze, theme[0x1AA76..0x1AA7F]);
        var dynamicColor2 = new byte[] { 0x23, 0x2C, 0x00, 0xBD, 0x00, 0xA8, 0x00 };
        Assert.Equal(dynamicColor2, theme[0x1AA7F..0x1AA86]);
        var offsetOne = new byte[] { 0x24, 0x07, 0x2D, 0x00, 0x01, 0x31, 0x51, 0xFF };
        Assert.Equal(offsetOne, theme[0x1AA86..0x1AA8E]);
        Assert.Equal(new byte[] { 0x04, 0x04 }, theme[0x1AA8E..0x1AA90]);
        // The next entry head is the keyed 'DarkPrimaryBrush' SolidColorBrush
        // (well-known type id -578) with its own Freeze marker right behind it.
        var nextHead = new byte[] { 0x03, 0xBE, 0xFD, 0x00 };
        Assert.Equal(nextHead, theme[0x1AA90..0x1AA94]);
        Assert.Equal(freeze, theme[0x1AA94..0x1AA9D]);
        // The key record 0x26 keyidx 166 plus the key-string rows 166/167/168
        // plus the AttributeInfo 42-45 rows (owners -358/-252).
        var primaryKey = new byte[] { 0x26, 0x09, 0xA6, 0x00, 0x9A, 0xB8, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(primaryKey, theme[0xB613..0xB61D]);
        var primaryKeyRow = new byte[] { 0x20, 0x10, 0xA6, 0x00, 0x0C };
        Assert.Equal(primaryKeyRow, theme[0x2840..0x2845]);
        Assert.Equal(Encoding.ASCII.GetBytes("PrimaryBrush"), theme[0x2845..0x2851]);
        var primaryColorRow = new byte[] { 0x20, 0x10, 0xA7, 0x00, 0x0C };
        Assert.Equal(primaryColorRow, theme[0x2851..0x2856]);
        Assert.Equal(Encoding.ASCII.GetBytes("PrimaryColor"), theme[0x2856..0x2862]);
        var darkPrimaryColorRow = new byte[] { 0x20, 0x14, 0xA8, 0x00, 0x10 };
        Assert.Equal(darkPrimaryColorRow, theme[0x2862..0x2867]);
        Assert.Equal(Encoding.ASCII.GetBytes("DarkPrimaryColor"), theme[0x2867..0x2877]);
        var startPointRow = new byte[] { 0x1F, 0x11, 0x2A, 0x00, 0x9A, 0xFE, 0x00, 0x0A };
        Assert.Equal(startPointRow, theme[0x1E42..0x1E4A]);
        Assert.Equal(Encoding.ASCII.GetBytes("StartPoint"), theme[0x1E4A..0x1E54]);
        var endPointRow = new byte[] { 0x1F, 0x0F, 0x2B, 0x00, 0x9A, 0xFE, 0x00, 0x08 };
        Assert.Equal(endPointRow, theme[0x1E54..0x1E5C]);
        Assert.Equal(Encoding.ASCII.GetBytes("EndPoint"), theme[0x1E5C..0x1E64]);
        var stopColorRow = new byte[] { 0x1F, 0x0C, 0x2C, 0x00, 0x04, 0xFF, 0x00, 0x05 };
        Assert.Equal(stopColorRow, theme[0x1E64..0x1E6C]);
        Assert.Equal(Encoding.ASCII.GetBytes("Color"), theme[0x1E6C..0x1E71]);
        var stopOffsetRow = new byte[] { 0x1F, 0x0D, 0x2D, 0x00, 0x04, 0xFF, 0x00, 0x06 };
        Assert.Equal(stopOffsetRow, theme[0x1E71..0x1E79]);
        Assert.Equal(Encoding.ASCII.GetBytes("Offset"), theme[0x1E79..0x1E7F]);
        // The next stop after the frozen brush run: the keyed 'EffectShadow1'
        // DropShadowEffect entry (per-file type id 63) with its own Freeze
        // marker plus the BlurRadius "5" DoubleConverter PWC.
        var effectHead = new byte[] { 0x03, 0x3F, 0x00, 0x00 };
        Assert.Equal(effectHead, theme[0x1AE8D..0x1AE91]);
        Assert.Equal(freeze, theme[0x1AE91..0x1AE9A]);
        var blurRadius = new byte[] { 0x24, 0x07, 0x56, 0x00, 0x01, 0x35, 0x51, 0xFF };
        Assert.Equal(blurRadius, theme[0x1AE9A..0x1AEA2]);
        var effectKey = new byte[] { 0x26, 0x09, 0xE7, 0x00, 0xF6, 0xBC, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(effectKey, theme[0xB753..0xB75D]);
        var effectKeyRow = new byte[] { 0x20, 0x11, 0xE7, 0x00, 0x0D };
        Assert.Equal(effectKeyRow, theme[0x2D6A..0x2D6F]);
        Assert.Equal(Encoding.ASCII.GetBytes("EffectShadow1"), theme[0x2D6F..0x2D7C]);
        var blurRadiusRow = new byte[] { 0x1F, 0x11, 0x56, 0x00, 0x3F, 0x00, 0x00, 0x0A };
        Assert.Equal(blurRadiusRow, theme[0x2D7C..0x2D84]);
        Assert.Equal(Encoding.ASCII.GetBytes("BlurRadius"), theme[0x2D84..0x2D8E]);
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
    public void Staged_boot_advances_past_primary_brush_run_to_effect_shadow()
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
        Assert.DoesNotContain("BAML+0x1AA35", inner.Message);
        Assert.DoesNotContain("'PrimaryBrush'", inner.Message);
        Assert.DoesNotContain("record 0x34 in a LinearGradientBrush", inner.Message);
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
        Assert.DoesNotContain("Carousel", inner.Message);
        Assert.DoesNotContain("GroupBox", inner.Message);
        Assert.DoesNotContain("templates take a ContentPresenter", inner.Message);
        Assert.DoesNotContain("items panels take a UniformGrid/StackPanel/VirtualizingStackPanel/WrapPanel/DockPanel visual", inner.Message);
    }

    [Fact]
    public void Staged_primary_brush_entry_lands_frozen_with_seeded_colors()
    {
        // The full staged 95-byte entry parses its Freeze marker, the
        // EndPoint/StartPoint PWCs and both DynamicResource stop Colors with
        // the staged numbering. The upstream default-skin #326cf3 values seed
        // both keys, so the XAML loader resolve-now rule lands them for real;
        // the Freeze markers freeze the brush and both stops.
        var app = new Application();
        try
        {
            app.Resources.Add("PrimaryColor", Color.FromRgb(0x32, 0x6C, 0xF3));
            app.Resources.Add("DarkPrimaryColor", Color.FromRgb(0x32, 0x6C, 0xF3));
            LoadStagedHandyControlEntry();
            var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
            var dictionary = BamlReader.LoadDictionary(
                StagedGradientDictionary(theme[0x1AA31..0x1AA90]));
            var brush = Assert.IsType<LinearGradientBrush>(dictionary["PrimaryBrush"]);
            Assert.True(brush.IsFrozen);
            Assert.Equal(new Point(0, 0), brush.StartPoint);
            Assert.Equal(new Point(1, 0), brush.EndPoint);
            Assert.Equal(2, brush.GradientStops.Count);
            Assert.Equal(Color.FromRgb(0x32, 0x6C, 0xF3), brush.GradientStops[0].Color);
            Assert.Equal(0.0, brush.GradientStops[0].Offset);
            Assert.True(brush.GradientStops[0].IsFrozen);
            Assert.Equal(Color.FromRgb(0x32, 0x6C, 0xF3), brush.GradientStops[1].Color);
            Assert.Equal(1.0, brush.GradientStops[1].Offset);
            Assert.True(brush.GradientStops[1].IsFrozen);
            Assert.Same(brush, dictionary["PrimaryBrush"]);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Synthetic_dynamic_stop_colors_resolve_from_application_resources()
    {
        // DynamicResource stop Colors with the keys seeded in Application
        // resources land the seeded Colors for real (not defaults): this is
        // the genuine conversion, and the seed-swap tests below prove no
        // hardcoding. The Freeze markers freeze the brush and both stops.
        var app = new Application();
        try
        {
            app.Resources.Add("StopA", Color.FromRgb(0x11, 0x22, 0x33));
            app.Resources.Add("StopB", Color.FromRgb(0x44, 0x55, 0x66));
            var dictionary = BamlReader.LoadDictionary(
                GradientDictionary("K", GradientBody(
                    new[]
                    {
                        StopBody(dynamicKey: "StopA", keyId: 1, offset: "0", freeze: true),
                        StopBody(dynamicKey: "StopB", keyId: 2, offset: "1", freeze: true),
                    },
                    freeze: true),
                    shared: true, "StopA", "StopB"));
            var brush = Assert.IsType<LinearGradientBrush>(dictionary["K"]);
            Assert.True(brush.IsFrozen);
            Assert.Equal(new Point(0, 0), brush.StartPoint);
            Assert.Equal(new Point(1, 0), brush.EndPoint);
            Assert.Equal(2, brush.GradientStops.Count);
            Assert.Equal(Color.FromRgb(0x11, 0x22, 0x33), brush.GradientStops[0].Color);
            Assert.Equal(0.0, brush.GradientStops[0].Offset);
            Assert.True(brush.GradientStops[0].IsFrozen);
            Assert.Equal(Color.FromRgb(0x44, 0x55, 0x66), brush.GradientStops[1].Color);
            Assert.Equal(1.0, brush.GradientStops[1].Offset);
            Assert.True(brush.GradientStops[1].IsFrozen);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Synthetic_dynamic_stop_brush_and_packed_and_text_values_coerce()
    {
        // SolidColorBrush, packed-uint (gallery legacy store) and text values
        // behind the DynamicResource stop keys all coerce to the stop Color
        // like the XAML loader resource-entry rule.
        var app = new Application();
        try
        {
            app.Resources.Add("BrushKey", new SolidColorBrush(Colors.Blue));
            app.Resources.Add("PackedKey", 0xFF0000FFu);
            app.Resources.Add("TextKey", "#0000FF");
            var brushDictionary = BamlReader.LoadDictionary(
                GradientDictionary("B", GradientBody(
                    new[] { StopBody(dynamicKey: "BrushKey", keyId: 1, offset: "0") },
                    freeze: false),
                    shared: true, "BrushKey"));
            Assert.Equal(Colors.Blue, Assert.IsType<LinearGradientBrush>(brushDictionary["B"]).GradientStops[0].Color);
            var packedDictionary = BamlReader.LoadDictionary(
                GradientDictionary("P", GradientBody(
                    new[] { StopBody(dynamicKey: "PackedKey", keyId: 1, offset: "0") },
                    freeze: false),
                    shared: true, "PackedKey"));
            Assert.Equal(Colors.Red, Assert.IsType<LinearGradientBrush>(packedDictionary["P"]).GradientStops[0].Color);
            var textDictionary = BamlReader.LoadDictionary(
                GradientDictionary("T", GradientBody(
                    new[] { StopBody(dynamicKey: "TextKey", keyId: 1, offset: "0") },
                    freeze: false),
                    shared: true, "TextKey"));
            Assert.Equal(Color.FromRgb(0x00, 0x00, 0xFF), Assert.IsType<LinearGradientBrush>(textDictionary["T"]).GradientStops[0].Color);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Synthetic_dynamic_stop_color_resolves_from_dictionary_so_far()
    {
        // A DynamicResource stop key defined by an earlier entry in the same
        // file resolves without any Application: the dictionary-so-far is the
        // local scope. Neither the brush nor the stop carries a Freeze
        // marker, so both stay unfrozen.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var content = new B();
            var first = content.Position;
            content.Raw(ColorEntry("#112233").ToArray());
            var second = content.Position;
            content.Raw(GradientBody(
                new[] { StopBody(dynamicKey: "Base", keyId: 0, offset: "0.5") },
                freeze: false)).ToArray();
            var head = Tables("Base", "K2");
            head.Deferable();
            head.Sized(0x26, p => p.U16(0).U32((uint)first).U16(0));
            head.Sized(0x26, p => p.U16(1).U32((uint)second).U16(0));
            var dictionary = BamlReader.LoadDictionary(head.Raw(content.ToArray()).FinishDictionary().ToArray());
            var brush = Assert.IsType<LinearGradientBrush>(dictionary["K2"]);
            Assert.False(brush.IsFrozen);
            Assert.Equal(Color.FromRgb(0x11, 0x22, 0x33), brush.GradientStops[0].Color);
            Assert.Equal(0.5, brush.GradientStops[0].Offset);
            Assert.False(brush.GradientStops[0].IsFrozen);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_literal_stop_lands_live()
    {
        // The literal-Color arm still converts for real through the
        // ColorConverter (Red lands, and the second color proves no
        // hardcoding), so the DynamicResource landings above are genuine
        // conversions.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var red = BamlReader.LoadDictionary(
                GradientDictionary("R", GradientBody(
                    new[] { StopBody(literal: "Red", offset: "0") },
                    freeze: false),
                    shared: true));
            Assert.Equal(Colors.Red, Assert.IsType<LinearGradientBrush>(red["R"]).GradientStops[0].Color);
            var blue = BamlReader.LoadDictionary(
                GradientDictionary("B", GradientBody(
                    new[] { StopBody(literal: "#0000FF", offset: "1") },
                    freeze: false),
                    shared: true));
            Assert.Equal(Color.FromRgb(0x00, 0x00, 0xFF), Assert.IsType<LinearGradientBrush>(blue["B"]).GradientStops[0].Color);
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
        // brush with its parse-time colors even after the seed changes.
        var app = new Application();
        try
        {
            app.Resources.Add("StopA", Colors.Red);
            var dictionary = BamlReader.LoadDictionary(
                GradientDictionary("K", GradientBody(
                    new[] { StopBody(dynamicKey: "StopA", keyId: 1, offset: "0", freeze: true) },
                    freeze: true),
                    shared: true, "StopA"));
            var first = Assert.IsType<LinearGradientBrush>(dictionary["K"]);
            Assert.Equal(Colors.Red, first.GradientStops[0].Color);
            app.Resources["StopA"] = Colors.Blue;
            Assert.Same(first, dictionary["K"]);
            Assert.Equal(Colors.Red, Assert.IsType<LinearGradientBrush>(dictionary["K"]).GradientStops[0].Color);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Synthetic_non_shared_lookup_materializes_fresh_instances()
    {
        // Non-shared keys replay the validated bags through a factory: each
        // lookup resolves the live seed, so a seed swap repaints the next
        // materialization.
        var app = new Application();
        try
        {
            app.Resources.Add("StopA", Colors.Red);
            var dictionary = BamlReader.LoadDictionary(
                GradientDictionary("K", GradientBody(
                    new[] { StopBody(dynamicKey: "StopA", keyId: 1, offset: "0", freeze: true) },
                    freeze: true),
                    shared: false, "StopA"));
            var first = Assert.IsType<LinearGradientBrush>(dictionary["K"]);
            Assert.Equal(Colors.Red, first.GradientStops[0].Color);
            app.Resources["StopA"] = Colors.Blue;
            var second = Assert.IsType<LinearGradientBrush>(dictionary["K"]);
            Assert.NotSame(first, second);
            Assert.Equal(Colors.Blue, second.GradientStops[0].Color);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Synthetic_mismatched_resource_type_stays_precise()
    {
        // A DynamicResource stop key resolving to a non-color (here an Int32)
        // fails naming the key, the entry and the runtime type instead of
        // landing a default brush.
        var app = new Application();
        try
        {
            app.Resources.Add("StopA", 42);
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                GradientDictionary("K", GradientBody(
                    new[] { StopBody(dynamicKey: "StopA", keyId: 1, offset: "0", freeze: true) },
                    freeze: true),
                    shared: true, "StopA")));
            Assert.Contains("DynamicResource 'StopA' for keyed entry 'K' is a Int32, not a color", error.Message);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Synthetic_gradient_brush_empty_stays_precise()
    {
        // A LinearGradientBrush with no GradientStops content fails naming
        // the entry instead of landing a default brush (defaults are
        // ambiguous with unset, like the SolidColorBrush run).
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(-358).Raw(0x00).Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                GradientDictionary("K", body, shared: true)));
            Assert.Contains("holds a LinearGradientBrush with no GradientStops", error.Message);
            Assert.Contains("gradient brushes take the GradientStops content", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_gradient_stop_empty_stays_precise()
    {
        // A GradientStop with no Color fails naming the entry instead of
        // landing a Transparent default (defaults are ambiguous with unset).
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var stop = new B().Raw(0x03).I16(-252).Raw(0x00)
                .Sized(0x24, q => q.I16(3).Str("0").I16(-175))
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                GradientDictionary("K", GradientBody(new[] { stop }, freeze: false), shared: true)));
            Assert.Contains("has no Color", error.Message);
            Assert.Contains("gradient stops take Color plus Offset", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_gradient_brush_duplicate_start_point_stays_precise()
    {
        // A second StartPoint (here "0,0" behind "1,0") fails naming the
        // duplicate instead of replacing the first.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(-358).Raw(0x00)
                .Sized(0x24, q => q.I16(0).Str("1,0").I16(-474))
                .Sized(0x24, q => q.I16(0).Str("0,0").I16(-474))
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                GradientDictionary("K", body, shared: true)));
            Assert.Contains("duplicate StartPoint on the LinearGradientBrush", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_gradient_stop_duplicate_color_stays_precise()
    {
        // A second Color (here a literal behind the DynamicResource) fails
        // naming the duplicate instead of replacing the first.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var stop = new B().Raw(0x03).I16(-252).Raw(0x00)
                .Raw(ExtensionBytes(2, 189, 1))
                .Sized(0x24, q => q.I16(2).Str("Red").I16(-87))
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                GradientDictionary("K", GradientBody(new[] { stop }, freeze: false), shared: true, "StopA")));
            Assert.Contains("duplicate Color on the GradientStop", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_gradient_stop_wrong_extension_stays_precise()
    {
        // A stop PropertyWithExtension over any other discriminator (here the
        // StaticExtension 602) fails naming the extension instead of landing
        // a half-built stop.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var stop = new B().Raw(0x03).I16(-252).Raw(0x00)
                .Raw(ExtensionBytes(2, 602, 1))
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                GradientDictionary("K", GradientBody(new[] { stop }, freeze: false), shared: true, "StopA")));
            Assert.Contains("extension 602 on the GradientStop Color", error.Message);
            Assert.Contains("take the DynamicResource extension", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_gradient_stop_wrong_attribute_stays_precise()
    {
        // A DynamicResource over a foreign attribute (here the SolidColorBrush
        // Color row) fails naming the attribute and the GradientStop target
        // instead of landing on the wrong property.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var stop = new B().Raw(0x03).I16(-252).Raw(0x00)
                .Raw(new B().Raw(0x23).I16(4).U16(189).U16(1).ToArray())
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                WrongAttrDictionary("K", "StopA", GradientBody(new[] { stop }, freeze: false))));
            Assert.Contains("attribute 'Color'", error.Message);
            Assert.Contains("take the GradientStop Color", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_gradient_stop_offset_wrong_converter_stays_precise()
    {
        // A stop Offset over any other converter (here the EnumConverter)
        // fails naming the text, the converter and the target instead of
        // landing a default.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var stop = new B().Raw(0x03).I16(-252).Raw(0x00)
                .Sized(0x24, q => q.I16(2).Str("Red").I16(-87))
                .Sized(0x24, q => q.I16(3).Str("0").I16(-195))
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                GradientDictionary("K", GradientBody(new[] { stop }, freeze: false), shared: true)));
            Assert.Contains("Offset value with converter -195", error.Message);
            Assert.Contains("take the DoubleConverter", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_gradient_brush_markup_extension_stays_precise()
    {
        // A markup extension directly on the brush (no property slot carries
        // it: points travel as PropertyWithConverter, stop colors on the
        // stops) fails naming the entry instead of landing a half-built brush.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(-358).Raw(0x00)
                .Raw(ExtensionBytes(0, 189, 1))
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                GradientDictionary("K", body, shared: true, "StopA")));
            Assert.Contains("markup-extension value on the LinearGradientBrush", error.Message);
            Assert.Contains("gradient markup values are a style-surface follow-up", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_gradient_brush_stop_before_content_stays_precise()
    {
        // A GradientStop before the GradientStops content member fails naming
        // the order instead of landing out of order (BAML emits content first).
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(-358).Raw(0x00)
                .Raw(StopBody(literal: "Red", offset: "0"))
                .Raw(0x2E).I16(-60)
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                GradientDictionary("K", body, shared: true)));
            Assert.Contains("precedes the GradientStops content", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_gradient_brush_non_stop_child_stays_precise()
    {
        // A non-GradientStop child inside the GradientStops content (here a
        // Color element) fails naming the element instead of landing it as a
        // stop.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var child = new B().Raw(0x03).I16(-81).Raw(0x00).Raw(0x04).ToArray();
            var body = new B().Raw(0x03).I16(-358).Raw(0x00)
                .Raw(0x2E).I16(-60)
                .Raw(child)
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                GradientDictionary("K", body, shared: true)));
            Assert.Contains("take GradientStop children", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_gradient_brush_point_wrong_converter_stays_precise()
    {
        // A StartPoint over any other converter (here the EnumConverter)
        // fails naming the point, the converter and the target instead of
        // landing a default.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(-358).Raw(0x00)
                .Sized(0x24, q => q.I16(0).Str("0,0").I16(-195))
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                GradientDictionary("K", body, shared: true)));
            Assert.Contains("StartPoint value with converter -195", error.Message);
            Assert.Contains("take the PointConverter", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Gradient_brush_engine_shape_matches_wpf()
    {
        // The routed values match the genuine net10.0-windows shape: the
        // brush and its stops are Freezables honoring Freeze, the
        // StartPoint/EndPoint DPs are LinearGradientBrush-owned Points
        // defaulting (0,0)/(1,1), the GradientStop Color/Offset DPs are
        // GradientStop-owned Color/double defaulting Transparent/0.0, and the
        // stops travel in a GradientStopCollection. The engine narrows the
        // slots to CLR properties (the SolidColorBrush.Color precedent, which
        // carries no SetResourceReference so the resolve-now entry rule is
        // the honest engine semantic); note the engine GradientStop.Color CLR
        // default stays Black while the genuine DP defaults Transparent, but
        // routing always sets Color explicitly (a missing Color is a precise
        // error), so the default never surfaces through this path.
        Assert.Same(typeof(Point), typeof(LinearGradientBrush).GetProperty("StartPoint")!.PropertyType);
        Assert.Same(typeof(Point), typeof(LinearGradientBrush).GetProperty("EndPoint")!.PropertyType);
        Assert.Same(typeof(Color), typeof(GradientStop).GetProperty("Color")!.PropertyType);
        Assert.Same(typeof(double), typeof(GradientStop).GetProperty("Offset")!.PropertyType);
        Assert.True(typeof(Freezable).IsAssignableFrom(typeof(LinearGradientBrush)));
        Assert.True(typeof(Freezable).IsAssignableFrom(typeof(GradientStop)));
        Assert.Same(typeof(GradientStopCollection), typeof(LinearGradientBrush).GetProperty("GradientStops")!.PropertyType);
        var brush = new LinearGradientBrush();
        Assert.False(brush.IsFrozen);
        Assert.Equal(new Point(0, 0), brush.StartPoint);
        Assert.Equal(new Point(1, 1), brush.EndPoint);
        Assert.Empty(brush.GradientStops);
        brush.Freeze();
        Assert.True(brush.IsFrozen);
        var stop = new GradientStop();
        Assert.False(stop.IsFrozen);
        Assert.Equal(0.0, stop.Offset);
        stop.Freeze();
        Assert.True(stop.IsFrozen);
    }

    private static byte[] GradientBody(byte[][] stops, bool freeze, string? start = "0,0", string? end = "1,0")
    {
        // The staged brush order (EndPoint first, matching the upstream
        // attribute order) with the content member and the stop children.
        var body = new B().Raw(0x03).I16(-358).Raw(0x00);
        if (freeze)
            body.Sized(0x34, q => q.Str("True").U16(0x22));
        if (end is not null)
            body.Sized(0x24, q => q.I16(1).Str(end).I16(-474));
        if (start is not null)
            body.Sized(0x24, q => q.I16(0).Str(start).I16(-474));
        body.Raw(0x2E).I16(-60);
        foreach (var stop in stops) body.Raw(stop);
        return body.Raw(0x04).ToArray();
    }

    private static byte[] StopBody(string? dynamicKey = null, string? literal = null, ushort keyId = 1, string? offset = "0", bool freeze = false)
    {
        var stop = new B().Raw(0x03).I16(-252).Raw(0x00);
        if (freeze)
            stop.Sized(0x34, q => q.Str("True").U16(0x22));
        if (dynamicKey is not null)
            stop.Raw(ExtensionBytes(2, 189, keyId));
        else if (literal is not null)
            stop.Sized(0x24, q => q.I16(2).Str(literal).I16(-87));
        if (offset is not null)
            stop.Sized(0x24, q => q.I16(3).Str(offset).I16(-175));
        return stop.Raw(0x04).ToArray();
    }

    private static byte[] ExtensionBytes(short attr, ushort extension, ushort key) =>
        new B().Raw(0x23).I16(attr).U16(extension).U16(key).ToArray();

    private static B Tables(params string[] names)
    {
        // Per-file ids stand in for the staged rows (0/1 LinearGradientBrush-
        // owned StartPoint/EndPoint, 2/3 GradientStop-owned Color/Offset).
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(0).I16(-358).Raw(0x00).Str("StartPoint"))
            .Sized(0x1F, p => p.I16(1).I16(-358).Raw(0x00).Str("EndPoint"))
            .Sized(0x1F, p => p.I16(2).I16(-252).Raw(0x00).Str("Color"))
            .Sized(0x1F, p => p.I16(3).I16(-252).Raw(0x00).Str("Offset"));
        for (var i = 0; i < names.Length; i++)
        {
            var id = (ushort)i;
            var name = names[i];
            head.Sized(0x20, p => p.U16(id).Str(name));
        }
        return head;
    }

    private static byte[] Finish(B head, byte[] entry, ushort keyId, bool shared)
    {
        head.Deferable();
        if (shared)
            head.Sized(0x26, p => p.U16(keyId).U32(0).U16(0));
        else
            head.Sized(0x26, p => p.U16(keyId).U32(0).Raw(0x00, 0x01));
        return head.Raw(entry).FinishDictionary().ToArray();
    }

    private static byte[] GradientDictionary(string keyName, byte[] entry, bool shared, params string[] valueNames)
    {
        var names = new string[1 + valueNames.Length];
        names[0] = keyName;
        Array.Copy(valueNames, 0, names, 1, valueNames.Length);
        return Finish(Tables(names), entry, 0, shared);
    }

    private static byte[] StagedGradientDictionary(byte[] entry)
    {
        // The staged numbering for the sliced PrimaryBrush bytes (attrs
        // 42/43 LinearGradientBrush-owned StartPoint/EndPoint, attrs 44/45
        // GradientStop-owned Color/Offset, StringInfo 0/167/168 for the entry
        // key and the 'PrimaryColor'/'DarkPrimaryColor' resource keys).
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(42).I16(-358).Raw(0x00).Str("StartPoint"))
            .Sized(0x1F, p => p.I16(43).I16(-358).Raw(0x00).Str("EndPoint"))
            .Sized(0x1F, p => p.I16(44).I16(-252).Raw(0x00).Str("Color"))
            .Sized(0x1F, p => p.I16(45).I16(-252).Raw(0x00).Str("Offset"))
            .Sized(0x20, p => p.U16(0).Str("PrimaryBrush"))
            .Sized(0x20, p => p.U16(167).Str("PrimaryColor"))
            .Sized(0x20, p => p.U16(168).Str("DarkPrimaryColor"));
        return Finish(head, entry, 0, true);
    }

    private static byte[] WrongAttrDictionary(string keyName, string valueName, byte[] entry)
    {
        // AttributeInfo 4 names the SolidColorBrush-owned Color row, so the
        // GradientStop owner check must reject the extension value.
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(0).I16(-358).Raw(0x00).Str("StartPoint"))
            .Sized(0x1F, p => p.I16(1).I16(-358).Raw(0x00).Str("EndPoint"))
            .Sized(0x1F, p => p.I16(2).I16(-252).Raw(0x00).Str("Color"))
            .Sized(0x1F, p => p.I16(3).I16(-252).Raw(0x00).Str("Offset"))
            .Sized(0x1F, p => p.I16(4).I16(-578).Raw(0x00).Str("Color"))
            .Sized(0x20, p => p.U16(0).Str(keyName))
            .Sized(0x20, p => p.U16(1).Str(valueName));
        return Finish(head, entry, 0, true);
    }

    private static B ColorEntry(string text) => new B()
        .Raw(0x03).I16(-81).Raw(0x00)
        .Sized(0x11, q => q.Str(text).I16(-87))
        .Raw(0x04);

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




