// Copyright (c) 2026 The WPF on WASM Contributors.
// Licensed under the MIT License. See LICENSE in the project root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-160: the keyed 'SearchPathStyle' Style entry at BAML+0x1AFFD
/// (BasedOn StaticResource record 0x38 at BAML+0x1B001 over the per-file
/// Style.BasedOn row 92 plus the short-form Data PropertyCustom and the Data
/// StaticResource value) routes as a live engine Style, so the staged boot
/// advances through the whole fifteen-entry Path style run (SearchPathStyle at
/// 0x1AFFD through LeftPathStyle at 0x1B1F5, each the same 36-byte BasedOn
/// plus Data-static shape) and stops precise at the keyed 'RightPathStyle'
/// Style entry at BAML+0x1B219 (RenderTransformOrigin PWC at BAML+0x1B22E
/// plus the RenderTransform TransformGroup complex, the next slice's work).
/// Live counts verified first: handycontrol conversion-failed/6 staged/2 gaps
/// (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched;
/// staged-oracle status-gate bypass stays in-probe only). Triangulation three
/// ways: staged framing spans against the live 466350-byte theme.baml
/// (sha256 D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0,
/// verified live) - the 36-byte SearchPathStyle entry (Style head
/// 03 94 FD 00 with the BasedOn 0x38 value over per-file row 92 with static
/// id 0, the TargetType PropertyTypeReference 22 00 00 46 FE, the
/// Style.Setters content member 2E 12 FF, the single Setter element with the
/// short-form Data PropertyCustom 06 07 01 00 89 00 A9 FF plus the Data
/// StaticResource value 38 02 00 01 00, closed by the setter/entry 04 04
/// pair), the fourteen identical sibling heads at 0x1B021 through 0x1B1F5,
/// the entry-16 RightPathStyle head at 0x1B219 with its qualified
/// RenderTransformOrigin PropertyCustom plus "0.5, 0.5" PointConverter PWC
/// plus the RenderTransform short-form PropertyCustom and the TransformGroup
/// complex (ScaleTransform/ScaleX "-1", SkewTransform, RotateTransform,
/// TranslateTransform, byte-verified below), the 0x26 keyidx 236-238 records
/// at 0xB799/0xB7AB/0xB7BF each with two 0x37 static declarations (StringInfo
/// 88 'PathBaseStyle' for the BasedOn plus the per-style Geometry name for
/// the Data setter), the key-string rows 236 'SearchPathStyle' at 0x2E2B and
/// 237 'FullScreenReturnPathStyle' plus the AttributeInfo rows 0
/// (Style/TargetType), 92 (Style/BasedOn) and the StringInfo 88/53 rows, the
/// staged 'PathBaseStyle' entry at 0x166FF (123 bytes: TargetType plus the
/// Stretch/SnapsToDevicePixels/FlowDirection setters) and the staged
/// 'SearchGeometry' entry at 0x126A6 (512 bytes: frozen StreamGeometry text
/// through the GeometryConverter); the live upstream HandyControl
/// Themes/Basic/Paths.xaml block at pinned commit e9ad3d70 (PathBaseStyle
/// with Stretch/SnapsToDevicePixels/FlowDirection setters, SearchPathStyle
/// with BasedOn="{StaticResource PathBaseStyle}" plus
/// Data="{StaticResource SearchGeometry}", the same-shape
/// FullScreenReturn/FullScreen/Windows/Save/Download/Enlarge/Reduce/Rotate/
/// Calendar/Delete/Close/Down/Clock/Left run, then RightPathStyle with
/// RenderTransformOrigin "0.5, 0.5" plus the TransformGroup
/// (ScaleTransform ScaleY=1 ScaleX=-1, SkewTransform 0/0, RotateTransform 0,
/// TranslateTransform) plus Data="{StaticResource LeftGeometry}", fetched
/// live) plus the upstream Themes/Basic/Geometries.xaml frozen SearchGeometry
/// entry; a genuine net10.0-windows ResourceDictionary fixture compiling the
/// upstream PathBaseStyle/SearchPathStyle/RightPathStyle block on framework
/// types (Paths.baml: Style head 03 94 FD 00, BasedOn 0x38 over the per-file
/// Style.BasedOn row, TargetType 22 00 00 46 FE, Setters 2E 12 FF, Data
/// PropertyCustom 06 07 01 00 89 00 A9 FF, Data StaticResource
/// 38 02 00 01 00, key-block 0x37 declarations, RenderTransformOrigin
/// qualified PropertyCustom 06 1D 01 00 89 40 4A FD over UIElement-owned row
/// with the PointConverter -474 PWC, RenderTransform short-form member -134,
/// TransformGroup -682 with Children -130 and ScaleTransform -546 ScaleY/ScaleX
/// doubles - byte-identical in every staged record shape modulo per-file
/// numbering and the 0x35/0x36 line markers the staged file omits; fixture
/// retained at E:/tmp/bamlfix160 outside the tree) plus a genuine
/// net10.0-windows reflection probe (Style.TargetType typed Type,
/// Style.BasedOn typed Style defaulting null, Path.DataProperty owned by Path
/// typed Geometry, RenderTransformOrigin carried as a Point CLR property with
/// a RenderTransformOriginProperty DP, ScaleX defaulting 1, Geometry.Parse
/// landing a StreamGeometry; probe retained at E:/tmp/rbprobe160 outside the
/// tree). Real-behavior engine work, never fabricated: BamlReader routes the
/// style BasedOn through a new 0x38 arm on ParseStyleEntry (fixed attr plus
/// key-block static id, validated through the per-file BasedOn row like the
/// shadow Color arm, landing as the engine Style.BasedOnKey for apply-time
/// resolution like the XAML loader text path) and the Data setter value
/// through a new 0x38 arm on ParseStyleSetter (fixed attr plus key-block
/// static id over the Setter.Value row, landing as a deferred
/// "{StaticResource key}" RawValue like the XAML loader text path so apply
/// resolves the live seed with missing keys and genuine mismatches failing
/// loud); the staged boot advances through the whole fifteen-entry run.
/// Re-package skipped by evidence (Convert map untouched, staged bins
/// byte-identical). Tests: 21 new Slice160BamlPathStyleTests (see below)
/// plus the stop-triple flip with new-stop triples, and the BamlReader
/// class-doc frontier rewrite.
/// application-compatibility-status.md handycontrol row verified current
/// against the live manifest (conversion-failed/6/2; no regen, nothing
/// re-packaged). No browser proof - package stays conversion-failed.
/// Precise next blockers: the keyed 'RightPathStyle' Style entry (qualified
/// RenderTransformOrigin PropertyCustom 0x06 at BAML+0x1B22A naming a
/// CLR-only property with no engine dependency property), then the
/// CarouselPateButton/implicit Carousel styles, the GroupBox styles with
/// BasedOn/DynamicResource setters and TitleElement triggers, EventTriggers,
/// the line-info trailer, the per-file (Type,Name) x:Name rows and the
/// converted-lib elements (CircleProgressBar/GifImage/Watermark/Drawer/
/// ElementGroup/SearchBar) behind the line-17 SkinDefault stop.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice160BamlPathStyleTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice160BamlPathStyleTests()
    {
        XamlLoader.CustomElementSource = null;
    }

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    [Fact]
    public void Theme_baml_search_path_entry_bytes()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        Assert.Equal(
            "D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0",
            Convert.ToHexString(SHA256.HashData(theme)));

        // The keyed 'SearchPathStyle' entry is 36 bytes at BAML+0x1AFFD: the
        // Style head (well-known type id -620) with the BasedOn StaticResource
        // (attr 92, static id 0) right behind it at BAML+0x1B001.
        Assert.Equal(36, 0x1B021 - 0x1AFFD);
        Assert.Equal(new byte[] { 0x03, 0x94, 0xFD, 0x00 }, theme[0x1AFFD..0x1B001]);
        Assert.Equal(new byte[] { 0x38, 0x5C, 0x00, 0x00, 0x00 }, theme[0x1B001..0x1B006]);
        // The TargetType PropertyTypeReference (attr 0, Path -442) plus the
        // Style.Setters content member (-238).
        Assert.Equal(new byte[] { 0x22, 0x00, 0x00, 0x46, 0xFE }, theme[0x1B006..0x1B00B]);
        Assert.Equal(new byte[] { 0x2E, 0x12, 0xFF }, theme[0x1B00B..0x1B00E]);
        // The single Setter element (-556) with the short-form Data
        // PropertyCustom (attr 1, serializer 137, member -87) plus the Data
        // StaticResource value (attr 2, static id 1), closed by the setter
        // and entry ElementEnd pair.
        Assert.Equal(new byte[] { 0x03, 0xD4, 0xFD, 0x00 }, theme[0x1B00E..0x1B012]);
        Assert.Equal(new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xA9, 0xFF }, theme[0x1B012..0x1B01A]);
        Assert.Equal(new byte[] { 0x38, 0x02, 0x00, 0x01, 0x00 }, theme[0x1B01A..0x1B01F]);
        Assert.Equal(new byte[] { 0x04, 0x04 }, theme[0x1B01F..0x1B021]);
        // The next entry head is the second Path style at BAML+0x1B021.
        Assert.Equal(new byte[] { 0x03, 0x94, 0xFD, 0x00 }, theme[0x1B021..0x1B025]);
        // The fifteen-entry run: entries 1 through 14 repeat the same 36-byte
        // BasedOn plus Data-static shape (heads at 0x1B021 + 36*k), and the
        // sixteenth entry ('RightPathStyle') starts at BAML+0x1B219.
        var styleHead = new byte[] { 0x03, 0x94, 0xFD, 0x00 };
        for (var k = 1; k <= 14; k++)
            Assert.Equal(styleHead, theme[(0x1AFFD + 36 * k)..(0x1AFFD + 36 * k + 4)]);
        Assert.Equal(36, 0x1B219 - 0x1B1F5);
        Assert.Equal(styleHead, theme[0x1B219..0x1B21D]);
        Assert.Equal(new byte[] { 0x38, 0x5C, 0x00, 0x00, 0x00 }, theme[0x1B21D..0x1B222]);
        // The 'RightPathStyle' entry carries the RenderTransformOrigin setter
        // first: the qualified PropertyCustom (attr 1, serializer 16521,
        // owner UIElement -694, name 'RenderTransformOrigin') at BAML+0x1B22A.
        Assert.Equal(new byte[] { 0x22, 0x00, 0x00, 0x46, 0xFE }, theme[0x1B222..0x1B227]);
        Assert.Equal(new byte[] { 0x2E, 0x12, 0xFF }, theme[0x1B227..0x1B22A]);
        Assert.Equal(new byte[] { 0x03, 0xD4, 0xFD, 0x00 }, theme[0x1B22A..0x1B22E]);
        Assert.Equal(
            new byte[] { 0x06, 0x1D, 0x01, 0x00, 0x89, 0x40, 0x4A, 0xFD },
            theme[0x1B22E..0x1B236]);
        Assert.Equal(Encoding.ASCII.GetBytes("RenderTransformOrigin"), theme[0x1B237..0x1B24C]);
        // The "0.5, 0.5" PointConverter PWC (attr 2, converter -474) plus the
        // setter close, then the RenderTransform setter head with the
        // short-form member -134 and the TransformGroup complex.
        Assert.Equal(new byte[] { 0x24, 0x0E, 0x02, 0x00, 0x08 }, theme[0x1B24C..0x1B251]);
        Assert.Equal(Encoding.ASCII.GetBytes("0.5, 0.5"), theme[0x1B251..0x1B259]);
        Assert.Equal(new byte[] { 0x26, 0xFE, 0x04 }, theme[0x1B259..0x1B25C]);
        Assert.Equal(new byte[] { 0x03, 0xD4, 0xFD, 0x00 }, theme[0x1B25C..0x1B260]);
        Assert.Equal(new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0x7A, 0xFF }, theme[0x1B260..0x1B268]);
        Assert.Equal(new byte[] { 0x07, 0x02, 0x00 }, theme[0x1B268..0x1B26B]);
        Assert.Equal(new byte[] { 0x03, 0x56, 0xFD, 0x00 }, theme[0x1B26B..0x1B26F]);
        Assert.Equal(new byte[] { 0x2E, 0x7E, 0xFF }, theme[0x1B26F..0x1B272]);
        // The ScaleTransform child (-546) with ScaleY "1" and ScaleX "-1"
        // (DoubleConverter -175), closed before the SkewTransform child.
        Assert.Equal(new byte[] { 0x03, 0xDE, 0xFD, 0x00 }, theme[0x1B272..0x1B276]);
        Assert.Equal(new byte[] { 0x24, 0x07, 0x5D, 0x00, 0x01, 0x31, 0x51, 0xFF }, theme[0x1B276..0x1B27E]);
        Assert.Equal(new byte[] { 0x24, 0x08, 0x5E, 0x00, 0x02, 0x2D, 0x31, 0x51, 0xFF }, theme[0x1B27E..0x1B287]);
        Assert.Equal(new byte[] { 0x04 }, theme[0x1B287..0x1B288]);
        Assert.Equal(new byte[] { 0x03, 0xC1, 0xFD, 0x00 }, theme[0x1B288..0x1B28C]);
        Assert.Equal(new byte[] { 0x24, 0x07, 0x5F, 0x00, 0x01, 0x30, 0x51, 0xFF }, theme[0x1B28C..0x1B294]);
        Assert.Equal(new byte[] { 0x24, 0x07, 0x60, 0x00, 0x01, 0x30, 0x51, 0xFF }, theme[0x1B294..0x1B29C]);
        Assert.Equal(new byte[] { 0x04 }, theme[0x1B29C..0x1B29D]);
        Assert.Equal(new byte[] { 0x03, 0xF0, 0xFD, 0x00 }, theme[0x1B29D..0x1B2A1]);
        Assert.Equal(new byte[] { 0x24, 0x07, 0x24, 0x00, 0x01, 0x30, 0x51, 0xFF }, theme[0x1B2A1..0x1B2A9]);
        Assert.Equal(new byte[] { 0x04 }, theme[0x1B2A9..0x1B2AA]);
        Assert.Equal(new byte[] { 0x03, 0x54, 0xFD, 0x00 }, theme[0x1B2AA..0x1B2AE]);
        Assert.Equal(new byte[] { 0x04, 0x04, 0x08, 0x04 }, theme[0x1B2AE..0x1B2B2]);
        // The entry closes with the same Data StaticResource setter shape as
        // the fifteen-entry run (member -87, attr 2, static id 1).
        Assert.Equal(new byte[] { 0x03, 0xD4, 0xFD, 0x00 }, theme[0x1B2B2..0x1B2B6]);
        Assert.Equal(new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xA9, 0xFF }, theme[0x1B2B6..0x1B2BE]);
        Assert.Equal(new byte[] { 0x38, 0x02, 0x00, 0x01, 0x00 }, theme[0x1B2BE..0x1B2C3]);
        Assert.Equal(new byte[] { 0x04, 0x04 }, theme[0x1B2C3..0x1B2C5]);
        // The keyidx-236 key record at 0xB799 carries two key-block statics:
        // 'PathBaseStyle' (StringInfo 88) for the BasedOn plus
        // 'SearchGeometry' (StringInfo 53) for the Data setter.
        Assert.Equal(new byte[] { 0x26, 0x09, 0xEC, 0x00, 0x66, 0xBE, 0x00, 0x00, 0x00, 0x00 }, theme[0xB799..0xB7A3]);
        Assert.Equal(new byte[] { 0x37, 0x00, 0x58, 0x00 }, theme[0xB7A3..0xB7A7]);
        Assert.Equal(new byte[] { 0x37, 0x00, 0x35, 0x00 }, theme[0xB7A7..0xB7AB]);
        // The sibling keyidx-237/238 records carry the same two-declaration
        // shape (base 88 plus the per-style Geometry name).
        Assert.Equal(new byte[] { 0x26, 0x09, 0xED, 0x00, 0x8A, 0xBE, 0x00, 0x00, 0x00, 0x00 }, theme[0xB7AB..0xB7B5]);
        Assert.Equal(new byte[] { 0x37, 0x00, 0x58, 0x00 }, theme[0xB7B5..0xB7B9]);
        Assert.Equal(new byte[] { 0x26, 0x09, 0xEE, 0x00, 0xAE, 0xBE, 0x00, 0x00, 0x00, 0x00 }, theme[0xB7BD..0xB7C7]);
        Assert.Equal(new byte[] { 0x37, 0x00, 0x58, 0x00 }, theme[0xB7C7..0xB7CB]);
        Assert.Equal(new byte[] { 0x37, 0x00, 0x33, 0x00 }, theme[0xB7CB..0xB7CF]);
        // The key-string row 236 'SearchPathStyle' at 0x2E2B plus the
        // AttributeInfo 92 row (owner Style -620, 'BasedOn') at 0x2E40, the
        // AttributeInfo 0 row (owner Style -620, 'TargetType') at 0x1351 and
        // the StringInfo 88/53 rows.
        Assert.Equal(new byte[] { 0x20, 0x13, 0xEC, 0x00, 0x0F }, theme[0x2E2B..0x2E30]);
        Assert.Equal(Encoding.ASCII.GetBytes("SearchPathStyle"), theme[0x2E30..0x2E3F]);
        Assert.Equal(new byte[] { 0x1F, 0x0E, 0x5C, 0x00, 0x94, 0xFD, 0x00, 0x07 }, theme[0x2E3F..0x2E47]);
        Assert.Equal(Encoding.ASCII.GetBytes("BasedOn"), theme[0x2E47..0x2E4E]);
        Assert.Equal(new byte[] { 0x1F, 0x11, 0x00, 0x00, 0x94, 0xFD, 0x00, 0x0A }, theme[0x1351..0x1359]);
        Assert.Equal(Encoding.ASCII.GetBytes("TargetType"), theme[0x1359..0x1363]);
        Assert.Equal(new byte[] { 0x20, 0x11, 0x58, 0x00, 0x0D }, theme[0x133F..0x1344]);
        Assert.Equal(Encoding.ASCII.GetBytes("PathBaseStyle"), theme[0x1344..0x1351]);
        Assert.Equal(new byte[] { 0x20, 0x12, 0x35, 0x00, 0x0E }, theme[0x1060..0x1065]);
        Assert.Equal(Encoding.ASCII.GetBytes("SearchGeometry"), theme[0x1065..0x1073]);
        // The staged 'PathBaseStyle' entry (123 bytes at BAML+0x166FF) and
        // the staged 'SearchGeometry' entry (512 bytes at BAML+0x126A6) seed
        // the staged landing/apply tests below.
        Assert.Equal(new byte[] { 0x03, 0x94, 0xFD, 0x00 }, theme[0x166FF..0x16703]);
        Assert.Equal(123, 0x1677A - 0x166FF);
        Assert.Equal(new byte[] { 0x04, 0x04 }, theme[0x16778..0x1677A]);
        Assert.Equal(new byte[] { 0x03, 0x58, 0xFF, 0x01 }, theme[0x1677A..0x1677E]);
        Assert.Equal(new byte[] { 0x03, 0x13, 0xFF, 0x01 }, theme[0x126A6..0x126AA]);
        Assert.Equal(new byte[] { 0x34, 0x08, 0x04, 0x54, 0x72, 0x75, 0x65, 0x22, 0x00 }, theme[0x126AA..0x126B3]);
        Assert.Equal(512, 0x128A6 - 0x126A6);
        Assert.Equal(new byte[] { 0x10, 0xFF, 0x04 }, theme[0x128A3..0x128A6]);
        Assert.Equal(new byte[] { 0x03, 0x13, 0xFF, 0x01 }, theme[0x128A6..0x128AA]);
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
    public void Staged_boot_advances_past_path_run_to_right_path_style()
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
        Assert.DoesNotContain("'SearchPathStyle'", inner.Message);
        Assert.DoesNotContain("record 0x38 in a Style", inner.Message);
        Assert.DoesNotContain("BAML+0x1B22E", inner.Message);
        Assert.DoesNotContain("'RightPathStyle'", inner.Message);
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
    public void Staged_search_path_style_lands_with_based_on_and_data()
    {
        // The full staged 36-byte entry parses its BasedOn plus the Data
        // StaticResource with the staged numbering (attr 92, static id 0 for
        // the base; attr 2, static id 1 for the geometry member -87). Both
        // values stay deferred like the XAML loader path: the base lands as
        // the BasedOnKey and the Data setter keeps the StaticResource text
        // for apply time.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        var dictionary = BamlReader.LoadDictionary(
            StagedPathDictionary(theme[0x1AFFD..0x1B021]));
        var style = Assert.IsType<Style>(dictionary["SearchPathStyle"]);
        Assert.Same(typeof(Path), style.TargetType);
        Assert.Equal("PathBaseStyle", style.BasedOnKey);
        var setter = Assert.IsType<Setter>(Assert.Single(style.Setters));
        Assert.Same(Path.DataProperty, setter.Property);
        Assert.Equal("{StaticResource SearchGeometry}", setter.RawValue);
        Assert.Same(style, dictionary["SearchPathStyle"]);
    }

    [Fact]
    public void Staged_search_path_applies_with_based_on_geometry()
    {
        // The staged entries are live, not retained bytes: with the staged
        // PathBaseStyle entry plus the staged SearchGeometry entry in the
        // same dictionary, applying the staged SearchPathStyle lands the
        // frozen staged geometry through the deferred StaticResource plus the
        // Uniform stretch inherited through the deferred BasedOn.
        var app = new Application();
        try
        {
            LoadStagedHandyControlEntry();
            var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
            var dictionary = BamlReader.LoadDictionary(StagedPathDictionary(
                theme[0x1AFFD..0x1B021],
                ("PathBaseStyle", theme[0x166FF..0x1677A]),
                ("SearchGeometry", theme[0x126A6..0x128A6])));
            var style = Assert.IsType<Style>(dictionary["SearchPathStyle"]);
            app.Resources.Add("PathBaseStyle", dictionary["PathBaseStyle"]);
            app.Resources.Add("SearchGeometry", dictionary["SearchGeometry"]);
            app.Resources.Add("SearchPathStyle", style);
            var path = new Path();
            Assert.Null(path.Data);
            style.ApplyTo(path);
            var geometry = Assert.IsAssignableFrom<Geometry>(path.Data);
            Assert.True(geometry.IsFrozen);
            Assert.True(geometry.Bounds.Width > 0);
            Assert.Equal(Stretch.Uniform, path.Stretch);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Synthetic_based_on_resolves_from_application_resources()
    {
        // A BasedOn StaticResource with the key seeded in Application
        // resources lands the base style for real: the derived style carries
        // the base key, and applying inherits the base Stretch while the
        // derived Data stays null (no geometry seeded).
        var app = new Application();
        try
        {
            var baseStyle = new Style(typeof(Path));
            baseStyle.Setters.Add(new Setter(Shape.StretchProperty, Stretch.Uniform));
            app.Resources.Add("Base", baseStyle);
            var dictionary = BamlReader.LoadDictionary(
                StyleDictionary([("Derived", StyleBody(basedOnId: 0))], ["Base"]));
            var style = Assert.IsType<Style>(dictionary["Derived"]);
            Assert.Equal("Base", style.BasedOnKey);
            var path = new Path();
            style.ApplyTo(path);
            Assert.Equal(Stretch.Uniform, path.Stretch);
            Assert.Null(path.Data);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Synthetic_based_on_resolves_from_target_resources()
    {
        // A same-file base style resolves without any Application: the base
        // lands on the target's own Resources, which the in-load document
        // order checks before the tree. The derived Data static resolves the
        // same way, so no Application is needed at all.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var dictionary = BamlReader.LoadDictionary(StyleDictionary(
                [("Base", BaseBody(), []),
                 ("Derived", StyleBody(basedOnId: 0, staticId: 1), ["Base", "G"]),
                 ("G", GeometryBody("M 0,0 L 10,10"), [])]));
            var path = new Path();
            path.Resources.Add("Base", dictionary["Base"]);
            path.Resources.Add("G", dictionary["G"]);
            var style = Assert.IsType<Style>(dictionary["Derived"]);
            style.ApplyTo(path);
            Assert.Equal(Stretch.Uniform, path.Stretch);
            Assert.IsAssignableFrom<Geometry>(path.Data);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_missing_based_on_key_apply_stays_precise()
    {
        // StaticResource must resolve at apply: a missing base key fails
        // naming the key instead of landing a baseless style.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var dictionary = BamlReader.LoadDictionary(
                StyleDictionary([("Derived", StyleBody(basedOnId: 0))], ["Base"]));
            var style = Assert.IsType<Style>(dictionary["Derived"]);
            var error = Assert.Throws<InvalidOperationException>(() => style.ApplyTo(new Path()));
            Assert.Contains("'Base'", error.Message);
            Assert.Contains("BasedOn", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_mismatched_based_on_type_apply_stays_precise()
    {
        // A BasedOn key resolving to a non-style (here a Geometry) fails
        // naming the key and the runtime type instead of applying it.
        var app = new Application();
        try
        {
            app.Resources.Add("Base", Geometry.Parse("M 0,0 L 10,10"));
            var dictionary = BamlReader.LoadDictionary(
                StyleDictionary([("Derived", StyleBody(basedOnId: 0))], ["Base"]));
            var style = Assert.IsType<Style>(dictionary["Derived"]);
            var error = Assert.Throws<InvalidOperationException>(() => style.ApplyTo(new Path()));
            Assert.Contains("'Base'", error.Message);
            Assert.Contains("BasedOn", error.Message);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Synthetic_duplicate_based_on_stays_precise()
    {
        // A second BasedOn (here the same base twice) fails naming the
        // duplicate instead of replacing the first.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(-620).Raw(0x00)
                .Raw(new B().Raw(0x38).I16(3).U16(0).ToArray())
                .Raw(new B().Raw(0x38).I16(3).U16(0).ToArray())
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                StyleDictionary([("S", body)], ["Base"])));
            Assert.Contains("duplicate BasedOn", error.Message);
            Assert.Contains("'S'", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_bad_based_on_static_id_stays_precise()
    {
        // A BasedOn naming a static id the key block never declared (here id
        // 7 with no 0x37 declarations) fails naming the index instead of
        // landing a baseless style.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(-620).Raw(0x00)
                .Raw(new B().Raw(0x38).I16(3).U16(7).ToArray())
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                StyleDictionary([("S", body)], [])));
            Assert.Contains("StaticResource id 7 in style entry 'S' has no StaticResource index", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_data_static_resolves_geometry_live()
    {
        // A Data StaticResource with the key seeded in Application resources
        // lands the live seed for real (not defaults): the deferred RawValue
        // resolves at apply, so swapping the seed repaints the next apply.
        var app = new Application();
        try
        {
            app.Resources.Add("G", Geometry.Parse("M 0,0 L 10,10"));
            var dictionary = BamlReader.LoadDictionary(
                StyleDictionary([("S", StyleBody(staticId: 0))], ["G"]));
            var style = Assert.IsType<Style>(dictionary["S"]);
            var setter = Assert.IsType<Setter>(Assert.Single(style.Setters));
            Assert.Equal("{StaticResource G}", setter.RawValue);
            var first = new Path();
            style.ApplyTo(first);
            var firstGeometry = Assert.IsAssignableFrom<Geometry>(first.Data);
            Assert.True(firstGeometry.Bounds.Width > 0);
            app.Resources["G"] = Geometry.Parse("M 0,0 L 20,20");
            var second = new Path();
            style.ApplyTo(second);
            var secondGeometry = Assert.IsAssignableFrom<Geometry>(second.Data);
            Assert.True(secondGeometry.Bounds.Width > firstGeometry.Bounds.Width);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Synthetic_data_static_resolves_from_target_resources()
    {
        // A same-file Geometry entry resolves the Data static without any
        // Application: the geometry lands on the target's own Resources and
        // the deferred setter value finds it through the in-load order.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var dictionary = BamlReader.LoadDictionary(StyleDictionary(
                [("S", StyleBody(staticId: 0), ["G"]),
                 ("G", GeometryBody("M 0,0 L 10,10"), [])]));
            var path = new Path();
            path.Resources.Add("G", dictionary["G"]);
            var style = Assert.IsType<Style>(dictionary["S"]);
            style.ApplyTo(path);
            Assert.IsAssignableFrom<Geometry>(path.Data);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_missing_data_static_key_apply_stays_precise()
    {
        // Unlike DynamicResource (which stays unset when missing),
        // StaticResource must resolve at apply: a missing Data key fails
        // naming the key and the setter instead of landing a null Data.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var dictionary = BamlReader.LoadDictionary(
                StyleDictionary([("S", StyleBody(staticId: 0))], ["G"]));
            var style = Assert.IsType<Style>(dictionary["S"]);
            var error = Assert.Throws<InvalidOperationException>(() => style.ApplyTo(new Path()));
            Assert.Contains("'G'", error.Message);
            Assert.Contains("was not found", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_mismatched_data_static_type_apply_stays_precise()
    {
        // A Data StaticResource resolving to a non-geometry (here an Int32)
        // fails naming the value and the setter instead of landing it.
        var app = new Application();
        try
        {
            app.Resources.Add("G", 42);
            var dictionary = BamlReader.LoadDictionary(
                StyleDictionary([("S", StyleBody(staticId: 0))], ["G"]));
            var style = Assert.IsType<Style>(dictionary["S"]);
            var error = Assert.Throws<InvalidOperationException>(() => style.ApplyTo(new Path()));
            Assert.Contains("Int32", error.Message);
            Assert.Contains("not Geometry", error.Message);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Synthetic_duplicate_data_value_stays_precise()
    {
        // A second Data value (here a converted literal behind the
        // StaticResource) fails naming the duplicate instead of replacing it.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(-620).Raw(0x00)
                .Raw(0x22).I16(0).I16(-442)
                .Raw(0x2E).I16(-238)
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, p => p.I16(1).U16(137).I16(-87))
                .Raw(new B().Raw(0x38).I16(2).U16(0).ToArray())
                .Sized(0x24, p => p.I16(2).Str("M 0,0 L 1,1").I16(-240))
                .Raw(0x04)
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                StyleDictionary([("S", body)], ["G"])));
            Assert.Contains("duplicate Value on setter 'Data'", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_data_static_wrong_attribute_stays_precise()
    {
        // A StaticResource over any attribute but the Setter.Value row (here
        // the TargetType row) fails naming the attribute instead of landing
        // on Data.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(-620).Raw(0x00)
                .Raw(0x22).I16(0).I16(-442)
                .Raw(0x2E).I16(-238)
                .Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, p => p.I16(1).U16(137).I16(-87))
                .Raw(new B().Raw(0x38).I16(0).U16(0).ToArray())
                .Raw(0x04)
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                StyleDictionary([("S", body)], ["G"])));
            Assert.Contains("attribute 'TargetType'", error.Message);
            Assert.Contains("styles take Value", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_static_before_property_stays_precise()
    {
        // A StaticResource before any Property fails naming the order instead
        // of landing on an unknown setter (BAML emits Property first).
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(-620).Raw(0x00)
                .Raw(0x22).I16(0).I16(-442)
                .Raw(0x2E).I16(-238)
                .Raw(0x03).I16(-556).Raw(0x00)
                .Raw(new B().Raw(0x38).I16(2).U16(0).ToArray())
                .Raw(0x04)
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                StyleDictionary([("S", body)], ["G"])));
            Assert.Contains("Value before Property", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Style_engine_shape_matches_wpf()
    {
        // The routed values match the genuine net10.0-windows shape: Style
        // carries the TargetType Type plus the BasedOn Style (both defaulting
        // null, matched by the engine), Path.DataProperty is owned by Path
        // and typed Geometry, RenderTransformOrigin is a Point CLR property
        // on the engine UIElement backed by the genuine RenderTransformOriginProperty
        // DP (owner UIElement, typed Point, defaulting 0,0 like net10, which the
        // qualified RightPathStyle setter resolves), and path-data text parses
        // to a live geometry.
        Assert.Same(typeof(Type), typeof(Style).GetProperty("TargetType")!.PropertyType);
        Assert.Same(typeof(Style), typeof(Style).GetProperty("BasedOn")!.PropertyType);
        Assert.Same(typeof(Path), Path.DataProperty.OwnerType);
        Assert.Same(typeof(Geometry), Path.DataProperty.PropertyType);
        Assert.Same(typeof(Point), typeof(UIElement).GetProperty("RenderTransformOrigin")!.PropertyType);
        var originProperty = Assert.IsType<DependencyProperty>(typeof(UIElement).GetField(
            "RenderTransformOriginProperty",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)!.GetValue(null));
        Assert.Same(typeof(UIElement), originProperty.OwnerType);
        Assert.Same(typeof(Point), originProperty.PropertyType);
        var geometry = Geometry.Parse("M 0,0 L 10,10");
        Assert.True(geometry.Bounds.Width > 0);
        var style = new Style(typeof(Path));
        Assert.Null(style.BasedOn);
        Assert.Same(typeof(Path), style.TargetType);
    }

    [Fact]
    public void Synthetic_non_shared_style_stays_precise()
    {
        // Non-shared style keys stay a precise boundary: the staged run is
        // shared throughout, so a non-shared BasedOn entry fails naming the
        // entry instead of landing a half-wired style.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var head = StyleTables(["S", "Base"], []);
            head.Deferable();
            head.Sized(0x26, p => p.U16(0).U32(0).Raw(0x00, 0x01));
            head.Raw(0x37, 0x00).U16(1);
            var body = new B().Raw(0x03).I16(-620).Raw(0x00)
                .Raw(0x22).I16(0).I16(-442)
                .Raw(new B().Raw(0x38).I16(3).U16(0).ToArray())
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                head.Raw(body.ToArray()).FinishDictionary().ToArray()));
            Assert.Contains("non-shared", error.Message);
            Assert.Contains("'S'", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    /// <summary>Builds a single-deferable dictionary with the Style
    /// AttributeInfo rows (TargetType 0, Property 1, Value 2, BasedOn 3) plus
    /// optional per-file TypeInfo rows. Each entry carries its own key-block
    /// statics (the 0x37 OptimizedStaticResource declarations ride the key
    /// like the staged file). Entry offsets are content-relative like the
    /// staged file.</summary>
    private static byte[] StyleDictionary((string Key, byte[] Entry, string[] Statics)[] entries)
    {
        var strings = new List<string>();
        foreach (var (key, _, statics) in entries)
        {
            if (!strings.Contains(key)) strings.Add(key);
            foreach (var name in statics)
                if (!strings.Contains(name)) strings.Add(name);
        }
        var head = StyleTables(strings.ToArray(), []);
        var positions = new List<int>();
        var content = new B();
        foreach (var (_, body, _) in entries)
        {
            positions.Add(content.Position);
            content.Raw(body);
        }
        head.Deferable();
        for (var i = 0; i < entries.Length; i++)
        {
            var position = positions[i];
            head.Sized(0x26, p => p.U16((ushort)strings.IndexOf(entries[i].Key)).U32((uint)position).U16(0));
            foreach (var name in entries[i].Statics)
                head.Raw(0x37, 0x00).U16((ushort)strings.IndexOf(name));
        }
        return head.Raw(content.ToArray()).FinishDictionary().ToArray();
    }

    private static byte[] StyleDictionary((string Key, byte[] Entry)[] entries, string[] staticNames) =>
        StyleDictionary(entries.Select(e => (e.Key, e.Entry, staticNames)).ToArray());

    /// <summary>Builds the staged-numbering dictionary for the sliced
    /// SearchPathStyle bytes (AttributeInfo 0/1/2/92 plus StringInfo 0/1/2
    /// for the entry key and its two static names, with the two 0x37
    /// declarations riding the Search key like the staged file), plus
    /// optional extra shared entries.</summary>
    private static byte[] StagedPathDictionary(byte[] searchEntry, params (string Key, byte[] Entry)[] extras)
    {
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x1F, p => p.I16(92).I16(-620).Raw(0x00).Str("BasedOn"))
            .Sized(0x20, p => p.U16(0).Str("SearchPathStyle"))
            .Sized(0x20, p => p.U16(1).Str("PathBaseStyle"))
            .Sized(0x20, p => p.U16(2).Str("SearchGeometry"));
        for (var i = 0; i < extras.Length; i++)
            head.Sized(0x20, p => p.U16((ushort)(3 + i)).Str(extras[i].Key));
        var content = new B();
        var positions = new List<int> { 0 };
        content.Raw(searchEntry);
        foreach (var (_, body) in extras)
        {
            positions.Add(content.Position);
            content.Raw(body);
        }
        head.Deferable();
        head.Sized(0x26, p => p.U16(0).U32((uint)positions[0]).U16(0));
        head.Raw(0x37, 0x00).U16(1);
        head.Raw(0x37, 0x00).U16(2);
        for (var i = 0; i < extras.Length; i++)
            head.Sized(0x26, p => p.U16((ushort)(3 + i)).U32((uint)positions[1 + i]).U16(0));
        return head.Raw(content.ToArray()).FinishDictionary().ToArray();
    }

    /// <summary>Builds a Path-targeted style body with the staged record
    /// order (head, optional BasedOn 0x38, TargetType, Setters content, one
    /// Data setter with a StaticResource or converted-literal value).</summary>
    private static byte[] StyleBody(ushort? basedOnId = null, ushort? staticId = null, string? dataText = null)
    {
        var body = new B().Raw(0x03).I16(-620).Raw(0x00);
        if (basedOnId.HasValue)
            body.Raw(new B().Raw(0x38).I16(3).U16(basedOnId.Value).ToArray());
        body.Raw(0x22).I16(0).I16(-442);
        body.Raw(0x2E).I16(-238);
        if (staticId.HasValue || dataText is not null)
        {
            body.Raw(0x03).I16(-556).Raw(0x00)
                .Sized(0x06, p => p.I16(1).U16(137).I16(-87));
            if (staticId.HasValue)
                body.Raw(new B().Raw(0x38).I16(2).U16(staticId.Value).ToArray());
            else
                body.Sized(0x24, p => p.I16(2).Str(dataText!).I16(-240));
            body.Raw(0x04);
        }
        return body.Raw(0x04).ToArray();
    }

    /// <summary>Builds a Path-targeted base body with one converted Stretch
    /// setter (the staged PathBaseStyle Uniform shape, qualified owner).</summary>
    private static byte[] BaseBody() => new B().Raw(0x03).I16(-620).Raw(0x00)
        .Raw(0x22).I16(0).I16(-442)
        .Raw(0x2E).I16(-238)
        .Raw(0x03).I16(-556).Raw(0x00)
        .Sized(0x06, p => p.I16(1).U16(16521).I16(-558).Str("Stretch"))
        .Sized(0x24, p => p.I16(2).Str("Uniform").I16(-195))
        .Raw(0x04)
        .Raw(0x04).ToArray();

    /// <summary>Builds a frozen scalar Geometry entry (the staged
    /// SearchGeometry shape: Geometry head with flags 1, Freeze marker, one
    /// TextWithConverter through the GeometryConverter).</summary>
    private static byte[] GeometryBody(string text) => new B().Raw(0x03).I16(-237).Raw(0x01)
        .Sized(0x34, p => p.Str("True").U16(0))
        .Sized(0x11, p => p.Str(text).I16(-240))
        .Raw(0x04).ToArray();

    private static B StyleTables(string[] names, string[] types)
    {
        // Per-file ids stand in for the staged rows (AttributeInfo 0/1/2/3
        // are the Style/Setter TargetType/Property/Value/BasedOn rows).
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x1F, p => p.I16(3).I16(-620).Raw(0x00).Str("BasedOn"));
        foreach (var name in names)
            head.Sized(0x20, p => p.U16((ushort)Array.IndexOf(names, name)).Str(name));
        _ = types;
        return head;
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
                File.ReadAllBytes(System.IO.Path.Combine(apps, "app-manifest.json")));
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
