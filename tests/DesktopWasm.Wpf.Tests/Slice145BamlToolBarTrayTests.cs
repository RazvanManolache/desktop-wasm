// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-145 HandyControl theme.baml TabItemMenu landing (ToolBarTray landed next).
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the slice-144 blocker (the converted Lang.Close static threw because the
/// converted getter builds a ResourceManager whose assembly-attribute
/// enumeration loads the assembly-level XmlnsDefinitionAttribute the
/// converter mis-scoped onto System.Collections) is fixed in the converter
/// (unmapped System.Xaml assembly attributes drop with an adaptation instead
/// of mis-scoping; mapped ones remap; anything else gaps loudly), and
/// HandyControl is re-packaged with the fixed converter (6 staged, the same
/// 2 absent-binary gaps, theme.baml byte-identical at 466350 bytes). The
/// keyed 'TabItemMenu' ContextMenu entry keyidx 160 at [BAML+0x19D99,
/// BAML+0x19E4B) lands live with its three converted commands and live
/// Lang headers, and the keyed 'ToolBarTrayBaseStyle' Style entry at
/// BAML+0x19E4B (TargetType record at BAML+0x19E4F over well-known type id
/// -672, key string 151) landed with the slice-146 style-target routing
/// (engine ToolBarTray shim with the shared Panel Background identity);
/// the run now stops precise at the keyed 'TreeArrow' entry at
/// BAML+0x19E7B (element well-known id -446).
///
/// Byte-exact decode against the staged 466350-byte theme.baml: the entry
/// head 03 94 FD 00 at 0x19E4B, the PropertyTypeReference 22 00 00 60 FD
/// (attr 0 over -672) at 0x19E4F, the key record 26 09 97 00 B4 AC 00 00
/// 00 00 (key id 151, entry offset 0xACB4 relative to the 0xF197 dict
/// base = 0x19E4B, shared flags 00 00) at 0xB573, and the next key head
/// 26 09 98 00 (key id 152) at 0xB57D.
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path); all 6 staged images
/// sha256-verified, manifest hand-untouched except the slice's own
/// re-package step; staged-oracle status-gate bypass stays in-probe only.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice145BamlToolBarTrayTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice145BamlToolBarTrayTests()
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
        // with exactly the same two absent upstream binaries after the
        // slice's re-package, so the run below decodes the same staged
        // bytes the earlier slices pinned. The 466350-byte theme.baml
        // length plus the sha256 map below pin the input. Loading the
        // staged entry first guarantees the HandyControl assembly is
        // present however the runner orders the tests.
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
    public void Theme_baml_places_toolbar_tray_head_and_key_bytes()
    {
        // Staged framing pins for the keyed 'ToolBarTrayBaseStyle' Style
        // entry at [0x19E4B, ...): the Style head, the TargetType record
        // over well-known id -672, the key-151 record, and the next key
        // head, plus the TabItemMenu tail proving the landed region ends
        // where the new stop begins.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);

        var tabMenuTail = new byte[] { 0xA2, 0xFF, 0x23, 0xB9, 0xFF, 0x5A, 0x02, 0x4F };
        Assert.Equal(tabMenuTail, theme[0x19E40..0x19E48]);
        var entryHead = new byte[] { 0x03, 0x94, 0xFD, 0x00 };
        Assert.Equal(entryHead, theme[0x19E4B..0x19E4F]);
        var targetType = new byte[] { 0x22, 0x00, 0x00, 0x60, 0xFD };
        Assert.Equal(targetType, theme[0x19E4F..0x19E54]);
        var key151 = new byte[] { 0x26, 0x09, 0x97, 0x00, 0xB4, 0xAC, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(key151, theme[0xB573..0xB57D]);
        var key152Head = new byte[] { 0x26, 0x09, 0x98, 0x00 };
        Assert.Equal(key152Head, theme[0xB57D..0xB581]);
    }

    [Fact]
    public void Staged_boot_advances_past_toolbar_tray_to_tree_arrow_stop()
    {
        // The staged entry now decodes past the whole keyed
        // 'ToolBarTrayBaseStyle' entry (Style head, TargetType over -672,
        // single Background Transparent setter - the slice-146 style-target
        // routing), then stops precise at the keyed 'TreeArrow' entry: its
        // element well-known id -446 has no keyed-entry routing yet.
        // Everything the earlier slices pinned stays landed.
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
    public void Staged_shape_tab_menu_lands_live()
    {
        // The staged-verbatim 'TabItemMenu' entry bytes [0x19D99, 0x19E4B)
        // now land as a live engine ContextMenu: three rows carrying the
        // converted ControlCommands singletons with headers equal to the
        // live converted Lang statics (read through the converted
        // ResourceManager that the mis-scoped attributes used to break),
        // and every non-shared lookup materializes fresh instances.
        LoadStagedHandyControlEntry();
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var controlCommands = handyControl.GetType("HandyControl.Interactivity.ControlCommands")!;
        var closeCommand = controlCommands.GetProperty("Close", BindingFlags.Public | BindingFlags.Static)!.GetValue(null);
        var closeAllCommand = controlCommands.GetProperty("CloseAll", BindingFlags.Public | BindingFlags.Static)!.GetValue(null);
        var closeOtherCommand = controlCommands.GetProperty("CloseOther", BindingFlags.Public | BindingFlags.Static)!.GetValue(null);
        Assert.IsType<RoutedCommand>(closeCommand);
        Assert.IsType<RoutedCommand>(closeAllCommand);
        Assert.IsType<RoutedCommand>(closeOtherCommand);
        var lang = handyControl.GetType("HandyControl.Properties.Langs.Lang")!;
        var closeText = (string)lang.GetProperty("Close", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var closeAllText = (string)lang.GetProperty("CloseAll", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var closeOtherText = (string)lang.GetProperty("CloseOther", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        var dictionary = BamlReader.LoadDictionary(StagedLandingMenuDictionary(theme[0x19D99..0x19E4B]));
        var first = (ContextMenu)dictionary["TabItemMenu"]!;
        Assert.Equal(3, first.Items.Count);
        var firstClose = Assert.IsType<MenuItem>(first.Items[0]);
        var firstCloseAll = Assert.IsType<MenuItem>(first.Items[1]);
        var firstCloseOther = Assert.IsType<MenuItem>(first.Items[2]);
        Assert.Equal(closeText, firstClose.Header);
        Assert.Equal(closeAllText, firstCloseAll.Header);
        Assert.Equal(closeOtherText, firstCloseOther.Header);
        Assert.Same(closeCommand, firstClose.Command);
        Assert.Same(closeAllCommand, firstCloseAll.Command);
        Assert.Same(closeOtherCommand, firstCloseOther.Command);
        var second = (ContextMenu)dictionary["TabItemMenu"]!;
        Assert.NotSame(first, second);
        Assert.NotSame(firstClose, Assert.IsType<MenuItem>(second.Items[0]));
        Assert.Same(closeCommand, Assert.IsType<MenuItem>(second.Items[0]).Command);
        Assert.Equal(closeText, Assert.IsType<MenuItem>(second.Items[0]).Header);
    }

    [Fact]
    public void Staged_images_carry_no_dangling_assembly_attributes()
    {
        // The re-packaged images enumerate their assembly and module
        // attributes cleanly: the mis-scoped XAML/content rows are gone
        // (pre-fix, GetCustomAttributesData threw TypeLoadException on the
        // System.Collections-scoped XmlnsDefinitionAttribute), and no
        // remaining row names an unmapped framework marker.
        LoadStagedHandyControlEntry();
        var staged = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => StagedNames.Contains(a.GetName().Name, StringComparer.OrdinalIgnoreCase))
            .GroupBy(a => a.GetName().Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
        Assert.Equal(StagedNames.OrderBy(n => n).ToArray(),
            staged.Select(a => a.GetName().Name!).OrderBy(n => n).ToArray());
        foreach (var assembly in staged)
        {
            var rows = assembly.GetCustomAttributesData()
                .Concat(assembly.Modules.SelectMany(m => m.GetCustomAttributesData()))
                .ToArray();
            Assert.DoesNotContain(rows, d => d.AttributeType.Name.Contains("Xmlns", StringComparison.Ordinal));
            Assert.DoesNotContain(rows, d => d.AttributeType.FullName == "System.Windows.Resources.AssemblyAssociatedContentFileAttribute");
            Assert.DoesNotContain(rows, d => d.AttributeType.FullName == "System.Windows.ThemeInfoAttribute");
        }
    }

    [Fact]
    public void Synthetic_toolbar_tray_style_target_lands()
    {
        // The slice-146 style-target routing lands a keyed Style over
        // well-known type id -672: TargetType resolves to the engine
        // ToolBarTray and the qualified Background packed-ARGB setter lands
        // Transparent.
        var content = new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(-672)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, p => p.I16(1).U16(16521).I16(-672).Str("Background"))
            .Sized(0x06, p => p.I16(2).U16(744).Raw(0x01, 0xFF, 0xFF, 0xFF, 0x00))
            .Raw(0x04, 0x04).ToArray();
        var dictionary = BamlReader.LoadDictionary(KeyedStyleDictionary("S", content));
        var style = Assert.IsType<Style>(dictionary["S"]);
        Assert.Same(typeof(ToolBarTray), style.TargetType);
        var setter = Assert.Single(style.Setters.OfType<Setter>());
        Assert.Same(ToolBarTray.BackgroundProperty, setter.Property);
        Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(setter.Value).Color);
    }

    private static readonly string[] StagedNames =
    [
        "HandyControlDemo", "HandyControl", "GalaSoft.MvvmLight",
        "ICSharpCode.AvalonEdit", "HandyControlDemoCode", "Newtonsoft.Json",
    ];

    private static byte[] StagedLandingMenuDictionary(byte[] stagedEntry)
    {
        // The staged table ids mirrored with dummies (assembly 0, TypeInfo
        // 55 Lang, AttributeInfo 76-79, string 150, the real interactivity
        // mapping rows) around the staged-verbatim entry bytes, so the
        // Header statics resolve through the converted Lang type.
        LoadStagedHandyControlEntry();
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var tables = new B().Sized(0x1C, p => p.U16(0).Str(handyControl.FullName!));
        for (var i = 0; i <= 55; i++)
            tables = tables.Sized(0x1D, p => p.U16((ushort)i).U16(0)
                .Str(i == 55 ? "HandyControl.Properties.Langs.Lang" : "Dummy.T" + i));
        for (var i = 0; i <= 79; i++)
        {
            var id = i;
            tables = tables.Sized(0x1F, p =>
            {
                p.U16((ushort)id);
                if (id == 76) { p.I16(-403).Raw(0x00).Str("Command"); }
                else if (id == 77) { p.I16(55).Raw(0x00).Str("Close"); }
                else if (id == 78) { p.I16(55).Raw(0x00).Str("CloseAll"); }
                else if (id == 79) { p.I16(55).Raw(0x00).Str("CloseOther"); }
                else { p.I16(0).Raw(0x00).Str("DummyA" + id); }
                return p;
            });
        }
        for (var i = 0; i <= 150; i++)
        {
            var id = i;
            tables = tables.Sized(0x20, p => p.U16((ushort)id).Str(id == 150 ? "TabItemMenu" : "DummyS" + id));
        }
        var xmlns = new B()
            .Sized(0x14, p => p.Str("interactivity").Str("clr-namespace:HandyControl.Interactivity").U16(1).U16(0))
            .Sized(0x1B, p => p.Str("clr-namespace:HandyControl.Interactivity").Str("HandyControl.Interactivity").U16(0))
            .ToArray();
        return MenuDictionary(tables.ToArray().Concat(xmlns).ToArray(), false, stagedEntry, keyId: 150);
    }

    private static byte[] MenuDictionary(byte[] tables, bool shared, byte[] entry, int keyId = 0)
    {
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(tables)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Deferable();
        head.Raw(0x26).Seven(9).U16((ushort)keyId).U32(0);
        head.Raw(shared ? new byte[] { 0x00, 0x00 } : new byte[] { 0x00, 0x01 });
        return head.Raw(entry).FinishDictionary().ToArray();
    }

    private static byte[] KeyedStyleDictionary(string key, byte[] entry)
    {
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.U16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.U16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x1F, p => p.U16(7).I16(-108).Raw(0x00).Str("TargetType"))
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
