// Copyright (c) 2026 The WPF on WASM Contributors.
// Licensed under the MIT License. See LICENSE in the project root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-159: the five keyed 'EffectShadow1' through 'EffectShadow5'
/// DropShadowEffect entries at BAML+0x1AE8D (per-file TypeInfo 63 naming the
/// framework System.Windows.Media.Effects.DropShadowEffect over
/// PresentationCore 10.0.0.0) route as live engine effects, so the staged
/// boot stops precise at the keyed 'SearchPathStyle' Style entry at
/// BAML+0x1AFFD (its BasedOn StaticResource record 0x38 at BAML+0x1B001,
/// the next slice's work).
/// Live counts verified first: handycontrol conversion-failed/6 staged/2 gaps
/// (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched;
/// staged-oracle status-gate bypass stays in-probe only). Triangulation three
/// ways: staged framing spans against the live 466350-byte theme.baml
/// (sha256 D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0,
/// verified live) - the 72-byte EffectShadow1 entry (DropShadowEffect head
/// 03 3F 00 00 with its Freeze marker, the BlurRadius "5"/ShadowDepth "1"/
/// Direction "270"/Opacity ".2" DoubleConverter PWCs over the per-file effect
/// rows 86/87/88/90 with converter -175, the Color StaticResource fixed
/// 0x38 value over per-file row 89 with static id 0, the RenderingBias
/// "Performance" EnumConverter PWC over row 91 with converter -195, the 04
/// close), the four sibling entries (Shadow2 8/1.5 at 0x1AED5, Shadow3
/// 14/4.5 at 0x1AF1F, Shadow4 25/8 at 0x1AF6A, Shadow5 35/13 at 0x1AFB3, all
/// with Direction 270, static Color id 0, Opacity ".2", Performance), the
/// 0x26 keyidx 231-235 records at 0xB753/0xB761/0xB76F/0xB77D/0xB78B each with
/// the 37 00 1D 00 OptimizedStaticResource declaration, the key-string rows
/// 231 'EffectShadow1' at 0x2D6A plus 232-235 and 236 'SearchPathStyle' at
/// 0x2E2B, the AttributeInfo 86-91 rows (owner 63) plus the BasedOn row 92
/// (owner Style -108), the TypeInfo 63 row naming the framework effect over
/// PresentationCore, the earlier 'EffectShadowColor' Color entry at 0xF228
/// (#88000000 TextWithConverter) with its keyidx-29 record at 0xAFF4, plus
/// the next 'SearchPathStyle' Style head 03 94 FD 00 at 0x1AFFD with its
/// 0x38 BasedOn static at 0x1B001; the live upstream HandyControl
/// Themes/Basic/Effects.xaml block at pinned commit e9ad3d70 (frozen
/// EffectShadow1-5 over StaticResource EffectShadowColor with
/// BlurRadius/ShadowDepth 5/1, 8/1.5, 14/4.5, 25/8, 35/13, Direction 270,
/// Opacity .2, RenderingBias Performance, heading the five-shadow run, plus
/// the EffectShadowColor #88000000 entry); a genuine net10.0-windows
/// ResourceDictionary fixture compiling a frozen StaticResource-color shadow
/// plus a literal-color unfrozen control on framework types (Shadows.baml:
/// per-file TypeInfo 0 over the same framework effect name, Freeze
/// PresentationOptions, double PWCs with the DoubleConverter -175,
/// RenderingBias PWC with the EnumConverter -195, fixed 5-byte 0x38
/// StaticResource with the key-block 0x37 declaration, literal Color PWC
/// with the ColorConverter -87 - byte-identical in every staged record shape
/// modulo per-file numbering and the 0x35/0x36 line markers the staged file
/// omits; fixture retained at E:/tmp/bamlfix159 outside the tree) plus a
/// genuine net10.0-windows reflection probe (DropShadowEffect
/// BlurRadius/ShadowDepth/Direction/Opacity/Color/RenderingBias DPs owned by
/// DropShadowEffect typed double/double/double/Color/double/RenderingBias
/// defaulting 5/5/315/#FF000000/1/Performance, RenderingBias
/// Performance=0/Quality=1, Freezable honoring Freeze; probe retained at
/// E:/tmp/rbprobe159 outside the tree). Real-behavior engine work, never
/// fabricated: BamlReader routes the framework effect name through
/// ParseDropShadowEffectEntry with the 0x34 Freeze arm (the effect freezes
/// like the staged o:Freeze="True"), the four double arms with duplicate
/// detection, the RenderingBias enum arm, the literal ColorConverter arm,
/// and the 0x38 StaticResource Color arm resolving now against the
/// dictionary-so-far plus Application resources like the XAML loader
/// resource-entry path with StaticResource must-resolve semantics (missing
/// keys and genuine mismatches fail loud); validated bags replay through a
/// factory for non-shared keys so every lookup resolves the live seed; the
/// staged boot advances through the whole five-shadow run. Re-package
/// skipped by evidence (Convert map untouched, staged bins byte-identical).
/// Tests: 23 new Slice159BamlShadowTests (see below) plus the stop-triple
/// flip with new-stop triples, and the BamlReader class-doc frontier
/// rewrite.
/// application-compatibility-status.md handycontrol row verified current
/// against the live manifest (conversion-failed/6/2; no regen, nothing
/// re-packaged). No browser proof - package stays conversion-failed.
/// Precise next blockers: the keyed 'SearchPathStyle' Style entry (BasedOn
/// StaticResource 0x38 at BAML+0x1B001), then the CarouselPateButton/implicit
/// Carousel styles, the GroupBox styles with BasedOn/DynamicResource setters
/// and TitleElement triggers, EventTriggers, the line-info trailer, the
/// per-file (Type,Name) x:Name rows and the converted-lib elements
/// (CircleProgressBar/GifImage/Watermark/Drawer/ElementGroup/SearchBar)
/// behind the line-17 SkinDefault stop.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice159BamlShadowTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice159BamlShadowTests()
    {
        XamlLoader.CustomElementSource = null;
    }

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    [Fact]
    public void Theme_baml_shadow_entries_bytes()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        Assert.Equal(
            "D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0",
            Convert.ToHexString(SHA256.HashData(theme)));

        // The keyed 'EffectShadow1' entry is 72 bytes at BAML+0x1AE8D: the
        // DropShadowEffect head (per-file TypeInfo 63, flags 00) with its
        // Freeze marker and the BlurRadius "5" DoubleConverter PWC.
        Assert.Equal(72, 0x1AED5 - 0x1AE8D);
        var effectHead = new byte[] { 0x03, 0x3F, 0x00, 0x00 };
        Assert.Equal(effectHead, theme[0x1AE8D..0x1AE91]);
        var freeze = new byte[] { 0x34, 0x08, 0x04, 0x54, 0x72, 0x75, 0x65, 0x22, 0x00 };
        Assert.Equal(freeze, theme[0x1AE91..0x1AE9A]);
        Assert.Equal(Encoding.ASCII.GetBytes("True"), theme[0x1AE94..0x1AE98]);
        var blurRadius = new byte[] { 0x24, 0x07, 0x56, 0x00, 0x01, 0x35, 0x51, 0xFF };
        Assert.Equal(blurRadius, theme[0x1AE9A..0x1AEA2]);
        // ShadowDepth "1" plus Direction "270" (both DoubleConverter -175).
        var shadowDepth = new byte[] { 0x24, 0x07, 0x57, 0x00, 0x01, 0x31, 0x51, 0xFF };
        Assert.Equal(shadowDepth, theme[0x1AEA2..0x1AEAA]);
        var direction = new byte[] { 0x24, 0x09, 0x58, 0x00, 0x03, 0x32, 0x37, 0x30, 0x51, 0xFF };
        Assert.Equal(direction, theme[0x1AEAA..0x1AEB4]);
        // The Color StaticResource: fixed 0x38 over per-file attr 89 with
        // static id 0 (indexing the key-block 0x37 declaration), byte-identical
        // to the genuine fixture modulo per-file numbering.
        var staticColor = new byte[] { 0x38, 0x59, 0x00, 0x00, 0x00 };
        Assert.Equal(staticColor, theme[0x1AEB4..0x1AEB9]);
        // Opacity ".2" plus RenderingBias "Performance"/EnumConverter -195
        // plus the entry close.
        var opacity = new byte[] { 0x24, 0x08, 0x5A, 0x00, 0x02, 0x2E, 0x32, 0x51, 0xFF };
        Assert.Equal(opacity, theme[0x1AEB9..0x1AEC2]);
        var bias = new byte[] { 0x24, 0x11, 0x5B, 0x00, 0x0B, 0x50, 0x65, 0x72, 0x66, 0x6F, 0x72, 0x6D, 0x61, 0x6E, 0x63, 0x65, 0x3D, 0xFF };
        Assert.Equal(bias, theme[0x1AEC2..0x1AED4]);
        Assert.Equal(Encoding.ASCII.GetBytes("Performance"), theme[0x1AEC7..0x1AED2]);
        Assert.Equal(new byte[] { 0x04 }, theme[0x1AED4..0x1AED5]);
        // The four sibling heads plus their distinguishing BlurRadius and
        // ShadowDepth PWCs (Direction/static/Opacity/Bias/close repeat the
        // Shadow1 shape verbatim in every entry).
        Assert.Equal(74, 0x1AF1F - 0x1AED5);
        Assert.Equal(effectHead, theme[0x1AED5..0x1AED9]);
        Assert.Equal(freeze, theme[0x1AED9..0x1AEE2]);
        Assert.Equal(new byte[] { 0x24, 0x07, 0x56, 0x00, 0x01, 0x38, 0x51, 0xFF }, theme[0x1AEE2..0x1AEEA]);
        Assert.Equal(new byte[] { 0x24, 0x09, 0x57, 0x00, 0x03, 0x31, 0x2E, 0x35, 0x51, 0xFF }, theme[0x1AEEA..0x1AEF4]);
        Assert.Equal(75, 0x1AF6A - 0x1AF1F);
        Assert.Equal(effectHead, theme[0x1AF1F..0x1AF23]);
        Assert.Equal(freeze, theme[0x1AF23..0x1AF2C]);
        Assert.Equal(new byte[] { 0x24, 0x08, 0x56, 0x00, 0x02, 0x31, 0x34, 0x51, 0xFF }, theme[0x1AF2C..0x1AF35]);
        Assert.Equal(new byte[] { 0x24, 0x09, 0x57, 0x00, 0x03, 0x34, 0x2E, 0x35, 0x51, 0xFF }, theme[0x1AF35..0x1AF3F]);
        Assert.Equal(73, 0x1AFB3 - 0x1AF6A);
        Assert.Equal(effectHead, theme[0x1AF6A..0x1AF6E]);
        Assert.Equal(freeze, theme[0x1AF6E..0x1AF77]);
        Assert.Equal(new byte[] { 0x24, 0x08, 0x56, 0x00, 0x02, 0x32, 0x35, 0x51, 0xFF }, theme[0x1AF77..0x1AF80]);
        Assert.Equal(new byte[] { 0x24, 0x07, 0x57, 0x00, 0x01, 0x38, 0x51, 0xFF }, theme[0x1AF80..0x1AF88]);
        Assert.Equal(74, 0x1AFFD - 0x1AFB3);
        Assert.Equal(effectHead, theme[0x1AFB3..0x1AFB7]);
        Assert.Equal(freeze, theme[0x1AFB7..0x1AFC0]);
        Assert.Equal(new byte[] { 0x24, 0x08, 0x56, 0x00, 0x02, 0x33, 0x35, 0x51, 0xFF }, theme[0x1AFC0..0x1AFC9]);
        Assert.Equal(new byte[] { 0x24, 0x08, 0x57, 0x00, 0x02, 0x31, 0x33, 0x51, 0xFF }, theme[0x1AFC9..0x1AFD2]);
        Assert.Equal(new byte[] { 0x04 }, theme[0x1AFFC..0x1AFFD]);
        // The next entry head is the keyed 'SearchPathStyle' Style
        // (well-known type id -108) with the BasedOn StaticResource right
        // behind it at BAML+0x1B001.
        var styleHead = new byte[] { 0x03, 0x94, 0xFD, 0x00 };
        Assert.Equal(styleHead, theme[0x1AFFD..0x1B001]);
        var basedOnStatic = new byte[] { 0x38, 0x5C, 0x00, 0x00, 0x00 };
        Assert.Equal(basedOnStatic, theme[0x1B001..0x1B006]);
        // The 0x26 keyidx 231-235 records plus the 0x37 static declaration
        // (flags 0, StringInfo key 29 'EffectShadowColor') riding each key.
        var effectKey1 = new byte[] { 0x26, 0x09, 0xE7, 0x00, 0xF6, 0xBC, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(effectKey1, theme[0xB753..0xB75D]);
        var staticDecl = new byte[] { 0x37, 0x00, 0x1D, 0x00 };
        Assert.Equal(staticDecl, theme[0xB75D..0xB761]);
        Assert.Equal(new byte[] { 0x26, 0x09, 0xE8, 0x00, 0x3E, 0xBD, 0x00, 0x00, 0x00, 0x00 }, theme[0xB761..0xB76B]);
        Assert.Equal(staticDecl, theme[0xB76B..0xB76F]);
        Assert.Equal(new byte[] { 0x26, 0x09, 0xE9, 0x00, 0x88, 0xBD, 0x00, 0x00, 0x00, 0x00 }, theme[0xB76F..0xB779]);
        Assert.Equal(staticDecl, theme[0xB779..0xB77D]);
        Assert.Equal(new byte[] { 0x26, 0x09, 0xEA, 0x00, 0xD3, 0xBD, 0x00, 0x00, 0x00, 0x00 }, theme[0xB77D..0xB787]);
        Assert.Equal(staticDecl, theme[0xB787..0xB78B]);
        Assert.Equal(new byte[] { 0x26, 0x09, 0xEB, 0x00, 0x1C, 0xBE, 0x00, 0x00, 0x00, 0x00 }, theme[0xB78B..0xB795]);
        Assert.Equal(staticDecl, theme[0xB795..0xB799]);
        Assert.Equal(new byte[] { 0x26, 0x09, 0xEC, 0x00, 0x66, 0xBE, 0x00, 0x00, 0x00, 0x00 }, theme[0xB799..0xB7A3]);
        // The key-string rows 231-236 plus the AttributeInfo 86-91 rows
        // (owner per-file TypeInfo 63) plus the BasedOn row 92 (owner -108).
        var effectKeyRow = new byte[] { 0x20, 0x11, 0xE7, 0x00, 0x0D };
        Assert.Equal(effectKeyRow, theme[0x2D6A..0x2D6F]);
        Assert.Equal(Encoding.ASCII.GetBytes("EffectShadow1"), theme[0x2D6F..0x2D7C]);
        var blurRadiusRow = new byte[] { 0x1F, 0x11, 0x56, 0x00, 0x3F, 0x00, 0x00, 0x0A };
        Assert.Equal(blurRadiusRow, theme[0x2D7C..0x2D84]);
        Assert.Equal(Encoding.ASCII.GetBytes("BlurRadius"), theme[0x2D84..0x2D8E]);
        var shadowDepthRow = new byte[] { 0x1F, 0x12, 0x57, 0x00, 0x3F, 0x00, 0x00, 0x0B };
        Assert.Equal(shadowDepthRow, theme[0x2D8E..0x2D96]);
        Assert.Equal(Encoding.ASCII.GetBytes("ShadowDepth"), theme[0x2D96..0x2DA1]);
        var directionRow = new byte[] { 0x1F, 0x10, 0x58, 0x00, 0x3F, 0x00, 0x00, 0x09 };
        Assert.Equal(directionRow, theme[0x2DA1..0x2DA9]);
        Assert.Equal(Encoding.ASCII.GetBytes("Direction"), theme[0x2DA9..0x2DB2]);
        var colorRow = new byte[] { 0x1F, 0x0C, 0x59, 0x00, 0x3F, 0x00, 0x00, 0x05 };
        Assert.Equal(colorRow, theme[0x2DB2..0x2DBA]);
        Assert.Equal(Encoding.ASCII.GetBytes("Color"), theme[0x2DBA..0x2DBF]);
        var opacityRow = new byte[] { 0x1F, 0x0E, 0x5A, 0x00, 0x3F, 0x00, 0x00, 0x07 };
        Assert.Equal(opacityRow, theme[0x2DBF..0x2DC7]);
        Assert.Equal(Encoding.ASCII.GetBytes("Opacity"), theme[0x2DC7..0x2DCE]);
        var biasRow = new byte[] { 0x1F, 0x14, 0x5B, 0x00, 0x3F, 0x00, 0x00, 0x0D };
        Assert.Equal(biasRow, theme[0x2DCE..0x2DD6]);
        Assert.Equal(Encoding.ASCII.GetBytes("RenderingBias"), theme[0x2DD6..0x2DE3]);
        Assert.Equal(new byte[] { 0x20, 0x11, 0xE8, 0x00, 0x0D }, theme[0x2DE3..0x2DE8]);
        Assert.Equal(Encoding.ASCII.GetBytes("EffectShadow2"), theme[0x2DE8..0x2DF5]);
        Assert.Equal(new byte[] { 0x20, 0x11, 0xE9, 0x00, 0x0D }, theme[0x2DF5..0x2DFA]);
        Assert.Equal(Encoding.ASCII.GetBytes("EffectShadow3"), theme[0x2DFA..0x2E07]);
        Assert.Equal(new byte[] { 0x20, 0x11, 0xEA, 0x00, 0x0D }, theme[0x2E07..0x2E0C]);
        Assert.Equal(Encoding.ASCII.GetBytes("EffectShadow4"), theme[0x2E0C..0x2E19]);
        Assert.Equal(new byte[] { 0x20, 0x11, 0xEB, 0x00, 0x0D }, theme[0x2E19..0x2E1E]);
        Assert.Equal(Encoding.ASCII.GetBytes("EffectShadow5"), theme[0x2E1E..0x2E2B]);
        Assert.Equal(new byte[] { 0x20, 0x13, 0xEC, 0x00, 0x0F }, theme[0x2E2B..0x2E30]);
        Assert.Equal(Encoding.ASCII.GetBytes("SearchPathStyle"), theme[0x2E30..0x2E3F]);
        var basedOnRow = new byte[] { 0x1F, 0x0E, 0x5C, 0x00, 0x94, 0xFD, 0x00, 0x07 };
        Assert.Equal(basedOnRow, theme[0x2E3F..0x2E47]);
        Assert.Equal(Encoding.ASCII.GetBytes("BasedOn"), theme[0x2E47..0x2E4E]);
        // The per-file DropShadowEffect TypeInfo 63 over PresentationCore.
        var effectTypeRow = new byte[] { 0x1D, 0x33, 0x3F, 0x00, 0x02, 0x00, 0x2D };
        Assert.Equal(effectTypeRow, theme[0x2D36..0x2D3D]);
        Assert.Equal(Encoding.ASCII.GetBytes("System.Windows.Media.Effects.DropShadowEffect"), theme[0x2D3D..0x2D6A]);
        // The earlier 'EffectShadowColor' Color entry at 0xF228 (#88000000)
        // with its keyidx-29 record plus the StringInfo 29 row.
        Assert.Equal(new byte[] { 0x03, 0xAF, 0xFF, 0x01 }, theme[0xF228..0xF22C]);
        var shadowColor = new byte[] { 0x11, 0x0D, 0x09, 0x23, 0x38, 0x38, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0xA9, 0xFF, 0x04 };
        Assert.Equal(shadowColor, theme[0xF22C..0xF23B]);
        Assert.Equal(Encoding.ASCII.GetBytes("#88000000"), theme[0xF22F..0xF238]);
        Assert.Equal(new byte[] { 0x26, 0x09, 0x1D, 0x00, 0x91, 0x00, 0x00, 0x00, 0x00, 0x00 }, theme[0xAFF4..0xAFFE]);
        Assert.Equal(new byte[] { 0x20, 0x15, 0x1D, 0x00, 0x11 }, theme[0xE87..0xE8C]);
        Assert.Equal(Encoding.ASCII.GetBytes("EffectShadowColor"), theme[0xE8C..0xE9D]);
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
    public void Staged_boot_advances_past_shadow_run_to_search_path_style()
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
    public void Staged_shadow1_entry_lands_frozen_with_static_color()
    {
        // The full staged 72-byte entry parses its Freeze marker, the four
        // double PWCs, the Bias enum PWC and the StaticResource Color with
        // the staged numbering. The upstream default-skin #88000000 value
        // seeds the key, so the XAML loader resolve-now rule lands it for
        // real; the Freeze marker freezes the effect like o:Freeze="True".
        var app = new Application();
        try
        {
            app.Resources.Add("EffectShadowColor", Color.FromArgb(0x88, 0, 0, 0));
            LoadStagedHandyControlEntry();
            var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
            var dictionary = BamlReader.LoadDictionary(
                StagedShadowDictionary(theme[0x1AE8D..0x1AED5]));
            var shadow = Assert.IsType<DropShadowEffect>(dictionary["EffectShadow1"]);
            Assert.True(shadow.IsFrozen);
            Assert.Equal(5.0, shadow.BlurRadius);
            Assert.Equal(1.0, shadow.ShadowDepth);
            Assert.Equal(270.0, shadow.Direction);
            Assert.Equal(Color.FromArgb(0x88, 0, 0, 0), shadow.Color);
            Assert.Equal(0.2, shadow.Opacity);
            Assert.Equal(RenderingBias.Performance, shadow.RenderingBias);
            Assert.Same(shadow, dictionary["EffectShadow1"]);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Staged_shadow5_entry_lands_with_large_radius()
    {
        // The last staged shadow proves the far end of the run routes too:
        // Shadow5 35/13/270/static/.2/Performance over the same numbering.
        var app = new Application();
        try
        {
            app.Resources.Add("EffectShadowColor", Color.FromArgb(0x88, 0, 0, 0));
            LoadStagedHandyControlEntry();
            var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
            var dictionary = BamlReader.LoadDictionary(
                StagedShadowDictionary(theme[0x1AFB3..0x1AFFD], "EffectShadow5"));
            var shadow = Assert.IsType<DropShadowEffect>(dictionary["EffectShadow5"]);
            Assert.True(shadow.IsFrozen);
            Assert.Equal(35.0, shadow.BlurRadius);
            Assert.Equal(13.0, shadow.ShadowDepth);
            Assert.Equal(270.0, shadow.Direction);
            Assert.Equal(Color.FromArgb(0x88, 0, 0, 0), shadow.Color);
            Assert.Equal(0.2, shadow.Opacity);
            Assert.Equal(RenderingBias.Performance, shadow.RenderingBias);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Synthetic_static_color_resolves_from_application_resources()
    {
        // StaticResource shadow Colors with the key seeded in Application
        // resources land the seeded Color for real (not defaults): this is
        // the genuine conversion, and the seed-swap tests below prove no
        // hardcoding. The Freeze marker freezes the effect.
        var app = new Application();
        try
        {
            app.Resources.Add("ShadowA", Color.FromArgb(0x88, 0, 0, 0));
            var dictionary = BamlReader.LoadDictionary(
                ShadowDictionary("K", ShadowBody(staticColorId: 0), shared: true, "ShadowA"));
            var shadow = Assert.IsType<DropShadowEffect>(dictionary["K"]);
            Assert.True(shadow.IsFrozen);
            Assert.Equal(5.0, shadow.BlurRadius);
            Assert.Equal(1.0, shadow.ShadowDepth);
            Assert.Equal(270.0, shadow.Direction);
            Assert.Equal(Color.FromArgb(0x88, 0, 0, 0), shadow.Color);
            Assert.Equal(0.2, shadow.Opacity);
            Assert.Equal(RenderingBias.Performance, shadow.RenderingBias);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Synthetic_static_shadow_brush_and_packed_and_text_values_coerce()
    {
        // SolidColorBrush, packed-uint (gallery legacy store) and text values
        // behind the StaticResource key all coerce to the shadow Color
        // like the XAML loader resource-entry rule.
        var app = new Application();
        try
        {
            app.Resources.Add("BrushKey", new SolidColorBrush(Colors.Blue));
            app.Resources.Add("PackedKey", 0xFF0000FFu);
            app.Resources.Add("TextKey", "#0000FF");
            var brushDictionary = BamlReader.LoadDictionary(
                ShadowDictionary("B", ShadowBody(staticColorId: 0), shared: true, "BrushKey"));
            Assert.Equal(Colors.Blue, Assert.IsType<DropShadowEffect>(brushDictionary["B"]).Color);
            var packedDictionary = BamlReader.LoadDictionary(
                ShadowDictionary("P", ShadowBody(staticColorId: 0), shared: true, "PackedKey"));
            Assert.Equal(Colors.Red, Assert.IsType<DropShadowEffect>(packedDictionary["P"]).Color);
            var textDictionary = BamlReader.LoadDictionary(
                ShadowDictionary("T", ShadowBody(staticColorId: 0), shared: true, "TextKey"));
            Assert.Equal(Color.FromRgb(0x00, 0x00, 0xFF), Assert.IsType<DropShadowEffect>(textDictionary["T"]).Color);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Synthetic_static_color_resolves_from_dictionary_so_far()
    {
        // A StaticResource shadow key defined by an earlier entry in the same
        // file resolves without any Application: the dictionary-so-far is the
        // local scope. The Freeze marker still freezes the effect.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var content = new B();
            var first = content.Position;
            content.Raw(ColorEntry("#112233").ToArray());
            var second = content.Position;
            content.Raw(ShadowBody(staticColorId: 0)).ToArray();
            var head = Tables("Base", "K2");
            head.Deferable();
            head.Sized(0x26, p => p.U16(0).U32((uint)first).U16(0));
            head.Sized(0x26, p => p.U16(1).U32((uint)second).U16(0));
            head.Raw(0x37, 0x00, 0x00, 0x00);
            var dictionary = BamlReader.LoadDictionary(head.Raw(content.ToArray()).FinishDictionary().ToArray());
            var shadow = Assert.IsType<DropShadowEffect>(dictionary["K2"]);
            Assert.True(shadow.IsFrozen);
            Assert.Equal(Color.FromRgb(0x11, 0x22, 0x33), shadow.Color);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_literal_color_lands_live()
    {
        // The literal-Color arm still converts for real through the
        // ColorConverter (Red lands, and the second color proves no
        // hardcoding), so the StaticResource landings above are genuine
        // conversions.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var red = BamlReader.LoadDictionary(
                ShadowDictionary("R", ShadowBody(literalColor: "Red", freeze: false), shared: true));
            Assert.Equal(Colors.Red, Assert.IsType<DropShadowEffect>(red["R"]).Color);
            var blue = BamlReader.LoadDictionary(
                ShadowDictionary("B", ShadowBody(literalColor: "#0000FF", freeze: false), shared: true));
            Assert.Equal(Color.FromRgb(0x00, 0x00, 0xFF), Assert.IsType<DropShadowEffect>(blue["B"]).Color);
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
        // effect with its parse-time color even after the seed changes.
        var app = new Application();
        try
        {
            app.Resources.Add("ShadowA", Colors.Red);
            var dictionary = BamlReader.LoadDictionary(
                ShadowDictionary("K", ShadowBody(staticColorId: 0), shared: true, "ShadowA"));
            var first = Assert.IsType<DropShadowEffect>(dictionary["K"]);
            Assert.Equal(Colors.Red, first.Color);
            app.Resources["ShadowA"] = Colors.Blue;
            Assert.Same(first, dictionary["K"]);
            Assert.Equal(Colors.Red, Assert.IsType<DropShadowEffect>(dictionary["K"]).Color);
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
            app.Resources.Add("ShadowA", Colors.Red);
            var dictionary = BamlReader.LoadDictionary(
                ShadowDictionary("K", ShadowBody(staticColorId: 0), shared: false, "ShadowA"));
            var first = Assert.IsType<DropShadowEffect>(dictionary["K"]);
            Assert.Equal(Colors.Red, first.Color);
            app.Resources["ShadowA"] = Colors.Blue;
            var second = Assert.IsType<DropShadowEffect>(dictionary["K"]);
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
        // A StaticResource shadow key resolving to a non-color (here an
        // Int32) fails naming the key, the entry and the runtime type instead
        // of landing a default effect.
        var app = new Application();
        try
        {
            app.Resources.Add("ShadowA", 42);
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                ShadowDictionary("K", ShadowBody(staticColorId: 0), shared: true, "ShadowA")));
            Assert.Contains("StaticResource 'ShadowA' for keyed entry 'K' is a Int32, not a color", error.Message);
        }
        finally
        {
            Application.Current = _previous;
        }
    }

    [Fact]
    public void Synthetic_missing_static_key_stays_precise()
    {
        // Unlike DynamicResource (which stays Transparent when missing),
        // StaticResource must resolve at load: a missing key fails naming
        // the key and the entry instead of landing a default effect.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                ShadowDictionary("K", ShadowBody(staticColorId: 0), shared: true, "ShadowA")));
            Assert.Contains("StaticResource 'ShadowA' for keyed entry 'K' has no resource", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_shadow_defaults_land_at_wpf_values()
    {
        // An effect with no values at all lands at the WPF defaults (BAML
        // omits unset values): BlurRadius/ShadowDepth 5, Direction 315,
        // opaque-black Color, Opacity 1, Performance bias, unfrozen.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(0).Raw(0x00).Raw(0x04).ToArray();
            var dictionary = BamlReader.LoadDictionary(ShadowDictionary("K", body, shared: true));
            var shadow = Assert.IsType<DropShadowEffect>(dictionary["K"]);
            Assert.False(shadow.IsFrozen);
            Assert.Equal(5.0, shadow.BlurRadius);
            Assert.Equal(5.0, shadow.ShadowDepth);
            Assert.Equal(315.0, shadow.Direction);
            Assert.Equal(Color.FromArgb(0xFF, 0, 0, 0), shadow.Color);
            Assert.Equal(1.0, shadow.Opacity);
            Assert.Equal(RenderingBias.Performance, shadow.RenderingBias);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_shadow_duplicate_blur_radius_stays_precise()
    {
        // A second BlurRadius (here "8" behind "5") fails naming the
        // duplicate instead of replacing the first.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(0).Raw(0x00)
                .Sized(0x24, q => q.I16(0).Str("5").I16(-175))
                .Sized(0x24, q => q.I16(0).Str("8").I16(-175))
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                ShadowDictionary("K", body, shared: true)));
            Assert.Contains("duplicate BlurRadius on the DropShadowEffect", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_shadow_duplicate_color_stays_precise()
    {
        // A second Color (here a literal behind the StaticResource) fails
        // naming the duplicate instead of replacing the first.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(0).Raw(0x00)
                .Raw(new B().Raw(0x38).I16(3).U16(0).ToArray())
                .Sized(0x24, q => q.I16(3).Str("Red").I16(-87))
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                ShadowDictionary("K", body, shared: true, "ShadowA")));
            Assert.Contains("duplicate Color on the DropShadowEffect", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_shadow_double_wrong_converter_stays_precise()
    {
        // A BlurRadius over any other converter (here the EnumConverter)
        // fails naming the value, the converter and the target instead of
        // landing a default.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(0).Raw(0x00)
                .Sized(0x24, q => q.I16(0).Str("5").I16(-195))
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                ShadowDictionary("K", body, shared: true)));
            Assert.Contains("BlurRadius value with converter -195", error.Message);
            Assert.Contains("take the DoubleConverter", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_shadow_garbage_double_stays_precise()
    {
        // A non-numeric BlurRadius (here "abc") fails naming the text and
        // the property instead of landing a default.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(0).Raw(0x00)
                .Sized(0x24, q => q.I16(0).Str("abc").I16(-175))
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                ShadowDictionary("K", body, shared: true)));
            Assert.Contains("'abc' is not a number for BlurRadius", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_shadow_bad_bias_stays_precise()
    {
        // An unknown RenderingBias name (here "Super") fails naming the
        // text instead of landing the Performance default.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(0).Raw(0x00)
                .Sized(0x24, q => q.I16(5).Str("Super").I16(-195))
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                ShadowDictionary("K", body, shared: true)));
            Assert.Contains("'Super' is not a RenderingBias", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_shadow_wrong_attribute_stays_precise()
    {
        // A double PWC over a foreign attribute (here the SolidColorBrush
        // Color row) fails naming the ownership instead of landing on the
        // wrong property.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(0).Raw(0x00)
                .Sized(0x24, q => q.I16(6).Str("5").I16(-175))
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                WrongAttrShadowDictionary("K", body)));
            Assert.Contains("is owned by", error.Message);
            Assert.Contains("take effect-owned values", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_shadow_static_wrong_attribute_stays_precise()
    {
        // A StaticResource over a foreign attribute (here the Brush row)
        // fails naming the attribute instead of landing on Color.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(0).Raw(0x00)
                .Raw(new B().Raw(0x38).I16(6).U16(0).ToArray())
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                WrongAttrShadowDictionary("K", body, "ShadowA")));
            Assert.Contains("attribute 'Color'", error.Message);
            Assert.Contains("take the DropShadowEffect Color", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_shadow_bad_static_id_stays_precise()
    {
        // A StaticResource naming a static id the key block never declared
        // (here id 7 with no 0x37 declarations) fails naming the index
        // instead of landing a default color.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(0).Raw(0x00)
                .Raw(new B().Raw(0x38).I16(3).U16(7).ToArray())
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                ShadowDictionary("K", body, shared: true)));
            Assert.Contains("StaticResource id 7 in shadow entry 'K' has no StaticResource index", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_shadow_optimized_static_stays_precise()
    {
        // An optimized StaticResource value inside the entry (the 0x37 form
        // belongs in the key block, not entry content) fails naming the
        // record instead of landing a half-built effect.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(0).Raw(0x00)
                .Raw(0x37, 0x00, 0x01, 0x00)
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                ShadowDictionary("K", body, shared: true, "ShadowA")));
            Assert.Contains("optimized StaticResource value on the DropShadowEffect", error.Message);
            Assert.Contains("effect static values are a style-surface follow-up", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_shadow_markup_extension_stays_precise()
    {
        // A markup extension directly on the effect (no property slot carries
        // it: doubles/colors travel as PropertyWithConverter, the static
        // Color as PropertyWithStaticResourceId) fails naming the entry
        // instead of landing a half-built effect.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var body = new B().Raw(0x03).I16(0).Raw(0x00)
                .Raw(new B().Raw(0x23).I16(3).U16(189).U16(0).ToArray())
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                ShadowDictionary("K", body, shared: true, "ShadowA")));
            Assert.Contains("markup-extension value on the DropShadowEffect", error.Message);
            Assert.Contains("effect markup values are a style-surface follow-up", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Synthetic_shadow_child_element_stays_precise()
    {
        // A child element inside the effect (drop shadows take scalar values
        // only) fails naming the element instead of landing it.
        var previous = Application.Current;
        Application.Current = null;
        try
        {
            var child = new B().Raw(0x03).I16(-81).Raw(0x00).Raw(0x04).ToArray();
            var body = new B().Raw(0x03).I16(0).Raw(0x00)
                .Raw(child)
                .Raw(0x04).ToArray();
            var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
                ShadowDictionary("K", body, shared: true)));
            Assert.Contains("take BlurRadius/ShadowDepth/Direction/Color/Opacity/RenderingBias", error.Message);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Shadow_engine_shape_matches_wpf()
    {
        // The routed values match the genuine net10.0-windows shape: the
        // effect is Freezable honoring Freeze with live-verified defaults
        // (BlurRadius/ShadowDepth 5, Direction 315, opaque-black Color,
        // Opacity 1, RenderingBias Performance). The engine narrows the slots
        // to CLR properties (the SolidColorBrush.Color precedent: no DPs, so
        // no SetResourceReference, and the resolve-now entry rule is the
        // honest engine semantic); note the genuine DPs default Transparent
        // for nothing here - routing always sets Color explicitly (a missing
        // StaticResource is a precise error), so the default never surfaces
        // through this path.
        Assert.Same(typeof(double), typeof(DropShadowEffect).GetProperty("BlurRadius")!.PropertyType);
        Assert.Same(typeof(double), typeof(DropShadowEffect).GetProperty("ShadowDepth")!.PropertyType);
        Assert.Same(typeof(double), typeof(DropShadowEffect).GetProperty("Direction")!.PropertyType);
        Assert.Same(typeof(Color), typeof(DropShadowEffect).GetProperty("Color")!.PropertyType);
        Assert.Same(typeof(double), typeof(DropShadowEffect).GetProperty("Opacity")!.PropertyType);
        Assert.Same(typeof(RenderingBias), typeof(DropShadowEffect).GetProperty("RenderingBias")!.PropertyType);
        Assert.True(typeof(Freezable).IsAssignableFrom(typeof(DropShadowEffect)));
        Assert.Equal(RenderingBias.Performance, (RenderingBias)0);
        Assert.Equal(RenderingBias.Quality, (RenderingBias)1);
        var shadow = new DropShadowEffect();
        Assert.False(shadow.IsFrozen);
        Assert.Equal(5.0, shadow.BlurRadius);
        Assert.Equal(5.0, shadow.ShadowDepth);
        Assert.Equal(315.0, shadow.Direction);
        Assert.Equal(Color.FromArgb(0xFF, 0, 0, 0), shadow.Color);
        Assert.Equal(1.0, shadow.Opacity);
        Assert.Equal(RenderingBias.Performance, shadow.RenderingBias);
        shadow.Freeze();
        Assert.True(shadow.IsFrozen);
    }

    private static byte[] ShadowBody(
        bool freeze = true,
        string? blur = "5",
        string? depth = "1",
        string? direction = "270",
        string? literalColor = null,
        ushort? staticColorId = null,
        string? opacity = ".2",
        string? bias = "Performance")
    {
        // The staged shadow order (doubles in XAML attribute order, static or
        // literal Color, Opacity, RenderingBias) with the Freeze marker and
        // the per-file synthetic numbering (TypeInfo 0, attrs 0-5).
        var body = new B().Raw(0x03).I16(0).Raw(0x00);
        if (freeze)
            body.Sized(0x34, q => q.Str("True").U16(0));
        if (blur is not null)
            body.Sized(0x24, q => q.I16(0).Str(blur).I16(-175));
        if (depth is not null)
            body.Sized(0x24, q => q.I16(1).Str(depth).I16(-175));
        if (direction is not null)
            body.Sized(0x24, q => q.I16(2).Str(direction).I16(-175));
        if (literalColor is not null)
            body.Sized(0x24, q => q.I16(3).Str(literalColor).I16(-87));
        else if (staticColorId.HasValue)
            body.Raw(new B().Raw(0x38).I16(3).U16(staticColorId.Value).ToArray());
        if (opacity is not null)
            body.Sized(0x24, q => q.I16(4).Str(opacity).I16(-175));
        if (bias is not null)
            body.Sized(0x24, q => q.I16(5).Str(bias).I16(-195));
        return body.Raw(0x04).ToArray();
    }

    private static B Tables(params string[] names)
    {
        // Per-file ids stand in for the staged rows (TypeInfo 0 names the
        // framework DropShadowEffect; attrs 0-5 are its BlurRadius/
        // ShadowDepth/Direction/Color/Opacity/RenderingBias rows).
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1D, p => p.U16(0).U16(0).Str("System.Windows.Media.Effects.DropShadowEffect"))
            .Sized(0x1F, p => p.I16(0).I16(0).Raw(0x00).Str("BlurRadius"))
            .Sized(0x1F, p => p.I16(1).I16(0).Raw(0x00).Str("ShadowDepth"))
            .Sized(0x1F, p => p.I16(2).I16(0).Raw(0x00).Str("Direction"))
            .Sized(0x1F, p => p.I16(3).I16(0).Raw(0x00).Str("Color"))
            .Sized(0x1F, p => p.I16(4).I16(0).Raw(0x00).Str("Opacity"))
            .Sized(0x1F, p => p.I16(5).I16(0).Raw(0x00).Str("RenderingBias"));
        for (var i = 0; i < names.Length; i++)
        {
            var id = (ushort)i;
            var name = names[i];
            head.Sized(0x20, p => p.U16(id).Str(name));
        }
        return head;
    }

    private static byte[] Finish(B head, byte[] entry, ushort keyId, bool shared, params ushort[] staticKeyIds)
    {
        // One key record plus one 0x37 OptimizedStaticResource declaration
        // per static name (the 0x37 rides its key like the staged file),
        // then the entry content and the dictionary close.
        head.Deferable();
        if (shared)
            head.Sized(0x26, p => p.U16(keyId).U32(0).U16(0));
        else
            head.Sized(0x26, p => p.U16(keyId).U32(0).Raw(0x00, 0x01));
        foreach (var staticKeyId in staticKeyIds)
            head.Raw(0x37, 0x00).U16(staticKeyId);
        return head.Raw(entry).FinishDictionary().ToArray();
    }

    private static byte[] ShadowDictionary(string keyName, byte[] entry, bool shared, params string[] valueNames)
    {
        // StringInfo 0 is the entry key; each static name takes the next id,
        // and each takes a 0x37 declaration on the key.
        var names = new string[1 + valueNames.Length];
        names[0] = keyName;
        Array.Copy(valueNames, 0, names, 1, valueNames.Length);
        var staticIds = new ushort[valueNames.Length];
        for (var i = 0; i < valueNames.Length; i++) staticIds[i] = (ushort)(1 + i);
        return Finish(Tables(names), entry, 0, shared, staticIds);
    }

    private static byte[] StagedShadowDictionary(byte[] entry, string keyName = "EffectShadow1")
    {
        // The staged numbering for the sliced EffectShadow bytes (TypeInfo 63
        // names the framework effect; attrs 86-91 are its BlurRadius/
        // ShadowDepth/Direction/Color/Opacity/RenderingBias rows; StringInfo 0
        // is the entry key and 29 is 'EffectShadowColor' with the 0x37
        // declaration riding the key, like the staged file).
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1D, p => p.U16(63).U16(0).Str("System.Windows.Media.Effects.DropShadowEffect"))
            .Sized(0x1F, p => p.I16(0x56).I16(63).Raw(0x00).Str("BlurRadius"))
            .Sized(0x1F, p => p.I16(0x57).I16(63).Raw(0x00).Str("ShadowDepth"))
            .Sized(0x1F, p => p.I16(0x58).I16(63).Raw(0x00).Str("Direction"))
            .Sized(0x1F, p => p.I16(0x59).I16(63).Raw(0x00).Str("Color"))
            .Sized(0x1F, p => p.I16(0x5A).I16(63).Raw(0x00).Str("Opacity"))
            .Sized(0x1F, p => p.I16(0x5B).I16(63).Raw(0x00).Str("RenderingBias"))
            .Sized(0x20, p => p.U16(0).Str(keyName))
            .Sized(0x20, p => p.U16(29).Str("EffectShadowColor"));
        return Finish(head, entry, 0, true, 29);
    }

    private static byte[] WrongAttrShadowDictionary(string keyName, byte[] entry, params string[] valueNames)
    {
        // AttributeInfo 6 names the SolidColorBrush-owned Color row, so the
        // effect owner checks must reject both double and static values
        // riding it.
        var names = new string[1 + valueNames.Length];
        names[0] = keyName;
        Array.Copy(valueNames, 0, names, 1, valueNames.Length);
        var head = Tables(names);
        head.Sized(0x1F, p => p.I16(6).I16(-578).Raw(0x00).Str("Color"));
        var staticIds = new ushort[valueNames.Length];
        for (var i = 0; i < valueNames.Length; i++) staticIds[i] = (ushort)(1 + i);
        return Finish(head, entry, 0, true, staticIds);
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
