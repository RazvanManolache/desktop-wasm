// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-144 HandyControl theme.baml TabItemMenu run.
///
/// Scope decision (see the slice entry in docs/preview-upgrade-tasks.md):
/// the slice-143 blocker (the keyed 'TabItemMenu' ContextMenu entry at
/// BAML+0x19D99 naming well-known type id -105 the keyed-entry routing had
/// no shape for) routes structurally: the Items content member, three
/// MenuItem rows each carrying a converted Command PropertyWithConverter
/// (converter -94 over the MenuItem-owned Command row, prefix:Type.Member
/// text resolved through the file xmlns and namespace-mapping tables) plus
/// an x:Static Header PropertyWithExtension (target member -71, Static
/// extension 602, per-file AttributeInfo id naming the converted static),
/// closed by ElementEnd. The commands resolve live to the converted
/// HandyControl.Interactivity.ControlCommands engine RoutedCommand
/// singletons. The first Header static throws reading
/// HandyControl.Properties.Langs.Lang.Close: the converted getter builds a
/// ResourceManager, whose assembly-attribute enumeration loads the
/// assembly-level XmlnsDefinitionAttribute the converter mis-scoped onto
/// System.Collections (upstream scope System.Xaml) - so the staged entry
/// stops precise at BAML+0x19DCE with the converter-defect error, and
/// ToolBarTrayBaseStyle plus everything after it stays pinned.
///
/// Byte-exact decode against the staged 466350-byte theme.baml,
/// triangulated three ways: the staged bytes (the non-shared key record
/// with key id 150, the 03 97 FF 00 ContextMenu head, the 2E 40 FF Items
/// content, the three 03 6D FE 00 MenuItem heads, the three Command texts
/// with their A2 FF converters, the three 23 B9 FF 5A 02 Header statics
/// with AttributeInfo operands 77/78/79, the closes, the next 03 94 FD 00
/// Style head, plus the 0x1B/0x14 interactivity mapping records and the
/// AttributeInfo rows 76-79 plus the TypeInfo 55 Lang row); the upstream
/// TabControlBaseStyle.xaml block at pinned commit e9ad3d70 (the
/// ContextMenu with its three converted Command plus x:Static Header rows
/// and the xmlns:interactivity/xmlns:langs declarations); a genuine
/// net10.0-windows fixture compiling the same menu block (tabmenu.baml,
/// emitting 97 FF heads, 6D FE row heads, A2 FF Command converters on both
/// MenuItem and Button Command properties with custom and framework
/// statics, the fixed 23 B9 FF 5A 02 static shape with AttributeInfo
/// operands, and the non-shared key flags, retained at E:/tmp/bamlfix144
/// outside the tree) plus a live net10.0-windows runtime probe (the
/// fixture XAML loads through the real XamlReader with string headers and
/// RoutedCommand/RoutedUICommand commands, eager like the engine).
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// manifests otherwise hand-untouched; staged-oracle status-gate bypass
/// stays in-probe only).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice144BamlTabMenuTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice144BamlTabMenuTests()
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
    public void Theme_baml_places_tab_menu_head_and_row_bytes()
    {
        // Staged framing pins for the keyed 'TabItemMenu' ContextMenu entry
        // keyidx 160 spanning [0x19D99, 0x19E4B): the non-shared key record,
        // the ContextMenu head (well-known id -105), the Items content
        // member, the three MenuItem heads, the three converted Command
        // PropertyWithConverter records (MenuItem-owned row, converter -94,
        // prefix:Type.Member texts), the three x:Static Header
        // PropertyWithExtension records (target -71, Static extension 602,
        // AttributeInfo operands 77/78/79), the closes, and the next Style
        // entry head; plus the 0x1B/0x14 interactivity mapping records and
        // the AttributeInfo rows 76-79 with the TypeInfo 55 Lang row that
        // the entry machine resolves.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");

        var tabMenuKey = new byte[] { 0x26, 0x09, 0x96, 0x00, 0x02, 0xAC, 0x00, 0x00, 0x00, 0x01 };
        Assert.Equal(tabMenuKey, theme[0xB569..0xB573]);
        var tabMenuHead = new byte[] { 0x03, 0x97, 0xFF, 0x00 };
        Assert.Equal(tabMenuHead, theme[0x19D99..0x19D9D]);
        var tabMenuContent = new byte[] { 0x2E, 0x40, 0xFF };
        Assert.Equal(tabMenuContent, theme[0x19D9D..0x19DA0]);

        var menuItemHead = new byte[] { 0x03, 0x6D, 0xFE, 0x00 };
        Assert.Equal(menuItemHead, theme[0x19DA0..0x19DA4]);
        Assert.Equal(menuItemHead, theme[0x19DD6..0x19DDA]);
        Assert.Equal(menuItemHead, theme[0x19E0F..0x19E13]);

        var commandHead1 = new byte[] { 0x24, 0x29, 0x4C, 0x00, 0x23 };
        Assert.Equal(commandHead1, theme[0x19DA4..0x19DA9]);
        Assert.Equal(
            Encoding.ASCII.GetBytes("interactivity:ControlCommands.Close"),
            theme[0x19DA9..0x19DCC]);
        var commandConv = new byte[] { 0xA2, 0xFF };
        Assert.Equal(commandConv, theme[0x19DCC..0x19DCE]);

        var commandHead2 = new byte[] { 0x24, 0x2C, 0x4C, 0x00, 0x26 };
        Assert.Equal(commandHead2, theme[0x19DDA..0x19DDF]);
        Assert.Equal(
            Encoding.ASCII.GetBytes("interactivity:ControlCommands.CloseAll"),
            theme[0x19DDF..0x19E05]);
        Assert.Equal(commandConv, theme[0x19E05..0x19E07]);

        var commandHead3 = new byte[] { 0x24, 0x2E, 0x4C, 0x00, 0x28 };
        Assert.Equal(commandHead3, theme[0x19E13..0x19E18]);
        Assert.Equal(
            Encoding.ASCII.GetBytes("interactivity:ControlCommands.CloseOther"),
            theme[0x19E18..0x19E40]);
        Assert.Equal(commandConv, theme[0x19E40..0x19E42]);

        var headerStatic1 = new byte[] { 0x23, 0xB9, 0xFF, 0x5A, 0x02, 0x4D, 0x00 };
        Assert.Equal(headerStatic1, theme[0x19DCE..0x19DD5]);
        var headerStatic2 = new byte[] { 0x23, 0xB9, 0xFF, 0x5A, 0x02, 0x4E, 0x00 };
        Assert.Equal(headerStatic2, theme[0x19E07..0x19E0E]);
        var headerStatic3 = new byte[] { 0x23, 0xB9, 0xFF, 0x5A, 0x02, 0x4F, 0x00 };
        Assert.Equal(headerStatic3, theme[0x19E42..0x19E49]);

        var menuCloses = new byte[] { 0x04, 0x04 };
        Assert.Equal(menuCloses, theme[0x19E49..0x19E4B]);
        var nextHead = new byte[] { 0x03, 0x94, 0xFD, 0x00 };
        Assert.Equal(nextHead, theme[0x19E4B..0x19E4F]);

        var pimapHead = new byte[] { 0x1B, 0x47, 0x28 };
        Assert.Equal(pimapHead, theme[0x193..0x196]);
        Assert.Equal(
            Encoding.ASCII.GetBytes("clr-namespace:HandyControl.Interactivity"),
            theme[0x196..0x1BE]);
        Assert.Equal(0x1A, theme[0x1BE]);
        Assert.Equal(
            Encoding.ASCII.GetBytes("HandyControl.Interactivity"),
            theme[0x1BF..0x1D9]);
        Assert.Equal(new byte[] { 0x00, 0x00 }, theme[0x1D9..0x1DB]);

        var xmlnsHead = new byte[] { 0x14, 0x3A, 0x0D };
        Assert.Equal(xmlnsHead, theme[0xADD2..0xADD5]);
        Assert.Equal(Encoding.ASCII.GetBytes("interactivity"), theme[0xADD5..0xADE2]);
        Assert.Equal(0x28, theme[0xADE2]);
        Assert.Equal(
            Encoding.ASCII.GetBytes("clr-namespace:HandyControl.Interactivity"),
            theme[0xADE3..0xAE0B]);
        Assert.Equal(new byte[] { 0x00, 0x00 }, theme[0xAE0B..0xAE0D]);

        var commandAttr = new byte[] { 0x1F, 0x0E, 0x4C, 0x00, 0x6D, 0xFE, 0x00, 0x07 };
        Assert.Equal(commandAttr, theme[0x246F..0x2477]);
        Assert.Equal(Encoding.ASCII.GetBytes("Command"), theme[0x2477..0x247E]);

        var langType = new byte[] { 0x1D, 0x28, 0x37, 0x00, 0x00, 0x00, 0x22 };
        Assert.Equal(langType, theme[0x247E..0x2485]);
        Assert.Equal(
            Encoding.ASCII.GetBytes("HandyControl.Properties.Langs.Lang"),
            theme[0x2485..0x24A7]);

        var closeAttr = new byte[] { 0x1F, 0x0C, 0x4D, 0x00, 0x37, 0x00, 0x00, 0x05 };
        Assert.Equal(closeAttr, theme[0x24A7..0x24AF]);
        Assert.Equal(Encoding.ASCII.GetBytes("Close"), theme[0x24AF..0x24B4]);
        var closeAllAttr = new byte[] { 0x1F, 0x0F, 0x4E, 0x00, 0x37, 0x00, 0x00, 0x08 };
        Assert.Equal(closeAllAttr, theme[0x24B4..0x24BC]);
        Assert.Equal(Encoding.ASCII.GetBytes("CloseAll"), theme[0x24BC..0x24C4]);
        var closeOtherAttr = new byte[] { 0x1F, 0x11, 0x4F, 0x00, 0x37, 0x00, 0x00, 0x0A };
        Assert.Equal(closeOtherAttr, theme[0x24C4..0x24CC]);
        Assert.Equal(Encoding.ASCII.GetBytes("CloseOther"), theme[0x24CC..0x24D6]);
    }

    [Fact]
    public void Staged_boot_advances_past_tab_menu_to_toolbar_tray_stop()
    {
        // Slice-145 rewrite: the converter attribute-scope fix plus the
        // re-package land the whole keyed 'TabItemMenu' entry (ContextMenu
        // head, Items content, three MenuItem rows with converted commands
        // and live Lang headers - the header-static defect is gone), so the
        // staged boot now stops precise at the keyed 'ToolBarTrayBaseStyle'
        // Style entry at BAML+0x19E4F: it targets well-known type id -672,
        // which has no style-target routing yet. Everything the earlier
        // slices pinned stays landed.
        var entry = LoadStagedHandyControlEntry();
        var error = Assert.Throws<InvalidOperationException>(() => WpfAppBoot.Boot(entry));
        Assert.Contains("startup failed in entry point", error.Message);
        Assert.Contains("line 23", error.Message);
        Assert.Contains("position 26", error.Message);
        var inner = Assert.IsType<XamlLoadException>(error.InnerException);
        Assert.DoesNotContain("'Padding' is not a dependency property of Border", inner.Message);
        Assert.DoesNotContain("SortByCategoryDrawingBrush", inner.Message);
        Assert.DoesNotContain("BAML+0x196F6", inner.Message);
        Assert.DoesNotContain("SortByNameDrawingBrush", inner.Message);
        Assert.DoesNotContain("TabItemMenu", inner.Message);
        Assert.DoesNotContain("BAML+0x19D99", inner.Message);
        Assert.DoesNotContain("well-known type id -105", inner.Message);
        Assert.DoesNotContain("BAML+0x19DCE", inner.Message);
        Assert.DoesNotContain("HandyControl.Properties.Langs.Lang.Close", inner.Message);
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
        Assert.DoesNotContain("well-known type id -620", inner.Message);
    }

    [Fact]
    public void Staged_shape_tab_menu_resolves_commands_live()
    {
        // The staged Command half lands live: a crafted dictionary carries
        // the real staged interactivity xmlns/namespace-mapping rows and the
        // verbatim staged "interactivity:ControlCommands.Close/CloseAll"
        // texts, with a working test-assembly static behind each Header, so
        // the non-shared menu materializes with the converted
        // ControlCommands singletons as its row commands and fresh
        // MenuItem/ContextMenu instances per lookup.
        LoadStagedHandyControlEntry();
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var controlCommands = handyControl.GetType("HandyControl.Interactivity.ControlCommands")!;
        var close = controlCommands.GetProperty("Close", BindingFlags.Public | BindingFlags.Static)!.GetValue(null);
        var closeAll = controlCommands.GetProperty("CloseAll", BindingFlags.Public | BindingFlags.Static)!.GetValue(null);
        Assert.IsType<RoutedCommand>(close);
        Assert.IsType<RoutedCommand>(closeAll);
        var dictionary = BamlReader.LoadDictionary(StagedCommandMenuDictionary());
        var first = (ContextMenu)dictionary["TabItemMenu"]!;
        Assert.Equal(2, first.Items.Count);
        var firstClose = Assert.IsType<MenuItem>(first.Items[0]);
        var firstCloseAll = Assert.IsType<MenuItem>(first.Items[1]);
        Assert.Equal("Close", firstClose.Header);
        Assert.Equal("Other", firstCloseAll.Header);
        Assert.Same(close, firstClose.Command);
        Assert.Same(closeAll, firstCloseAll.Command);
        var second = (ContextMenu)dictionary["TabItemMenu"]!;
        Assert.NotSame(first, second);
        Assert.NotSame(firstClose, Assert.IsType<MenuItem>(second.Items[0]));
        Assert.Same(close, Assert.IsType<MenuItem>(second.Items[0]).Command);
        Assert.Same(closeAll, Assert.IsType<MenuItem>(second.Items[1]).Command);
    }

    [Fact]
    public void Staged_shape_tab_menu_header_lands_live()
    {
        // Slice-145 rewrite: the converter attribute-scope fix plus the
        // re-package let the converted Lang statics read through the real
        // ResourceManager, so the staged-verbatim Header statics land as
        // live header strings (mirrored staged table ids: assembly 0,
        // TypeInfo 55 Lang, AttributeInfo 77-79, string 150, the real
        // interactivity mapping rows).
        LoadStagedHandyControlEntry();
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var lang = handyControl.GetType("HandyControl.Properties.Langs.Lang")!;
        var closeText = (string)lang.GetProperty("Close", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var closeAllText = (string)lang.GetProperty("CloseAll", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var closeOtherText = (string)lang.GetProperty("CloseOther", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        var entry = theme[0x19D99..0x19E4B].ToArray();
        var dictionary = BamlReader.LoadDictionary(StagedHeaderMenuDictionary(entry));
        var menu = (ContextMenu)dictionary["TabItemMenu"]!;
        Assert.Equal(3, menu.Items.Count);
        Assert.Equal(closeText, Assert.IsType<MenuItem>(menu.Items[0]).Header);
        Assert.Equal(closeAllText, Assert.IsType<MenuItem>(menu.Items[1]).Header);
        Assert.Equal(closeOtherText, Assert.IsType<MenuItem>(menu.Items[2]).Header);
    }

    [Fact]
    public void Crafted_menu_lands_shared_once()
    {
        // A shared keyed ContextMenu entry lands once: both lookups return
        // the same ContextMenu holding the same resolved rows.
        var dictionary = BamlReader.LoadDictionary(SharedTestMenuDictionary());
        var first = (ContextMenu)dictionary["TabItemMenu"]!;
        Assert.Equal(2, first.Items.Count);
        var firstClose = Assert.IsType<MenuItem>(first.Items[0]);
        Assert.Equal("Close", firstClose.Header);
        Assert.Same(Slice144Statics.CloseCommand, firstClose.Command);
        Assert.Same(first, dictionary["TabItemMenu"]);
    }

    [Fact]
    public void Menu_entry_with_wrong_target_type_fails_precise()
    {
        // A keyed MenuItem entry is not a ContextMenu entry: routing fails
        // naming the element instead of landing a menu.
        var entry = TestMenuEntryHead(-403).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(TestMenuDictionary(true, entry)));
        Assert.Contains("keyed entry 'TabItemMenu' holds well-known type id -403", error.Message);
    }

    [Fact]
    public void Menu_entry_without_items_content_fails_precise()
    {
        // A ContextMenu entry without the Items content member fails naming
        // the record instead of landing an empty menu.
        var entry = new B().Raw(0x03).I16(-105).Raw(0x00).Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(TestMenuDictionary(true, entry)));
        Assert.Contains("holds record 0x04 (ContextMenu entries take the Items content", error.Message);
    }

    [Fact]
    public void Menu_entry_with_wrong_content_member_fails_precise()
    {
        // A ContextMenu entry over another content member fails naming the
        // member instead of landing rows under it.
        var entry = new B().Raw(0x03).I16(-105).Raw(0x00).Raw(0x2E).I16(-161).Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(TestMenuDictionary(true, entry)));
        Assert.Contains("content member -161 on context-menu entry 'TabItemMenu'", error.Message);
    }

    [Fact]
    public void Menu_entry_with_empty_menu_fails_precise()
    {
        // An empty ContextMenu entry fails instead of landing a menu with no
        // rows to exercise.
        var entry = TestMenuEntry(Array.Empty<byte[]>());
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(TestMenuDictionary(true, entry)));
        Assert.Contains("holds an empty ContextMenu", error.Message);
    }

    [Fact]
    public void Menu_entry_with_non_menu_child_fails_precise()
    {
        // A non-MenuItem element inside the Items content fails naming the
        // element instead of landing it as a row.
        var row = new B().Raw(0x03).I16(-102).Raw(0x00).Raw(0x04).ToArray();
        var entry = TestMenuEntry(new[] { row });
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(TestMenuDictionary(true, entry)));
        Assert.Contains("element ContentPresenter in context-menu entry 'TabItemMenu'", error.Message);
    }

    [Fact]
    public void Menu_command_with_wrong_converter_fails_precise()
    {
        // A Command conversion without the Command converter fails naming
        // the converter instead of guessing the text.
        var entry = TestMenuEntry(new[] { TestMenuRow("t:Slice144Statics.CloseCommand", -195, 1) });
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(TestMenuDictionary(true, entry)));
        Assert.Contains("Command value with converter -195", error.Message);
    }

    [Fact]
    public void Menu_command_with_unknown_prefix_fails_precise()
    {
        // A Command naming an undeclared xmlns prefix fails naming the
        // prefix instead of searching assemblies for the type.
        var entry = TestMenuEntry(new[] { TestMenuRow("nope:Slice144Statics.CloseCommand", -94, 1) });
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(TestMenuDictionary(true, entry)));
        Assert.Contains("names unknown xmlns prefix 'nope'", error.Message);
    }

    [Fact]
    public void Menu_command_without_qualified_text_fails_precise()
    {
        // A bare Command name outside the canonical holders fails naming
        // the text (bare canonical names like Copy resolve; unknown bare
        // text never binds).
        var entry = TestMenuEntry(new[] { TestMenuRow("CloseCommand", -94, 1) });
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(TestMenuDictionary(true, entry)));
        Assert.Contains("'CloseCommand'", error.Message, StringComparison.Ordinal);
        Assert.Contains("names an unknown command", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Menu_command_with_unknown_member_fails_precise()
    {
        // A Command naming a missing static fails naming the member and the
        // owner instead of landing a null command.
        var entry = TestMenuEntry(new[] { TestMenuRow("t:Slice144Statics.Nope", -94, 1) });
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(TestMenuDictionary(true, entry)));
        Assert.Contains("names no public static property or field 'Nope' on 'DesktopWasm.Wpf.Tests.Slice144Statics'", error.Message);
    }

    [Fact]
    public void Menu_command_with_non_command_value_fails_precise()
    {
        // A Command static resolving to a non-command value fails naming
        // both types instead of landing the row without a command.
        var entry = TestMenuEntry(new[] { TestMenuRow("t:Slice144Statics.CloseText", -94, 1) });
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(TestMenuDictionary(true, entry)));
        Assert.Contains("is a System.String, not a ICommand", error.Message);
    }

    [Fact]
    public void Menu_row_without_command_fails_precise()
    {
        // A row without its converted Command fails instead of landing a
        // header-only row.
        var entry = TestMenuEntry(new[] { TestMenuHeaderOnlyRow(1) });
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(TestMenuDictionary(true, entry)));
        Assert.Contains("has no Command", error.Message);
    }

    [Fact]
    public void Menu_row_without_header_fails_precise()
    {
        // A row without its x:Static Header fails instead of landing a
        // command-only row.
        var entry = TestMenuEntry(new[] { TestMenuCommandOnlyRow("t:Slice144Statics.CloseCommand") });
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(TestMenuDictionary(true, entry)));
        Assert.Contains("has no Header", error.Message);
    }

    [Fact]
    public void Menu_row_with_duplicate_command_fails_precise()
    {
        // A row carrying two Command values fails instead of picking one.
        var row = new B().Raw(0x03).I16(-403).Raw(0x00)
            .Sized(0x24, p => p.I16(0).Str("t:Slice144Statics.CloseCommand").I16(-94))
            .Sized(0x24, p => p.I16(0).Str("t:Slice144Statics.CloseCommand").I16(-94))
            .Raw(0x23).I16(-71).U16(602).I16(1)
            .Raw(0x04).ToArray();
        var entry = TestMenuEntry(new[] { row });
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(TestMenuDictionary(true, entry)));
        Assert.Contains("duplicate Command on the MenuItem row", error.Message);
    }

    [Fact]
    public void Menu_header_with_wrong_target_fails_precise()
    {
        // A Header extension over another member fails naming the member
        // instead of landing it as the header.
        var row = new B().Raw(0x03).I16(-403).Raw(0x00)
            .Sized(0x24, p => p.I16(0).Str("t:Slice144Statics.CloseCommand").I16(-94))
            .Raw(0x23).I16(-100).U16(602).I16(1)
            .Raw(0x04).ToArray();
        var entry = TestMenuEntry(new[] { row });
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(TestMenuDictionary(true, entry)));
        Assert.Contains("menu header targets member -100", error.Message);
    }

    [Fact]
    public void Menu_header_with_wrong_extension_fails_precise()
    {
        // A Header extension that is not x:Static fails naming the extension
        // instead of resolving it as a static.
        var row = new B().Raw(0x03).I16(-403).Raw(0x00)
            .Sized(0x24, p => p.I16(0).Str("t:Slice144Statics.CloseCommand").I16(-94))
            .Raw(0x23).I16(-71).U16(634).I16(1)
            .Raw(0x04).ToArray();
        var entry = TestMenuEntry(new[] { row });
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(TestMenuDictionary(true, entry)));
        Assert.Contains("menu header holds extension 634", error.Message);
    }

    [Fact]
    public void Menu_header_with_bad_static_id_fails_precise()
    {
        // A Header static id without an AttributeInfo row fails naming the
        // id instead of resolving a phantom static.
        var row = new B().Raw(0x03).I16(-403).Raw(0x00)
            .Sized(0x24, p => p.I16(0).Str("t:Slice144Statics.CloseCommand").I16(-94))
            .Raw(0x23).I16(-71).U16(602).I16(99)
            .Raw(0x04).ToArray();
        var entry = TestMenuEntry(new[] { row });
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(TestMenuDictionary(true, entry)));
        Assert.Contains("menu header static id 99", error.Message);
    }

    [Fact]
    public void Menu_header_with_framework_owner_fails_precise()
    {
        // A Header static owned by a well-known id fails as a follow-up
        // instead of resolving framework statics the entry machine does not
        // shape yet.
        var tables = TestMenuTables(ownedFrameworkStatic: true).Concat(TestMenuXmlns()).ToArray();
        var row = new B().Raw(0x03).I16(-403).Raw(0x00)
            .Sized(0x24, p => p.I16(0).Str("t:Slice144Statics.CloseCommand").I16(-94))
            .Raw(0x23).I16(-71).U16(602).I16(3)
            .Raw(0x04).ToArray();
        var entry = TestMenuEntry(new[] { row });
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(MenuDictionary(tables, true, entry)));
        Assert.Contains("names owner id -403 (menu headers take converted statics", error.Message);
    }

    [Fact]
    public void Menu_row_with_extra_record_fails_precise()
    {
        // Any other record inside a MenuItem row fails naming the record
        // instead of skipping it.
        var row = new B().Raw(0x03).I16(-403).Raw(0x00)
            .Sized(0x24, p => p.I16(0).Str("t:Slice144Statics.CloseCommand").I16(-94))
            .Raw(0x23).I16(-71).U16(602).I16(1)
            .Raw(0x2E).I16(-192)
            .Raw(0x04).ToArray();
        var entry = TestMenuEntry(new[] { row });
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(TestMenuDictionary(true, entry)));
        Assert.Contains("holds record 0x2E (menu rows take a converted Command plus an x:Static Header", error.Message);
    }

    private static byte[] TestMenuEntryHead(short typeId) =>
        new B().Raw(0x03).I16(typeId).Raw(0x00).ToArray();

    private static byte[] TestMenuEntry(byte[][] rows)
    {
        var b = new B().Raw(0x03).I16(-105).Raw(0x00).Raw(0x2E).I16(-192);
        foreach (var row in rows) b.Raw(row);
        return b.Raw(0x04).ToArray();
    }

    private static byte[] TestMenuRow(string commandText, short conv, short headerStatic) =>
        new B().Raw(0x03).I16(-403).Raw(0x00)
            .Sized(0x24, p => p.I16(0).Str(commandText).I16(conv))
            .Raw(0x23).I16(-71).U16(602).I16(headerStatic)
            .Raw(0x04).ToArray();

    private static byte[] TestMenuHeaderOnlyRow(short headerStatic) =>
        new B().Raw(0x03).I16(-403).Raw(0x00)
            .Raw(0x23).I16(-71).U16(602).I16(headerStatic)
            .Raw(0x04).ToArray();

    private static byte[] TestMenuCommandOnlyRow(string commandText) =>
        new B().Raw(0x03).I16(-403).Raw(0x00)
            .Sized(0x24, p => p.I16(0).Str(commandText).I16(-94))
            .Raw(0x04).ToArray();

    private static byte[] TestMenuTables(bool ownedFrameworkStatic = false)
    {
        // Per-file ids mirror the staged relationships with test-assembly
        // statics: assembly 0 is the test assembly, TypeInfo 0 names the
        // Slice144Statics holder, AttributeInfo 0 is the MenuItem-owned
        // Command row, 1/2 are the holder string members (plus an optional
        // framework-owned row 3 for the follow-up pin), string 0 is the key.
        var testAssembly = typeof(Slice144BamlTabMenuTests).Assembly.FullName!;
        var holder = typeof(Slice144Statics).FullName!;
        var b = new B()
            .Sized(0x1C, p => p.U16(0).Str(testAssembly))
            .Sized(0x1D, p => p.U16(0).U16(0).Str(holder))
            .Sized(0x1F, p => p.U16(0).I16(-403).Raw(0x00).Str("Command"))
            .Sized(0x1F, p => p.U16(1).I16(0).Raw(0x00).Str("CloseText"))
            .Sized(0x1F, p => p.U16(2).I16(0).Raw(0x00).Str("OtherText"));
        if (ownedFrameworkStatic)
            b = b.Sized(0x1F, p => p.U16(3).I16(-403).Raw(0x00).Str("FrameworkStatic"));
        return b
            .Sized(0x20, p => p.U16(0).Str("TabItemMenu"))
            .ToArray();
    }

    private static byte[] TestMenuXmlns() =>
        new B()
            .Sized(0x14, p => p.Str("t").Str("urn:slice144-tests").U16(1).U16(0))
            .Sized(0x1B, p => p.Str("urn:slice144-tests").Str("DesktopWasm.Wpf.Tests").U16(0))
            .ToArray();

    private static byte[] SharedTestMenuDictionary()
    {
        var rows = new[]
        {
            TestMenuRow("t:Slice144Statics.CloseCommand", -94, 1),
            TestMenuRow("t:Slice144Statics.CloseCommand", -94, 2),
        };
        return MenuDictionary(TestMenuTables().Concat(TestMenuXmlns()).ToArray(), true, TestMenuEntry(rows));
    }

    private static byte[] TestMenuDictionary(bool shared, byte[] entry) =>
        MenuDictionary(TestMenuTables().Concat(TestMenuXmlns()).ToArray(), shared, entry);

    private static byte[] StagedCommandMenuDictionary()
    {
        // The real staged interactivity mapping rows (verbatim xmlns and
        // namespace strings, assembly 0 = the loaded converted HandyControl
        // image) behind the verbatim staged command texts, with a working
        // test-assembly static behind each Header so the menu lands live.
        LoadStagedHandyControlEntry();
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var testAssembly = typeof(Slice144BamlTabMenuTests).Assembly.FullName!;
        var holder = typeof(Slice144Statics).FullName!;
        var tables = new B()
            .Sized(0x1C, p => p.U16(0).Str(handyControl.FullName!))
            .Sized(0x1C, p => p.U16(1).Str(testAssembly))
            .Sized(0x1D, p => p.U16(0).U16(1).Str(holder))
            .Sized(0x1F, p => p.U16(0).I16(-403).Raw(0x00).Str("Command"))
            .Sized(0x1F, p => p.U16(1).I16(0).Raw(0x00).Str("CloseText"))
            .Sized(0x1F, p => p.U16(2).I16(0).Raw(0x00).Str("OtherText"))
            .Sized(0x20, p => p.U16(0).Str("TabItemMenu"))
            .ToArray();
        var xmlns = new B()
            .Sized(0x14, p => p.Str("interactivity").Str("clr-namespace:HandyControl.Interactivity").U16(1).U16(0))
            .Sized(0x1B, p => p.Str("clr-namespace:HandyControl.Interactivity").Str("HandyControl.Interactivity").U16(0))
            .ToArray();
        var rows = new[]
        {
            TestMenuRow("interactivity:ControlCommands.Close", -94, 1),
            TestMenuRow("interactivity:ControlCommands.CloseAll", -94, 2),
        };
        return MenuDictionary(tables.Concat(xmlns).ToArray(), false, TestMenuEntry(rows));
    }

    private static byte[] StagedHeaderMenuDictionary(byte[] stagedEntry)
    {
        // The staged table ids mirrored with dummies (assembly 0, TypeInfo
        // 55 Lang, AttributeInfo 76-79, string 150, the real interactivity
        // mapping rows) around the staged-verbatim entry bytes.
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

/// <summary>Working statics behind the crafted menu dictionaries: the
/// header strings plus a shared engine command singleton, so the entry
/// machine can be exercised end to end without the converted
/// resource-backed statics (whose assembly attributes the converter
/// mis-scoped).</summary>
public static class Slice144Statics
{
    public static string CloseText => "Close";
    public static string OtherText => "Other";
    public static ICommand CloseCommand { get; } = ApplicationCommands.Copy;
}
