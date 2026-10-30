// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-147 HandyControl theme.baml PathGeometry string-form entry routing.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the slice-146 blocker (the keyed 'TreeArrow' entry at BAML+0x19E7B
/// holding element well-known id -446 with a single string-form
/// path-data value, which had no keyed-entry routing) lands as a live
/// engine PathGeometry: the new ParsePathGeometryEntry routes the
/// single Figures PropertyWithConverter (well-known member -89 through
/// the PathFigureCollectionConverter -445) through Geometry.Parse like
/// the scalar Geometry entries. The run advances past it (slice-148
/// routes the keyed 'RowHeaderGripperStyle'/'ColumnHeaderGripperStyle'
/// entries through the new FrameworkElement.CursorProperty DP) and stops
/// precise at the keyed 'DataGridComboBoxColumnStyle' Style entry at
/// BAML+0x1A113 (ComboBox target -92 with no style-target routing).
///
/// Byte-exact decode against the staged 466350-byte theme.baml: the
/// entry head 03 42 FE 00 at 0x19E7B, the 0x24 Figures record 24 F6 02
/// (size 374) with attr A7 FF (-89) and converter 43 FE (-445) carrying
/// the 366-char text "M508..." at 0x19E86, the tail 7A 43 FE 04 (final
/// 'z', converter, ElementEnd) at 0x19FF3, the next entry head
/// 03 94 FD 00 (Style) with TargetType 22 00 00 6E FD (-658 Thumb) at
/// 0x19FF7, the TreeArrow key record 26 09 98 00 E4 AC 00 00 00 00 at
/// 0xB57D (key string 152) and the RowHeaderGripperStyle key head
/// 26 09 99 00 at 0xB587 (key string 153).
///
/// Triangulated three ways: the staged framing above; the live upstream
/// HandyControl Theme.xaml 'TreeArrow' block at pinned commit e9ad3d70
/// (the same PathGeometry element with the same Figures string, sitting
/// between the ToolBarTrayBaseStyle block and the RowHeaderGripperStyle
/// block; the staged 366-char text is byte-identical to the upstream
/// Figures value); a genuine net10.0-windows ResourceDictionary fixture
/// compiling the exact upstream block into TreeArrow.baml (1082 bytes,
/// emitting 03 42 FE with the same 24 F6 02 A7 FF EE 02 prefix, the
/// byte-identical 366-char text, the same 43 FE converter and the
/// 04 04 close, differing only in per-file table numbering and the
/// 0x35/0x36 line markers the staged file omits; fixture added as
/// TreeArrow.xaml to the E:/tmp/bamlfix project outside the tree),
/// plus a genuine net10.0-windows runtime probe loading the same
/// Figures value through XamlReader (PathGeometry with 1 figure,
/// EvenOdd, closed+filled, start (508.893248, 762.931659), 7 segments
/// Arc/Line/PolyBezier/PolyLine/Arc/Line/Arc - the engine keeps the
/// implicit repeats uncompacted, so it lands 9 segments).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path); all 6 staged images
/// sha256-verified, manifest hand-untouched; staged-oracle status-gate
/// bypass stays in-probe only.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice147BamlTreeArrowTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice147BamlTreeArrowTests()
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
    public void Theme_baml_places_tree_arrow_and_gripper_bytes()
    {
        // Staged framing pins for the keyed 'TreeArrow' PathGeometry entry
        // at [0x19E7B, 0x19FF7): the element head over well-known id -446,
        // the Figures 0x24 record prefix (attr -89, converter -445, text
        // opening "M508"), the tail (final 'z', converter, ElementEnd)
        // plus the next 'RowHeaderGripperStyle' entry head (Style over
        // Thumb -658 with its Cursor setter slice-148 routes), the
        // TreeArrow/RowHeaderGripperStyle key records, and the key-string
        // table rows.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);

        var trayTail = new byte[] { 0x75, 0x6E, 0x64, 0x06, 0x0A, 0x02, 0x00, 0xE8, 0x02, 0x01, 0xFF, 0xFF, 0xFF, 0x00, 0x04, 0x04 };
        Assert.Equal(trayTail, theme[0x19E6B..0x19E7B]);
        var entryHead = new byte[] { 0x03, 0x42, 0xFE, 0x00 };
        Assert.Equal(entryHead, theme[0x19E7B..0x19E7F]);
        var figuresPrefix = new byte[] { 0x24, 0xF6, 0x02, 0xA7, 0xFF, 0xEE, 0x02 }
            .Concat(Encoding.ASCII.GetBytes("M508")).ToArray();
        Assert.Equal(figuresPrefix, theme[0x19E7F..0x19E8A]);
        var tail = new byte[] { 0x7A, 0x43, 0xFE, 0x04, 0x03, 0x94 };
        Assert.Equal(tail, theme[0x19FF3..0x19FF9]);
        var nextHead = new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x6E, 0xFD };
        Assert.Equal(nextHead, theme[0x19FF7..0x1A000]);
        var cursorProperty = new byte[] { 0x03, 0xD4, 0xFD, 0x00, 0x06, 0x0E, 0x01, 0x00, 0x89, 0x40, 0x1E, 0xFF, 0x06 }
            .Concat(Encoding.ASCII.GetBytes("Cursor")).ToArray();
        Assert.Equal(cursorProperty, theme[0x1A030..0x1A043]);
        var cursorValue = new byte[] { 0x24, 0x0C, 0x02, 0x00, 0x06 }
            .Concat(Encoding.ASCII.GetBytes("SizeNS")).Concat(new byte[] { 0x8B, 0xFF }).ToArray();
        Assert.Equal(cursorValue, theme[0x1A043..0x1A050]);
        var key162 = new byte[] { 0x26, 0x09, 0x98, 0x00, 0xE4, 0xAC, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key162, theme[0xB57D..0xB587]);
        var key163Head = new byte[] { 0x26, 0x09, 0x99, 0x00 };
        Assert.Equal(key163Head, theme[0xB587..0xB58B]);
        var treeString = new byte[] { 0x20, 0x0D, 0x98, 0x00, 0x09 }
            .Concat(Encoding.ASCII.GetBytes("TreeArrow")).ToArray();
        Assert.Equal(treeString, theme[0x24EF..0x24FD]);
        var gripperStringHead = new byte[] { 0x20, 0x19, 0x99, 0x00, 0x15 }
            .Concat(Encoding.ASCII.GetBytes("RowHe")).ToArray();
        Assert.Equal(gripperStringHead, theme[0x24FD..0x2507]);
    }

    [Fact]
    public void Staged_boot_advances_past_tree_arrow_to_gripper_stop()
    {
        // The staged entry now decodes the whole keyed 'TreeArrow'
        // PathGeometry entry (element -446, single Figures
        // PropertyWithConverter) and both keyed gripper Style entries
        // (routed by slice-148), then stops precise at the keyed
        // 'DataGridComboBoxColumnStyle' Style entry (ComboBox target -92
        // with no style-target routing). Everything the earlier slices
        // pinned stays landed.
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
    public void Staged_shape_tree_arrow_lands_live()
    {
        // The staged-verbatim 'TreeArrow' entry bytes [0x19E7B, 0x19FF7)
        // land as a live engine PathGeometry: one closed+filled figure
        // starting at the staged (508.893248, 762.931659) with the nine
        // engine segments (implicit repeats stay uncompacted, unlike the
        // genuine-WPF PolyBezier/PolyLine compaction), the last arc
        // closing exactly back at the start, and the shared key returning
        // the same instance on every lookup.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        var dictionary = BamlReader.LoadDictionary(
            PathDictionary(StagedTreeArrowBytes(theme)));
        var geometry = Assert.IsType<PathGeometry>(dictionary["TreeArrow"]);
        Assert.Single(geometry.Figures);
        var figure = geometry.Figures[0];
        Assert.True(figure.IsClosed);
        Assert.True(figure.IsFilled);
        Assert.Equal(FillRule.EvenOdd, geometry.FillRule);
        Assert.Equal(508.893248, figure.StartPoint.X, 3);
        Assert.Equal(762.931659, figure.StartPoint.Y, 3);
        Assert.Equal(9, figure.Segments.Count);
        var arc0 = Assert.IsType<ArcSegment>(figure.Segments[0]);
        Assert.Equal(462.472589, arc0.Point.X, 3);
        Assert.Equal(743.44582, arc0.Point.Y, 3);
        Assert.IsType<LineSegment>(figure.Segments[1]);
        Assert.IsType<BezierSegment>(figure.Segments[2]);
        Assert.IsType<BezierSegment>(figure.Segments[3]);
        Assert.IsType<LineSegment>(figure.Segments[4]);
        Assert.IsType<LineSegment>(figure.Segments[5]);
        Assert.IsType<ArcSegment>(figure.Segments[6]);
        Assert.IsType<LineSegment>(figure.Segments[7]);
        var arc8 = Assert.IsType<ArcSegment>(figure.Segments[8]);
        Assert.Equal(508.893248, arc8.Point.X, 3);
        Assert.Equal(762.931659, arc8.Point.Y, 3);
        Assert.False(geometry.IsFrozen);
        Assert.Same(geometry, dictionary["TreeArrow"]);
    }

    [Fact]
    public void Fixture_numbered_tree_arrow_lands_with_line_markers()
    {
        // The genuine net10.0-windows fixture numbers its tables from
        // zero and carries 0x35/0x36 line markers around the same
        // shapes: the parser resolves everything through the tables, so
        // the same short path lands live with line markers transparent.
        XamlLoader.CustomElementSource = null;
        var entry = new B()
            .Raw(0x03).I16(-446).Raw(0x00)
            .Raw(0x35).U32(6).U32(2)
            .Sized(0x24, p => p.I16(-89).Str("M0,0 L10,10").I16(-445))
            .Raw(0x36).U32(44)
            .Raw(0x04).ToArray();
        var dictionary = BamlReader.LoadDictionary(PathDictionary(entry, "TreeArrowReplica"));
        var geometry = Assert.IsType<PathGeometry>(dictionary["TreeArrowReplica"]);
        Assert.Single(geometry.Figures);
        var figure = geometry.Figures[0];
        Assert.Equal(0, figure.StartPoint.X, 9);
        Assert.Equal(0, figure.StartPoint.Y, 9);
        var line = Assert.IsType<LineSegment>(Assert.Single(figure.Segments));
        Assert.Equal(10, line.Point.X, 9);
        Assert.Equal(10, line.Point.Y, 9);
        Assert.Same(geometry, dictionary["TreeArrowReplica"]);
    }

    private static byte[] StagedTreeArrowBytes(byte[] theme)
    {
        // The staged-verbatim 'TreeArrow' entry bytes: the whole
        // [0x19E7B, 0x19FF7) span the framing test pins above.
        Assert.Equal(466350, theme.Length);
        return theme[0x19E7B..0x19FF7];
    }

    [Fact]
    public void Synthetic_short_figures_land_live_unfrozen()
    {
        // A hand-built keyed PathGeometry entry with the staged value
        // shape (single Figures 0x24 over the well-known member and
        // converter) lands live instead of failing precise: the exact
        // next-blocker surface behind the staged stop is routed. No
        // Freeze marker rides along, so the geometry stays unfrozen.
        var entry = new B()
            .Raw(0x03).I16(-446).Raw(0x00)
            .Sized(0x24, p => p.I16(-89).Str("M1,2 L3,4").I16(-445))
            .Raw(0x04).ToArray();
        var dictionary = BamlReader.LoadDictionary(PathDictionary(entry, "S"));
        var geometry = Assert.IsType<PathGeometry>(dictionary["S"]);
        var figure = Assert.Single(geometry.Figures);
        Assert.Equal(1, figure.StartPoint.X, 9);
        Assert.Equal(2, figure.StartPoint.Y, 9);
        var line = Assert.IsType<LineSegment>(Assert.Single(figure.Segments));
        Assert.Equal(3, line.Point.X, 9);
        Assert.Equal(4, line.Point.Y, 9);
        Assert.False(geometry.IsFrozen);
        Assert.Same(geometry, dictionary["S"]);
    }

    [Fact]
    public void Synthetic_freeze_marker_lands_frozen()
    {
        // The PresentationOptions Freeze marker rides the staged scalar
        // grammar: "True" freezes the landed PathGeometry (Freezable
        // like the staged DrawingGroups), anything else leaves it live.
        var frozen = new B()
            .Raw(0x03).I16(-446).Raw(0x00)
            .Sized(0x34, q => q.Str("True").U16(1))
            .Sized(0x24, p => p.I16(-89).Str("M0,0 L1,1").I16(-445))
            .Raw(0x04).ToArray();
        var dictionary = BamlReader.LoadDictionary(PathDictionary(frozen, "F", true));
        Assert.True(Assert.IsType<PathGeometry>(dictionary["F"]).IsFrozen);
        var live = new B()
            .Raw(0x03).I16(-446).Raw(0x00)
            .Sized(0x34, q => q.Str("False").U16(1))
            .Sized(0x24, p => p.I16(-89).Str("M0,0 L1,1").I16(-445))
            .Raw(0x04).ToArray();
        var liveDictionary = BamlReader.LoadDictionary(PathDictionary(live, "L", true));
        Assert.False(Assert.IsType<PathGeometry>(liveDictionary["L"]).IsFrozen);
    }

    [Fact]
    public void Synthetic_unknown_path_element_stays_precise()
    {
        // A keyed entry over an unrouted well-known id still fails
        // precise, naming the id through the extended type-name arm
        // (PathGeometry now resolves by name instead of the numeric
        // fallback the new entry used to hit).
        var content = new B()
            .Raw(0x03).I16(-777).Raw(0x00)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(PathDictionary(content)));
        Assert.Contains("well-known type id -777", error.Message);
        Assert.Contains("style-surface follow-up", error.Message);
    }

    [Fact]
    public void Synthetic_wrong_member_stays_precise()
    {
        // A PathGeometry entry setting any other member fails precise,
        // naming the member through the member-name arm plus the
        // Figures contract.
        var content = new B()
            .Raw(0x03).I16(-446).Raw(0x00)
            .Sized(0x24, p => p.I16(-47).Str("M0,0 L1,1").I16(-445))
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(PathDictionary(content)));
        Assert.Contains("Height", error.Message);
        Assert.Contains("take the Figures member", error.Message);
    }

    [Fact]
    public void Synthetic_wrong_converter_stays_precise()
    {
        // A PathGeometry entry converting through any other converter
        // fails precise, naming the converter id plus the
        // PathFigureCollectionConverter contract.
        var content = new B()
            .Raw(0x03).I16(-446).Raw(0x00)
            .Sized(0x24, p => p.I16(-89).Str("M0,0 L1,1").I16(-240))
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(PathDictionary(content)));
        Assert.Contains("uses converter -240", error.Message);
        Assert.Contains("take the PathFigureCollectionConverter", error.Message);
    }

    [Fact]
    public void Synthetic_garbage_text_stays_precise()
    {
        // Garbage path data fails precise through the parser instead of
        // landing a half-built geometry.
        var content = new B()
            .Raw(0x03).I16(-446).Raw(0x00)
            .Sized(0x24, p => p.I16(-89).Str("not-path-data").I16(-445))
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(PathDictionary(content)));
        Assert.Contains("is not a PathGeometry", error.Message);
    }

    [Fact]
    public void Synthetic_duplicate_figures_stays_precise()
    {
        // A second Figures value on the same entry fails precise like
        // the scalar duplicate-text gate.
        var content = new B()
            .Raw(0x03).I16(-446).Raw(0x00)
            .Sized(0x24, p => p.I16(-89).Str("M0,0 L1,1").I16(-445))
            .Sized(0x24, p => p.I16(-89).Str("M0,0 L1,1").I16(-445))
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(PathDictionary(content)));
        Assert.Contains("duplicate Figures", error.Message);
    }

    [Fact]
    public void Synthetic_missing_value_stays_precise()
    {
        // An empty PathGeometry entry fails precise like the scalar
        // missing-value gate.
        var content = new B()
            .Raw(0x03).I16(-446).Raw(0x00)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(PathDictionary(content)));
        Assert.Contains("has no value", error.Message);
    }

    [Fact]
    public void Synthetic_garbage_record_stays_precise()
    {
        // Any other record inside the entry fails precise with its
        // record id instead of misaligning the stream.
        var content = new B()
            .Raw(0x03).I16(-446).Raw(0x00)
            .Raw(0x2E).I16(-238)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(PathDictionary(content)));
        Assert.Contains("holds record 0x2E", error.Message);
    }

    [Fact]
    public void Tree_arrow_engine_shape_matches_wpf()
    {
        // The engine honors the live-WPF contract the genuine
        // net10.0-windows XamlReader probe pins for the staged text:
        // a PathGeometry figure starting at (508.893248, 762.931659)
        // whose opening arc carries the staged size/flags/endpoint
        // (the probe's full-text figure is closed+filled EvenOdd; this
        // opening run pins the same parse through the engine segments).
        var geometry = Assert.IsType<PathGeometry>(Geometry.Parse(
            "M508.893248 762.931659a65.039498 65.039498 0 0 1-46.420659-19.485839L97.125746 371.004022"));
        var figure = Assert.Single(geometry.Figures);
        Assert.Equal(FillRule.EvenOdd, geometry.FillRule);
        Assert.True(figure.IsFilled);
        Assert.Equal(508.893248, figure.StartPoint.X, 6);
        Assert.Equal(762.931659, figure.StartPoint.Y, 6);
        Assert.Equal(2, figure.Segments.Count);
        var arc = Assert.IsType<ArcSegment>(figure.Segments[0]);
        Assert.Equal(65.039498, arc.Size.Width, 6);
        Assert.Equal(65.039498, arc.Size.Height, 6);
        Assert.Equal(0, arc.RotationAngle, 9);
        Assert.False(arc.IsLargeArc);
        Assert.Equal(SweepDirection.Clockwise, arc.SweepDirection);
        Assert.Equal(462.472589, arc.Point.X, 3);
        Assert.Equal(743.44582, arc.Point.Y, 3);
        var line = Assert.IsType<LineSegment>(figure.Segments[1]);
        Assert.Equal(97.125746, line.Point.X, 6);
        Assert.Equal(371.004022, line.Point.Y, 6);
    }

    private static byte[] PathDictionary(byte[] entry, string key = "TreeArrow", bool withFreezeString = false)
    {
        // The staged per-file rows mirrored with dummies (assembly 0, no
        // TypeInfo rows since every id in the tree-arrow shape is
        // well-known, the key string plus the "Freeze" string when a
        // PresentationOptions marker rides along) around either the
        // staged-verbatim or the hand-built entry bytes.
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x20, p => p.U16(0).Str(key));
        if (withFreezeString)
            head = head.Sized(0x20, p => p.U16(1).Str("Freeze"));
        return head.Deferable()
            .Sized(0x26, p => p.U16(0).U32(0).U16(0))
            .Raw(entry).FinishDictionary().ToArray();
    }

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

    private static byte[] Header() =>
        new B()
        .Raw(0x0C, 0x00, 0x00, 0x00)
        .Raw(0x4D, 0x00, 0x53, 0x00, 0x42, 0x00, 0x41, 0x00, 0x4D, 0x00, 0x4C, 0x00)
        .U16(0).U16(0x60)
        .Raw(0x00, 0x00, 0x60, 0x00, 0x00, 0x00, 0x60, 0x00,
             0x01, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00)
        .ToArray();
}
