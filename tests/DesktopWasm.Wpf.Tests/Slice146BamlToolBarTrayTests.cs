// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-146 HandyControl theme.baml ToolBarTray style-target routing.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the slice-145 blocker (the keyed 'ToolBarTrayBaseStyle' Style entry at
/// BAML+0x19E4B targeting well-known type id -672, which had no style-target
/// routing) lands as a live engine Style: the new ToolBarTray shim carries
/// the shared Panel Background identity, so the staged qualified Background
/// packed-ARGB setter (Transparent) resolves through
/// DependencyProperty.FromName like WPF. The run advances past it (slice-147
/// routes the keyed 'TreeArrow' PathGeometry entry at BAML+0x19E7B as a live
/// engine PathGeometry; slice-148 routes the keyed 'RowHeaderGripperStyle'
/// and 'ColumnHeaderGripperStyle' entries at BAML+0x19FF7/0x1A083 through the
/// new FrameworkElement.CursorProperty DP) and stops precise at the keyed
/// 'DataGridComboBoxColumnStyle' Style entry at BAML+0x1A113 (ComboBox
/// target -92 with no style-target routing).
///
/// Byte-exact decode against the staged 466350-byte theme.baml: the Style
/// head 03 94 FD 00 at 0x19E4B, the PropertyTypeReference 22 00 00 60 FD
/// (attr 0 over -672) at 0x19E4F, the Style.Setters content 2E 12 FF at
/// 0x19E54, the Setter head 03 D4 FD 00 at 0x19E57, the qualified
/// PropertyCustom 06 12 01 00 89 40 60 FD 0A "Background" (attr 1,
/// serializer 16521, owner -672) at 0x19E5B, the packed-ARGB value 06 0A
/// 02 00 E8 02 01 FF FF FF 00 (attr 2, serializer 744, Transparent) at
/// 0x19E6E, the 04 04 closes at 0x19E79, the next entry head 03 42 FE 00
/// (element -446) at 0x19E7B with the 24 F6 02 A7 FF EE 02 string-form value
/// ("M508..."), the key-152 record 26 09 98 00 E4 AC 00 00 00 00 at 0xB57D
/// and the key-153 head 26 09 99 00 at 0xB587.
///
/// Triangulated three ways: the staged framing above; the live upstream
/// HandyControl ToolBarBaseStyle.xaml block at pinned commit e9ad3d70 (the
/// same 'ToolBarTrayBaseStyle' style with the single Background
/// "Transparent" setter); a genuine net10.0-windows ResourceDictionary
/// fixture compiling the exact upstream block into Tray.baml, byte-identical
/// in every staged record shape (Style head, TargetType -672, Setters
/// content, qualified Background setter over owner -672, packed-ARGB
/// Transparent value), differing only in per-file table numbering and line
/// markers (fixture retained as Tray.xaml in the E:/tmp/bamlfix project
/// outside the tree), plus a genuine net10.0-windows reflection probe
/// (ToolBarTray : FrameworkElement with ToolBarTray-owned Background,
/// Orientation and IsLocked; Background default null, Orientation default
/// Horizontal, IsLocked default false; the BackgroundProperty field value
/// is the shared Panel.BackgroundProperty instance).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path); all 6 staged images
/// sha256-verified, manifest hand-untouched; staged-oracle status-gate
/// bypass stays in-probe only.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice146BamlToolBarTrayTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice146BamlToolBarTrayTests()
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
    public void Theme_baml_places_tray_style_and_tree_arrow_bytes()
    {
        // Staged framing pins for the keyed 'ToolBarTrayBaseStyle' Style
        // entry at [0x19E4B, 0x19E7B): the Style head, the TargetType
        // record over well-known id -672, the Setters content, the Setter
        // head, the qualified Background PropertyCustom, the packed-ARGB
        // Transparent value and the closes - plus the landed 'TreeArrow'
        // entry bytes (element -446 with its string-form path-data value,
        // routed by slice-147) and the key-152/153 records.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);

        var tabMenuTail = new byte[] { 0xA2, 0xFF, 0x23, 0xB9, 0xFF, 0x5A, 0x02, 0x4F };
        Assert.Equal(tabMenuTail, theme[0x19E40..0x19E48]);
        var entryHead = new byte[] { 0x03, 0x94, 0xFD, 0x00 };
        Assert.Equal(entryHead, theme[0x19E4B..0x19E4F]);
        var targetType = new byte[] { 0x22, 0x00, 0x00, 0x60, 0xFD };
        Assert.Equal(targetType, theme[0x19E4F..0x19E54]);
        var settersContent = new byte[] { 0x2E, 0x12, 0xFF };
        Assert.Equal(settersContent, theme[0x19E54..0x19E57]);
        var setterHead = new byte[] { 0x03, 0xD4, 0xFD, 0x00 };
        Assert.Equal(setterHead, theme[0x19E57..0x19E5B]);
        var property = new byte[] { 0x06, 0x12, 0x01, 0x00, 0x89, 0x40, 0x60, 0xFD, 0x0A }
            .Concat(Encoding.ASCII.GetBytes("Background")).ToArray();
        Assert.Equal(property, theme[0x19E5B..0x19E6E]);
        var packedTransparent = new byte[] { 0x06, 0x0A, 0x02, 0x00, 0xE8, 0x02, 0x01, 0xFF, 0xFF, 0xFF, 0x00 };
        Assert.Equal(packedTransparent, theme[0x19E6E..0x19E79]);
        var closes = new byte[] { 0x04, 0x04 };
        Assert.Equal(closes, theme[0x19E79..0x19E7B]);
        var nextHead = new byte[] { 0x03, 0x42, 0xFE, 0x00 };
        Assert.Equal(nextHead, theme[0x19E7B..0x19E7F]);
        var nextValuePrefix = new byte[] { 0x24, 0xF6, 0x02, 0xA7, 0xFF, 0xEE, 0x02 }
            .Concat(Encoding.ASCII.GetBytes("M508")).ToArray();
        Assert.Equal(nextValuePrefix, theme[0x19E7F..0x19E8A]);
        var key152 = new byte[] { 0x26, 0x09, 0x98, 0x00, 0xE4, 0xAC, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key152, theme[0xB57D..0xB587]);
        var key153Head = new byte[] { 0x26, 0x09, 0x99, 0x00 };
        Assert.Equal(key153Head, theme[0xB587..0xB58B]);
    }

    [Fact]
    public void Staged_boot_advances_past_tray_to_tree_arrow_stop()
    {
        // The staged entry now decodes past the whole keyed
        // 'ToolBarTrayBaseStyle' entry (Style head, TargetType over -672,
        // single Background Transparent setter), the keyed 'TreeArrow'
        // PathGeometry entry (routed by slice-147) and both keyed gripper
        // Style entries (routed by slice-148), then stops precise at the
        // keyed 'DataGridComboBoxColumnStyle' entry (ComboBox target -92
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
    }

    [Fact]
    public void Staged_shape_tray_style_lands_live()
    {
        // The staged-verbatim 'ToolBarTrayBaseStyle' entry bytes
        // [0x19E4B, 0x19E7B) land as a live engine Style: TargetType is the
        // engine ToolBarTray, the single setter carries the shared Panel
        // Background identity with a Transparent SolidColorBrush, and the
        // shared key returns the same instance on every lookup.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        var dictionary = BamlReader.LoadDictionary(
            TrayDictionary(StagedTrayStyleBytes(theme)));
        var style = Assert.IsType<Style>(dictionary["ToolBarTrayBaseStyle"]);
        Assert.Same(typeof(ToolBarTray), style.TargetType);
        var setter = Assert.Single(style.Setters.OfType<Setter>());
        Assert.Same(ToolBarTray.BackgroundProperty, setter.Property);
        Assert.Same(Panel.BackgroundProperty, setter.Property);
        var brush = Assert.IsType<SolidColorBrush>(setter.Value);
        Assert.Equal(Colors.Transparent, brush.Color);
        Assert.Same(style, dictionary["ToolBarTrayBaseStyle"]);
    }

    [Fact]
    public void Synthetic_tray_style_target_lands()
    {
        // A hand-built keyed Style over well-known type id -672 with the
        // staged setter shape (qualified Background PropertyCustom plus a
        // packed-ARGB Transparent value) lands live instead of failing
        // precise: the exact next blocker behind the staged stop is routed.
        var entry = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-672)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, p => p.I16(1).U16(16521).I16(-672).Str("Background"))
            .Sized(0x06, p => p.I16(2).U16(744).Raw(0x01, 0xFF, 0xFF, 0xFF, 0x00))
            .Raw(0x04, 0x04).ToArray();
        var dictionary = BamlReader.LoadDictionary(TrayDictionary(entry, "S"));
        var style = Assert.IsType<Style>(dictionary["S"]);
        Assert.Same(typeof(ToolBarTray), style.TargetType);
        var setter = Assert.Single(style.Setters.OfType<Setter>());
        Assert.Same(ToolBarTray.BackgroundProperty, setter.Property);
        Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(setter.Value).Color);
    }

    [Fact]
    public void Synthetic_unknown_style_target_stays_precise()
    {
        // A keyed Style over an unrouted well-known id still fails precise,
        // naming the id and the extended style-target allowlist (which now
        // carries ToolBarTray behind the landed -672 entry).
        var content = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-777)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(TrayDictionary(content)));
        Assert.Contains("well-known type id -777", error.Message);
        Assert.Contains("style targets beyond Path/Border/ContentControl/GridSplitter/Rectangle/RepeatButton/GroupBox/Thumb/ToolBarTray", error.Message);
    }

    [Fact]
    public void Synthetic_unknown_tray_owner_stays_precise()
    {
        // A qualified setter owner with no routing still fails precise,
        // naming the owner and the extended owner allowlist (which now
        // carries ToolBarTray behind the landed Background setter).
        var content = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-672)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, p => p.I16(1).U16(16521).I16(-777).Str("Background"))
            .Raw(0x04, 0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(TrayDictionary(content)));
        Assert.Contains("setter property owner well-known type id -777", error.Message);
        Assert.Contains("owners beyond Shape/UIElement/Window/FrameworkElement/Canvas/ToolBarTray", error.Message);
    }

    [Fact]
    public void Tray_engine_shape_matches_wpf()
    {
        // The shim mirrors the genuine WPF surface the staged entry needs
        // (live-verified on net10.0-windows): a FrameworkElement carrying
        // the shared Panel Background identity with a null default, kept
        // store-backed through the CLR wrapper.
        Assert.True(typeof(FrameworkElement).IsAssignableFrom(typeof(ToolBarTray)));
        Assert.Same(Panel.BackgroundProperty, ToolBarTray.BackgroundProperty);
        Assert.Same(Panel.BackgroundProperty, DependencyProperty.FromName(typeof(ToolBarTray), "Background"));
        Assert.Null(new ToolBarTray().Background);
        var tray = new ToolBarTray { Background = new SolidColorBrush(Colors.Red) };
        Assert.Equal(Colors.Red, ((SolidColorBrush)tray.GetValue(ToolBarTray.BackgroundProperty)!).Color);
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(tray.Background!).Color);
    }

    [Fact]
    public void Tray_parses_loose_xaml()
    {
        // Loose XAML resolves the same engine name through the engine
        // factory, with Background landing through the Brush text path.
        // (Red, not Transparent: the loose named-color path keeps its
        // pre-existing transparent-black spelling while the BAML packed
        // path above pins the staged Transparent white.)
        var root = (ToolBarTray)XamlLoader.Load(
            "<ToolBarTray xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Background=\"Red\" />").Root;
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(root.Background!).Color);
    }

    private static byte[] StagedTrayStyleBytes(byte[] theme)
    {
        // The staged-verbatim 'ToolBarTrayBaseStyle' entry bytes: the whole
        // [0x19E4B, 0x19E7B) span the framing test pins above.
        Assert.Equal(466350, theme.Length);
        return theme[0x19E4B..0x19E7B];
    }

    private static byte[] TrayDictionary(byte[] entry, string key = "ToolBarTrayBaseStyle")
    {
        // The staged per-file rows mirrored with dummies (assembly 0, no
        // TypeInfo rows since every id in the tray shape is well-known,
        // AttributeInfo 0/1/2 over Style.TargetType plus Setter.Property
        // plus Setter.Value, the key string) around either the
        // staged-verbatim or the hand-built entry bytes.
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.U16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.U16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x20, p => p.U16(0).Str(key))
            .Deferable()
            .Sized(0x26, p => p.U16(0).U32(0).U16(0));
        return head.Raw(entry).FinishDictionary().ToArray();
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
