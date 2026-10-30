// Copyright (c) 2026 The WPF on WASM Contributors.
// Licensed under the MIT License. See LICENSE in the project root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-154: the implicit RibbonTab style entry's short-form
/// ItemsPanel setter (well-known member id -81 holding the ItemsPanelTemplate
/// complex with the StackPanel Orientation-Horizontal visual at BAML+0x1A92D)
/// routes as a live engine value, so the staged boot stops precise at the
/// Template setter's ControlTemplate complex (the converted SimplePanel
/// PART_RootContainer root carrying Visibility Collapsed at BAML+0x1A990:
/// root panels take the Name string).
/// Live counts verified first: handycontrol conversion-failed/6 staged/2 gaps
/// (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched;
/// staged-oracle status-gate bypass stays in-probe only). Triangulation three
/// ways: 37 staged framing spans against the live 466350-byte theme.baml
/// (sha256 D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0,
/// verified live) - the RibbonTab entry [0x1A8EA, 0x1AA1C) head
/// 03 94 FD 00 22 00 00 3D 00 2E 12 FF (TargetType record at BAML+0x1A8EE),
/// short-form Focusable -132 with the False/BooleanConverter value, short-form
/// VerticalContentAlignment -38 with the Center/EnumConverter value, short-form
/// ItemsPanel -81 with the ItemsPanelTemplate complex (07 02 00 03 B6 FE 00
/// 2E 52 FF 03 A7 FD 00 plus the Orientation Horizontal/EnumConverter PWC plus
/// the 04 04 08 04 close run), short-form Template -37 with the
/// ControlTemplate/TargetType-61/VisualTree/per-file-46-root head plus the
/// PART_RootContainer Name and Visibility Collapsed PWCs (the stop at
/// BAML+0x1A990), the Children content plus mainBorder Border head with the
/// BorderBrush/BorderThickness/Background TemplateBindings and the Margin PWC,
/// the ScrollViewer head, the 04 04 04 04 08 04 04 close run, the next head
/// 03 BE FD 00, the key record 0x26 keyidx A4 at 0xB609, the key-string row
/// 0xA4 'LightPrimaryBrush' at 0x2807, the TypeInfo 46
/// 'HandyControl.Controls.SimplePanel' row at 0x1B3F and the TypeInfo 61
/// RibbonTab row at 0x2793; the live upstream HandyControl Ribbon.xaml
/// implicit RibbonTab block at pinned commit e9ad3d70 (Focusable False,
/// VerticalContentAlignment Center, ItemsPanel holding the
/// ItemsPanelTemplate/StackPanel Orientation-Horizontal complex, then the
/// Template holding the ControlTemplate over the collapsed
/// PART_RootContainer SimplePanel with the mainBorder Border plus the
/// ScrollViewer, fetched live as raw Ribbon.xaml); a genuine
/// net10.0-windows ResourceDictionary fixture compiling the same
/// Focusable/VerticalContentAlignment/ItemsPanel shape on a real TabControl
/// style (RibbonPanel.baml, byte-identical in every staged record shape
/// modulo per-file numbering and the 0x35/0x36 line markers the staged file
/// omits: Style head, short-form member ids, AF FF ItemsPanel member,
/// ItemsPanelTemplate/VisualTree/StackPanel complex with the Orientation
/// EnumConverter PWC; fixture retained at E:/tmp/bamlfix/RibbonPanel.xaml
/// outside the tree) plus a genuine net10.0-windows reflection probe
/// (ItemsControl.ItemsPanelProperty owner ItemsControl, type
/// ItemsPanelTemplate, null default; TabControl : Selector : ItemsControl;
/// RibbonTab : HeaderedItemsControl : ItemsControl; probe retained at
/// E:/tmp/rbprobe outside the tree). One mid-slice correction: the next stop
/// was first predicted as the ScrollViewer child of the template root (the
/// Border parses, so the ScrollViewer looked next), but the live boot stops
/// earlier at the root panel's own Visibility PWC - the root takes only the
/// Name string. Real-behavior engine work, never fabricated: ItemsControl
/// gains ItemsPanelProperty (the DP store is the value authority with a
/// Regenerate metadata callback; the CLR wrapper keeps the converted
/// ElementGroup/ButtonGroup fallback, so explicit panels always win) and
/// BamlReader gains KnownItemsPanelMember -81 (ShortMemberDisplay/
/// WellKnownMemberName/ShortMemberList arms plus the ItemsPanelTemplate
/// branch in ParseComplexSetterValue reusing the keyed-entry panel parser;
/// the complex-setter contract message grows to 'a ControlTemplate, a Binding,
/// an ItemsPanelTemplate or a NullExtension' with 3 contract re-pins. The
/// RibbonTab entry parses its first three setters and its ItemsPanel complex
/// (the panel retains its StackPanel Orientation-Horizontal root) and stops
/// precise at the Template complex; the style never lands half-built. Tests:
/// 12 new Slice154BamlItemsPanelTests (hash-verifying 6/2 manifest oracle,
/// 37-span staged framing, boot advance to the new stop, full-entry staged
/// Template-root stop, 7 synthetic landing/negative pins incl. the
/// ItemsPanel-on-TextBlock and ItemsPanelTemplate-on-Template pins, the
/// engine-shape WPF-oracle pins) plus the Slice153 stop-assert flips plus
/// the HandyControlEntryBootTests triple flip plus the 3 complex-setter
/// message re-pins plus the BamlReader class-doc frontier rewrite.
/// application-compatibility-status.md handycontrol row verified current
/// against the live manifest (conversion-failed/6/2; no regen, nothing
/// re-packaged). No browser proof - package stays conversion-failed.
/// Precise next blockers: the template root panel's own Visibility PWC
/// (plus the Border-with-TemplateBindings child and the ScrollViewer child
/// at BAML+0x1A9DD behind it) on the RibbonTab Template complex, then the
/// keyed 'LightPrimaryBrush' entry (keyidx A4), the CarouselPateButton/implicit
/// Carousel styles, the GroupBox styles with BasedOn/DynamicResource setters
/// and TitleElement triggers, EventTriggers, the line-info trailer, the
/// per-file (Type,Name) x:Name rows and the converted-lib elements
/// (CircleProgressBar/GifImage/Watermark/Drawer/ElementGroup/SearchBar)
/// behind the line-17 SkinDefault stop.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice154BamlItemsPanelTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice154BamlItemsPanelTests()
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
    public void Theme_baml_routes_ribbontab_items_panel_bytes()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        Assert.Equal(
            "D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0",
            Convert.ToHexString(SHA256.HashData(theme)));

        // The implicit RibbonTab style entry [0x1A8EA, 0x1AA1C): head with the
        // TargetType record plus the Setters content member.
        var ribbonHead = new byte[] { 0x03, 0x94, 0xFD, 0x00, 0x22, 0x00, 0x00, 0x3D, 0x00, 0x2E, 0x12, 0xFF };
        Assert.Equal(ribbonHead, theme[0x1A8EA..0x1A8F6]);
        // Focusable setter: short-form -132 plus False/BooleanConverter.
        var focusSetter = new byte[] { 0x03, 0xD4, 0xFD, 0x00, 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0x7C, 0xFF, 0x24, 0x0B, 0x02, 0x00, 0x05, 0x46, 0x61, 0x6C, 0x73, 0x65, 0xD2, 0xFF };
        Assert.Equal(focusSetter, theme[0x1A8F6..0x1A90E]);
        Assert.Equal(0x04, theme[0x1A90E]);
        // VerticalContentAlignment setter: short-form -38 plus Center/EnumConverter.
        var vcontentSetter = new byte[] { 0x03, 0xD4, 0xFD, 0x00, 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xDA, 0xFF, 0x24, 0x0C, 0x02, 0x00, 0x06, 0x43, 0x65, 0x6E, 0x74, 0x65, 0x72, 0x3D, 0xFF };
        Assert.Equal(vcontentSetter, theme[0x1A90F..0x1A928]);
        Assert.Equal(0x04, theme[0x1A928]);
        // ItemsPanel setter: short-form -81 at BAML+0x1A92D holding the
        // ItemsPanelTemplate complex with the StackPanel Orientation-Horizontal
        // visual, closed by 04 04 08 04.
        var itemsPanelSetterHead = new byte[] { 0x03, 0xD4, 0xFD, 0x00 };
        Assert.Equal(itemsPanelSetterHead, theme[0x1A929..0x1A92D]);
        var itemsPanelProp = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xAF, 0xFF };
        Assert.Equal(itemsPanelProp, theme[0x1A92D..0x1A935]);
        var itemsPanelComplexHead = new byte[] { 0x07, 0x02, 0x00, 0x03, 0xB6, 0xFE, 0x00, 0x2E, 0x52, 0xFF, 0x03, 0xA7, 0xFD, 0x00 };
        Assert.Equal(itemsPanelComplexHead, theme[0x1A935..0x1A943]);
        var orientationPwc = new byte[] { 0x24, 0x10, 0x08, 0x00, 0x0A, 0x48, 0x6F, 0x72, 0x69, 0x7A, 0x6F, 0x6E, 0x74, 0x61, 0x6C, 0x3D, 0xFF };
        Assert.Equal(orientationPwc, theme[0x1A943..0x1A954]);
        var itemsPanelClose = new byte[] { 0x04, 0x04, 0x08, 0x04 };
        Assert.Equal(itemsPanelClose, theme[0x1A954..0x1A958]);
        // Template setter head: short-form -37 plus the ControlTemplate complex
        // over the per-file panel root with the PART_RootContainer Name PWC,
        // then the Visibility Collapsed stop at BAML+0x1A990.
        var templateSetterHead = new byte[] { 0x03, 0xD4, 0xFD, 0x00 };
        Assert.Equal(templateSetterHead, theme[0x1A958..0x1A95C]);
        var templateProp = new byte[] { 0x06, 0x07, 0x01, 0x00, 0x89, 0x00, 0xDB, 0xFF };
        Assert.Equal(templateProp, theme[0x1A95C..0x1A964]);
        var templateComplexHead = new byte[] { 0x07, 0x02, 0x00, 0x03, 0x94, 0xFF, 0x00 };
        Assert.Equal(templateComplexHead, theme[0x1A964..0x1A96B]);
        var templateTargetHead = new byte[] { 0x22, 0x07, 0x00, 0x3D, 0x00, 0x2E, 0x52, 0xFF };
        Assert.Equal(templateTargetHead, theme[0x1A96B..0x1A973]);
        var templateRootHead = new byte[] { 0x03, 0x2E, 0x00, 0x00 };
        Assert.Equal(templateRootHead, theme[0x1A973..0x1A977]);
        var rootNameHead = new byte[] { 0x24, 0x18, 0x2E, 0x00, 0x12 };
        Assert.Equal(rootNameHead, theme[0x1A977..0x1A97C]);
        Assert.Equal(Encoding.ASCII.GetBytes("PART_RootContainer"), theme[0x1A97C..0x1A98E]);
        var rootNameConverter = new byte[] { 0x99, 0xFD };
        Assert.Equal(rootNameConverter, theme[0x1A98E..0x1A990]);
        var visibilityPwc = new byte[] { 0x24, 0x0F, 0x79, 0xFF, 0x09, 0x43, 0x6F, 0x6C, 0x6C, 0x61, 0x70, 0x73, 0x65, 0x64, 0x3D, 0xFF };
        Assert.Equal(visibilityPwc, theme[0x1A990..0x1A9A0]);
        // Past the stop: the Children content, the mainBorder Border head with
        // its three TemplateBinding/PWC transfers, the Border end and the
        // ScrollViewer head the template continues with.
        var childrenContent = new byte[] { 0x2E, 0x2B, 0xFF };
        Assert.Equal(childrenContent, theme[0x1A9A0..0x1A9A3]);
        var borderHead = new byte[] { 0x03, 0xCE, 0xFF, 0x00 };
        Assert.Equal(borderHead, theme[0x1A9A3..0x1A9A7]);
        var mainBorderName = new byte[] { 0x24, 0x10, 0x53, 0x00, 0x0A, 0x6D, 0x61, 0x69, 0x6E, 0x42, 0x6F, 0x72, 0x64, 0x65, 0x72, 0x99, 0xFD };
        Assert.Equal(mainBorderName, theme[0x1A9A7..0x1A9B8]);
        var borderBrushBinding = new byte[] { 0x23, 0xFB, 0xFF, 0x7A, 0x02, 0xE7, 0xFF };
        Assert.Equal(borderBrushBinding, theme[0x1A9B8..0x1A9BF]);
        var borderThicknessPwc = new byte[] { 0x24, 0x0D, 0xFA, 0xFF, 0x07, 0x30, 0x2C, 0x30, 0x2C, 0x30, 0x2C, 0x33, 0x71, 0xFD };
        Assert.Equal(borderThicknessPwc, theme[0x1A9BF..0x1A9CD]);
        var backgroundBinding = new byte[] { 0x23, 0xFC, 0xFF, 0x7A, 0x02, 0xE8, 0xFF };
        Assert.Equal(backgroundBinding, theme[0x1A9CD..0x1A9D4]);
        var marginPwc = new byte[] { 0x24, 0x07, 0xCF, 0xFF, 0x01, 0x30, 0x71, 0xFD };
        Assert.Equal(marginPwc, theme[0x1A9D4..0x1A9DC]);
        Assert.Equal(0x04, theme[0x1A9DC]);
        var scrollViewerHead = new byte[] { 0x03, 0xDA, 0xFD, 0x00 };
        Assert.Equal(scrollViewerHead, theme[0x1A9DD..0x1A9E1]);
        // The style close run, the next entry head, its key record and the
        // key-string row, plus the panel/type rows the entry rides.
        var entryClose = new byte[] { 0x04, 0x04, 0x04, 0x04, 0x08, 0x04, 0x04 };
        Assert.Equal(entryClose, theme[0x1AA15..0x1AA1C]);
        var nextHead = new byte[] { 0x03, 0xBE, 0xFD, 0x00 };
        Assert.Equal(nextHead, theme[0x1AA1C..0x1AA20]);
        var lightPrimaryKey = new byte[] { 0x26, 0x09, 0xA4, 0x00, 0x85, 0xB8, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(lightPrimaryKey, theme[0xB609..0xB613]);
        var lightPrimaryKeyRow = new byte[] { 0x20, 0x15, 0xA4, 0x00, 0x11 };
        Assert.Equal(lightPrimaryKeyRow, theme[0x2807..0x280C]);
        Assert.Equal(Encoding.ASCII.GetBytes("LightPrimaryBrush"), theme[0x280C..0x281D]);
        var simplePanelTypeHead = new byte[] { 0x1D, 0x27, 0x2E, 0x00, 0x00, 0x00, 0x21 };
        Assert.Equal(simplePanelTypeHead, theme[0x1B3F..0x1B46]);
        Assert.Equal(Encoding.ASCII.GetBytes("HandyControl.Controls.SimplePanel"), theme[0x1B46..0x1B67]);
        var ribbonTypeHead = new byte[] { 0x1D, 0x25, 0x3D, 0x00, 0x00, 0x00, 0x1F };
        Assert.Equal(ribbonTypeHead, theme[0x2793..0x279A]);
        Assert.Equal(Encoding.ASCII.GetBytes("HandyControl.Controls.RibbonTab"), theme[0x279A..0x27B9]);
    }

    [Fact]
    public void Staged_boot_advances_past_ribbontab_to_template_root_stop()
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
        Assert.DoesNotContain("BAML+0x1A9DD", inner.Message);
        Assert.DoesNotContain("element well-known type id -550 on the template root panel", inner.Message);
        Assert.DoesNotContain("on setter 'Template'", inner.Message);
        Assert.DoesNotContain("implicit style key 'HandyControl.Controls.RibbonTab'", inner.Message);
        Assert.DoesNotContain("root panels take the Name string plus Visibility", inner.Message);
        Assert.DoesNotContain("BAML+0x1A9DD", inner.Message);
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
        Assert.DoesNotContain("Carousel", inner.Message);
        Assert.DoesNotContain("GroupBox", inner.Message);
        Assert.DoesNotContain("templates take a ContentPresenter", inner.Message);
        Assert.DoesNotContain("items panels take a UniformGrid/StackPanel/VirtualizingStackPanel/WrapPanel/DockPanel visual", inner.Message);
    }

    [Fact]
    public void Staged_ribbontab_entry_lands_with_scrollviewer_child()
    {
        // The full implicit RibbonTab entry parses its first three setters
        // (Focusable, VerticalContentAlignment, ItemsPanel with the
        // StackPanel-Horizontal panel) plus the Template setter's
        // ControlTemplate complex: the converted SimplePanel root lands its
        // PART_RootContainer Name PWC, its Visibility Collapsed PWC, the
        // Children content, the mainBorder Border child and the ScrollViewer
        // child at BAML+0x1A9DD (Orientation Horizontal plus Hidden/Disabled
        // scrollbar policies plus the ItemsPresenter child), landing as a
        // live engine Style. The staged HandyControl image must be loaded so
        // the per-file panel, target and attached-owner types resolve like
        // the boot path.
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(306, 0x1AA1C - 0x1A8EA);
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var ribbonTab = handyControl.GetType("HandyControl.Controls.RibbonTab");
        Assert.NotNull(ribbonTab);
        var scrollAttach = handyControl.GetType("HandyControl.Controls.ScrollViewerAttach");
        Assert.NotNull(scrollAttach);
        var dictionary = BamlReader.LoadDictionary(
            RibbonFullDictionary(theme[0x1A8EA..0x1AA1C], "RibbonTab"));
        var style = Assert.IsType<Style>(dictionary[ribbonTab]);
        Assert.Same(ribbonTab, style.TargetType);
        var byName = style.Setters.OfType<Setter>().ToDictionary(s => s.Property.Name);
        Assert.Equal(4, byName.Count);
        Assert.Equal(false, byName["Focusable"].Value);
        Assert.Equal(VerticalAlignment.Center, byName["VerticalContentAlignment"].Value);
        var template = Assert.IsType<ControlTemplate>(byName["Template"].Value);
        Assert.Same(ribbonTab, template.TargetType);
        var root = Assert.IsType<SimplePanel>(template.VisualTree);
        Assert.Equal("PART_RootContainer", root.GetValue(FrameworkElement.NameProperty));
        Assert.Equal(Visibility.Collapsed, root.GetValue(UIElement.VisibilityProperty));
        Assert.Equal(2, root.Children.Count);
        Assert.IsType<Border>(root.Children[0]);
        var viewer = Assert.IsType<ScrollViewer>(root.Children[1]);
        Assert.Equal(ScrollBarVisibility.Hidden, viewer.GetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty));
        Assert.Equal(ScrollBarVisibility.Disabled, viewer.GetValue(ScrollViewer.VerticalScrollBarVisibilityProperty));
        var orientationDp = DependencyProperty.FromName(scrollAttach, "Orientation");
        Assert.NotNull(orientationDp);
        Assert.Equal(Enum.Parse(orientationDp.PropertyType, "Horizontal"), viewer.GetValue(orientationDp));
        Assert.IsType<ItemsPresenter>(viewer.Content);
        Assert.Same(style, dictionary[ribbonTab]);
    }

    [Fact]
    public void Synthetic_items_panel_lands_live()
    {
        XamlLoader.CustomElementSource = null;
        var dictionary = BamlReader.LoadDictionary(
            TabControlDictionary(ItemsPanelEntry(HorizontalPanelBody()), "S"));
        var style = Assert.IsType<Style>(dictionary["S"]);
        Assert.Same(typeof(TabControl), style.TargetType);
        var setter = Assert.Single(style.Setters.OfType<Setter>());
        Assert.Same(ItemsControl.ItemsPanelProperty, setter.Property);
        var panel = Assert.IsType<ItemsPanelTemplate>(setter.Value);
        var root = Assert.IsType<StackPanel>(panel.VisualRoot);
        Assert.Equal(Orientation.Horizontal, root.Orientation);
        Assert.Same(style, dictionary["S"]);
    }

    [Fact]
    public void Synthetic_bare_items_panel_lands_live()
    {
        XamlLoader.CustomElementSource = null;
        var dictionary = BamlReader.LoadDictionary(
            TabControlDictionary(ItemsPanelEntry(BarePanelBody()), "S"));
        var style = Assert.IsType<Style>(dictionary["S"]);
        var setter = Assert.Single(style.Setters.OfType<Setter>());
        Assert.Same(ItemsControl.ItemsPanelProperty, setter.Property);
        var panel = Assert.IsType<ItemsPanelTemplate>(setter.Value);
        var root = Assert.IsType<StackPanel>(panel.VisualRoot);
        Assert.Equal(Orientation.Vertical, root.Orientation);
    }

    [Fact]
    public void Synthetic_items_panel_and_template_compose_live()
    {
        XamlLoader.CustomElementSource = null;
        var dictionary = BamlReader.LoadDictionary(
            TabControlDictionary(ItemsPanelTemplateEntry(), "S"));
        var style = Assert.IsType<Style>(dictionary["S"]);
        Assert.Same(typeof(TabControl), style.TargetType);
        Assert.Equal(2, style.Setters.OfType<Setter>().Count());
        var byName = style.Setters.OfType<Setter>().ToDictionary(s => s.Property.Name);
        var panel = Assert.IsType<ItemsPanelTemplate>(byName["ItemsPanel"].Value);
        Assert.Equal(Orientation.Horizontal, Assert.IsType<StackPanel>(panel.VisualRoot).Orientation);
        var template = Assert.IsType<ControlTemplate>(byName["Template"].Value);
        Assert.IsType<ContentPresenter>(template.VisualTree);
        Assert.Same(style, dictionary["S"]);
    }

    [Fact]
    public void Synthetic_items_panel_and_template_apply_live()
    {
        // The styled ItemsPanel lands through the real Setter path onto a
        // live TabControl: the DP store is the value authority and the markup
        // panel inflates a live host. (A BAML-retained panel never inflates -
        // BAML-tree inflation is a follow-up - so the parsed style above is
        // inspected, never applied; the apply path runs on markup here.)
        XamlLoader.CustomElementSource = null;
        var template = new ItemsPanelTemplate("<StackPanel Orientation=\"Horizontal\" />");
        var style = new Style(typeof(TabControl));
        style.Setters.Add(new Setter(ItemsControl.ItemsPanelProperty, template));
        var tab = new TabControl();
        Assert.Null(tab.ItemsPanel);
        style.ApplyTo(tab);
        Assert.Same(template, tab.ItemsPanel);
        Assert.Same(template, tab.GetValue(ItemsControl.ItemsPanelProperty));
    }

    [Fact]
    public void Synthetic_items_panel_wrong_target_stays_precise()
    {
        XamlLoader.CustomElementSource = null;
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            StyleDictionary(WrongTargetEntry(), "S", "DesktopWasm.Wpf.TextBlock", "DesktopWasm.Wpf.TabControl")));
        Assert.Contains("'ItemsPanel' is not a dependency property of TextBlock (setter in style entry 'S')", error.Message);
    }

    [Fact]
    public void Synthetic_items_panel_template_on_template_stays_precise()
    {
        XamlLoader.CustomElementSource = null;
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            TabControlDictionary(TemplateHoldsItemsPanelEntry(), "S")));
        Assert.Contains("holds <ItemsPanelTemplate> in a complex value", error.Message);
        Assert.Contains("an ItemsPanelTemplate, a NullExtension or a TransformGroup", error.Message);
    }

    [Fact]
    public void Synthetic_items_panel_bad_visual_stays_precise()
    {
        XamlLoader.CustomElementSource = null;
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            TabControlDictionary(ItemsPanelEntry(BorderPanelBody()), "S")));
        Assert.Contains("on items-panel entry 'S' (items panels take a UniformGrid/StackPanel/VirtualizingStackPanel/WrapPanel/DockPanel visual; style-surface follow-up)", error.Message);
    }

    [Fact]
    public void ItemsPanel_engine_shape_matches_wpf()
    {
        // The new DP matches the genuine net10.0-windows shape: owner
        // ItemsControl, ItemsPanelTemplate-typed, null default, resolving
        // through the Selector/TabControl hierarchy walk and absent elsewhere.
        Assert.Same(typeof(ItemsControl), ItemsControl.ItemsPanelProperty.OwnerType);
        Assert.Same(typeof(ItemsPanelTemplate), ItemsControl.ItemsPanelProperty.PropertyType);
        Assert.Null(ItemsControl.ItemsPanelProperty.DefaultMetadata.DefaultValue);
        Assert.Same(ItemsControl.ItemsPanelProperty, DependencyProperty.FromName(typeof(ItemsControl), "ItemsPanel"));
        Assert.Same(ItemsControl.ItemsPanelProperty, DependencyProperty.FromName(typeof(Selector), "ItemsPanel"));
        Assert.Same(ItemsControl.ItemsPanelProperty, DependencyProperty.FromName(typeof(TabControl), "ItemsPanel"));
        Assert.Null(DependencyProperty.FromName(typeof(TextBlock), "ItemsPanel"));
        Assert.True(typeof(ItemsControl).IsAssignableFrom(typeof(TabControl)));
        Assert.Null(new TabControl().ItemsPanel);
        // Direct sets dual-land the DP store and regenerate exactly once per
        // real change; the converted group fallback still applies only when
        // no explicit panel is set.
        var tab = new TabControl();
        var first = new ItemsPanelTemplate("<StackPanel />");
        tab.ItemsPanel = first;
        Assert.Same(first, tab.ItemsPanel);
        Assert.Same(first, tab.GetValue(ItemsControl.ItemsPanelProperty));
        tab.ItemsPanel = first;
        Assert.Same(first, tab.GetValue(ItemsControl.ItemsPanelProperty));
        tab.ItemsPanel = null;
        Assert.Null(tab.ItemsPanel);
        Assert.Null(tab.GetValue(ItemsControl.ItemsPanelProperty));
    }

    private static byte[] ItemsPanelEntry(byte[] panelBody)
    {
        return new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(0)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-81))
            .Raw(panelBody)
            .Raw(0x04)
            .Raw(0x04).ToArray();
    }

    private static byte[] HorizontalPanelBody()
    {
        return new B()
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-330).Raw(0x00)
            .Raw(0x2E).I16(-174)
            .Raw(0x03).I16(-601).Raw(0x00)
            .Sized(0x24, q => q.I16(3).Str("Horizontal").I16(-195))
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x08).ToArray();
    }

    private static byte[] BarePanelBody()
    {
        return new B()
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-330).Raw(0x00)
            .Raw(0x2E).I16(-174)
            .Raw(0x03).I16(-601).Raw(0x00)
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x08).ToArray();
    }

    private static byte[] BorderPanelBody()
    {
        return new B()
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-330).Raw(0x00)
            .Raw(0x2E).I16(-174)
            .Raw(0x03).I16(-50).Raw(0x00).ToArray();
    }

    private static byte[] ItemsPanelTemplateEntry()
    {
        return new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(0)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-81))
            .Raw(HorizontalPanelBody())
            .Raw(0x04)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-37))
            .Raw(0x07).I16(2)
            .Raw(0x03).I16(-108).Raw(0x00)
            .Raw(0x22).I16(4).I16(1)
            .Raw(0x2E).I16(-174)
            .Raw(0x03).I16(-102).Raw(0x00)
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x08)
            .Raw(0x04)
            .Raw(0x04).ToArray();
    }

    private static byte[] TemplateHoldsItemsPanelEntry()
    {
        return new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(0)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-37))
            .Raw(HorizontalPanelBody())
            .Raw(0x04)
            .Raw(0x04).ToArray();
    }

    private static byte[] WrongTargetEntry()
    {
        return new B()
            .Raw(0x03).I16(-620).Raw(0x00)
            .Raw(0x22).I16(0).I16(0)
            .Raw(0x2E).I16(-238)
            .Raw(0x03).I16(-556).Raw(0x00)
            .Sized(0x06, q => q.I16(1).U16(137).I16(-81))
            .Sized(0x24, q => q.I16(2).Str("x").I16(-615))
            .Raw(0x04)
            .Raw(0x04).ToArray();
    }

    private static byte[] TabControlDictionary(byte[] entry, string key)
        => StyleDictionary(entry, key, "DesktopWasm.Wpf.TabControl", "DesktopWasm.Wpf.TabControl");

    private static byte[] StyleDictionary(byte[] entry, string key, string targetType, string templateTargetType)
    {
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x1F, p => p.I16(3).I16(-601).Raw(0x00).Str("Orientation"))
            .Sized(0x1F, p => p.I16(4).I16(-108).Raw(0x00).Str("TargetType"))
            .Sized(0x1D, p => p.U16(0).U16(0).Str(targetType))
            .Sized(0x1D, p => p.U16(1).U16(0).Str(templateTargetType))
            .Sized(0x20, p => p.U16(0).Str(key));
        return head.Deferable()
            .Sized(0x26, p => p.U16(0).U32(0).U16(0))
            .Raw(entry).FinishDictionary().ToArray();
    }

    private static byte[] RibbonFullDictionary(byte[] entry, string key)
    {
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1F, p => p.I16(0).I16(-620).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(1).I16(-556).Raw(0x00).Str("Property"))
            .Sized(0x1F, p => p.I16(2).I16(-556).Raw(0x00).Str("Value"))
            .Sized(0x1F, p => p.I16(7).I16(-108).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(8).I16(-601).Raw(0x00).Str("Orientation"))
            .Sized(0x1F, p => p.I16(46).I16(46).Raw(0x00).Str("Name"))
            .Sized(0x1F, p => p.I16(83).I16(-50).Raw(0x00).Str("Name"))
            .Sized(0x1F, p => p.I16(84).I16(62).Raw(0x00).Str("Orientation"))
            .Sized(0x1D, p => p.U16(46).U16(0).Str("HandyControl.Controls.SimplePanel"))
            .Sized(0x1D, p => p.U16(61).U16(0).Str("HandyControl.Controls.RibbonTab"))
            .Sized(0x1D, p => p.U16(62).U16(0).Str("HandyControl.Controls.ScrollViewerAttach"))
            .Sized(0x20, p => p.U16(0).Str(key));
        return head.Deferable()
            .Raw(0x27).U16(61).Raw(0x00).U32(0).Raw(0x00, 0x00)
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
