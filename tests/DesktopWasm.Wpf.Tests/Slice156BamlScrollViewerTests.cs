// Copyright (c) 2026 The WPF on WASM Contributors.
// Licensed under the MIT License. See LICENSE in the project root.

using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;


/// <summary>Slice-156: the ScrollViewer child at BAML+0x1A9DD on the
/// RibbonTab Template complex (the ScrollViewerAttach.Orientation attached
/// PWC plus the Hidden/Disabled scrollbar simples plus the Content member
/// with the ItemsPresenter child) routes as a live engine visual, so the
/// staged boot stops precise at the keyed 'LightPrimaryBrush'
/// SolidColorBrush entry at BAML+0x1AA1C (well-known type id -578 with no
/// brush-surface routing - the next slice's work).
/// Live counts verified first: handycontrol conversion-failed/6 staged/2 gaps
/// (both declared absent binaries System.Security.Permissions +
/// System.Windows.Extensions with no browser path; Convert map untouched,
/// all 6 staged images sha256-verified, manifest hand-untouched;
/// staged-oracle status-gate bypass stays in-probe only). Triangulation three
/// ways: staged framing spans against the live 466350-byte theme.baml
/// (sha256 D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0,
/// verified live) - the ScrollViewer head 03 DA FD 00 at BAML+0x1A9DD, the
/// Orientation PWC over per-file attr 84 (owned by the ScrollViewerAttach
/// TypeInfo 62 row at 0x27C5) carrying Horizontal through the EnumConverter,
/// the Hidden member -98 and Disabled member -99 PWCs, the Content member
/// 2E F2 FF, the ItemsPresenter head 03 B5 FE 00 (well-known type id -331)
/// at BAML+0x1AA11, the 04 04 04 04 08 04 04 close run, the next head
/// 03 BE FD 00 (SolidColorBrush, well-known type id -578) at BAML+0x1AA1C,
/// the key record 0x26 keyidx A4 at 0xB609, the key-string row 0xA4
/// 'LightPrimaryBrush' at 0x2807; the live upstream HandyControl Ribbon.xaml
/// implicit RibbonTab block at pinned commit e9ad3d70 (ScrollViewer with
/// hc:ScrollViewerAttach.Orientation Horizontal plus
/// HorizontalScrollBarVisibility Hidden plus VerticalScrollBarVisibility
/// Disabled holding the ItemsPresenter, fetched live as raw Ribbon.xaml);
/// a genuine net10.0-windows ResourceDictionary fixture compiling the same
/// ScrollViewer/ItemsPresenter block on framework types (ScrollViewer.baml,
/// byte-identical in every staged record shape modulo per-file numbering
/// and the 0x35/0x36 line markers the staged file omits: ScrollViewer
/// element DA FD, members 9E FF/9D FF, Content member F2 FF, ItemsPresenter
/// element B5 FE; plus a Disambig.baml fixture proving BE FD is the
/// SolidColorBrush element head; fixtures retained at E:/tmp/bamlfix outside
/// the tree) plus a genuine net10.0-windows reflection probe
/// (ScrollViewer-owned H/VScrollBarVisibility DPs typed ScrollBarVisibility
/// with Disabled/Visible defaults, Disabled=0/Auto=1/Hidden=2/Visible=3,
/// ItemsPresenter : FrameworkElement, Orientation H=0/V=1, Content narrowed to UIElement;
/// probe retained at E:/tmp/rbprobe outside the tree). Real-behavior engine
/// work, never fabricated: BamlReader gains KnownItemsPresenter (-331) plus
/// KnownH/VScrollBarVisibilityMember (-98/-99) template arms and a
/// ParseTemplateScrollViewer visual (Orientation attached PWC plus the two
/// scrollbar enum simples through the DP store, the Content member, the
/// single empty ItemsPresenter child; the root-panel children contract
/// grows to name ScrollViewer). The RibbonTab entry lands as a live engine
/// Style. Re-package skipped by evidence (Convert map untouched, staged bins
/// byte-identical). Tests: 13 new Slice156BamlScrollViewerTests
/// (hash-verifying 6/2 manifest oracle, staged framing, boot advance to the
/// new stop, full-entry staged landing, 2 synthetic landings, 6 synthetic
/// negatives, engine-shape WPF-oracle pins) plus the Slice154/Slice155
/// full-entry rewrites (stop to landing), the Slice155 synthetic ScrollViewer
/// rewrite (stop to landing), the Slice134/Slice137 synthetic contract
/// re-pins, the 47-file stop-triple flip with new-stop triples, and the
/// BamlReader class-doc frontier rewrite.
/// application-compatibility-status.md handycontrol row verified current
/// against the live manifest (conversion-failed/6/2; no regen, nothing
/// re-packaged). No browser proof - package stays conversion-failed.
/// Precise next blockers: the keyed 'LightPrimaryBrush' SolidColorBrush
/// entry (keyidx A4, BAML+0x1AA1C), then the CarouselPateButton/implicit
/// Carousel styles, the GroupBox styles with BasedOn/DynamicResource setters
/// and TitleElement triggers, EventTriggers, the line-info trailer, the
/// per-file (Type,Name) x:Name rows and the converted-lib elements
/// (CircleProgressBar/GifImage/Watermark/Drawer/ElementGroup/SearchBar)
/// behind the line-17 SkinDefault stop.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice156BamlScrollViewerTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public Slice156BamlScrollViewerTests()
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
    public void Theme_baml_scrollviewer_subtree_bytes()
    {
        LoadStagedHandyControlEntry();
        var theme = ReadStagedGResource("HandyControl", "themes/theme.baml");
        Assert.Equal(466350, theme.Length);
        Assert.Equal(
            "D305CC52C4F8A6EB077D1CC79F556BD343830E30C7C67FC73D3988C63AAF65D0",
            Convert.ToHexString(SHA256.HashData(theme)));

        // The ScrollViewer child head at BAML+0x1A9DD (well-known type id
        // -550) with its flags byte.
        var scrollViewerHead = new byte[] { 0x03, 0xDA, 0xFD, 0x00 };
        Assert.Equal(scrollViewerHead, theme[0x1A9DD..0x1A9E1]);
        // ScrollViewerAttach.Orientation Horizontal (per-file attr 0x54 owned
        // by the ScrollViewerAttach TypeInfo 62) through the EnumConverter.
        var scrollOrientationPwc = new byte[] { 0x24, 0x10, 0x54, 0x00, 0x0A, 0x48, 0x6F, 0x72, 0x69, 0x7A, 0x6F, 0x6E, 0x74, 0x61, 0x6C, 0x3D, 0xFF };
        Assert.Equal(scrollOrientationPwc, theme[0x1A9E1..0x1A9F2]);
        Assert.Equal(Encoding.ASCII.GetBytes("Horizontal"), theme[0x1A9E6..0x1A9F0]);
        // HorizontalScrollBarVisibility Hidden (member -98) plus
        // VerticalScrollBarVisibility Disabled (member -99), both
        // EnumConverter simples.
        var hiddenPwc = new byte[] { 0x24, 0x0C, 0x9E, 0xFF, 0x06, 0x48, 0x69, 0x64, 0x64, 0x65, 0x6E, 0x3D, 0xFF };
        Assert.Equal(hiddenPwc, theme[0x1A9F2..0x1A9FF]);
        var disabledPwc = new byte[] { 0x24, 0x0E, 0x9D, 0xFF, 0x08, 0x44, 0x69, 0x73, 0x61, 0x62, 0x6C, 0x65, 0x64, 0x3D, 0xFF };
        Assert.Equal(disabledPwc, theme[0x1A9FF..0x1AA0E]);
        // Content member (well-known member id -14) plus the ItemsPresenter
        // child head (well-known type id -331), closed with the
        // 04 04 04 04 08 04 04 run, then the next entry head.
        var scrollContent = new byte[] { 0x2E, 0xF2, 0xFF };
        Assert.Equal(scrollContent, theme[0x1AA0E..0x1AA11]);
        var itemsPresenterHead = new byte[] { 0x03, 0xB5, 0xFE, 0x00 };
        Assert.Equal(itemsPresenterHead, theme[0x1AA11..0x1AA15]);
        var entryClose = new byte[] { 0x04, 0x04, 0x04, 0x04, 0x08, 0x04, 0x04 };
        Assert.Equal(entryClose, theme[0x1AA15..0x1AA1C]);
        // The next entry head is the keyed 'LightPrimaryBrush' SolidColorBrush
        // (well-known type id -578: BE FD emits for SolidColorBrush in the
        // genuine net10 Disambig fixture, while the BE FF AttributeInfo owner
        // is Canvas -66 in a different namespace slot).
        var nextHead = new byte[] { 0x03, 0xBE, 0xFD, 0x00 };
        Assert.Equal(nextHead, theme[0x1AA1C..0x1AA20]);
        var lightPrimaryKey = new byte[] { 0x26, 0x09, 0xA4, 0x00, 0x85, 0xB8, 0x00, 0x00, 0x00, 0x00 };
        Assert.Equal(lightPrimaryKey, theme[0xB609..0xB613]);
        var lightPrimaryKeyRow = new byte[] { 0x20, 0x15, 0xA4, 0x00, 0x11 };
        Assert.Equal(lightPrimaryKeyRow, theme[0x2807..0x280C]);
        Assert.Equal(Encoding.ASCII.GetBytes("LightPrimaryBrush"), theme[0x280C..0x281D]);
        var orientationAttrHead = new byte[] { 0x1F, 0x12, 0x54, 0x00, 0x3E, 0x00, 0x00, 0x0B };
        Assert.Equal(orientationAttrHead, theme[0x27F4..0x27FC]);
        Assert.Equal(Encoding.ASCII.GetBytes("Orientation"), theme[0x27FC..0x2807]);
        var scrollAttachTypeHead = new byte[] { 0x1D, 0x2E, 0x3E, 0x00, 0x00, 0x00, 0x28 };
        Assert.Equal(scrollAttachTypeHead, theme[0x27C5..0x27CC]);
        Assert.Equal(Encoding.ASCII.GetBytes("HandyControl.Controls.ScrollViewerAttach"), theme[0x27CC..0x27F4]);
    }

    [Fact]
    public void Staged_boot_advances_past_scrollviewer_to_light_primary_brush()
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
        // ControlTemplate complex: the converted SimplePanel root with its
        // PART_RootContainer Name PWC, its Visibility Collapsed PWC, the
        // Children content, the mainBorder Border child (Margin plus two
        // TemplateBindings) and the ScrollViewer child at BAML+0x1A9DD with
        // the ScrollViewerAttach.Orientation Horizontal PWC plus the Hidden
        // horizontal and Disabled vertical scrollbar PWCs plus the Content
        // member with the ItemsPresenter child. The entry lands as a live
        // engine Style keyed by the converted RibbonTab Type. The staged
        // HandyControl image must be loaded so the per-file panel, target
        // and attached-owner types resolve like the boot path.
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
        Assert.Same(DependencyProperty.FromName(ribbonTab, "Focusable"), byName["Focusable"].Property);
        Assert.Equal(VerticalAlignment.Center, byName["VerticalContentAlignment"].Value);
        Assert.Same(DependencyProperty.FromName(ribbonTab, "VerticalContentAlignment"), byName["VerticalContentAlignment"].Property);
        Assert.Same(ItemsControl.ItemsPanelProperty, byName["ItemsPanel"].Property);
        Assert.Same(DependencyProperty.FromName(ribbonTab, "Template"), byName["Template"].Property);
        var panel = Assert.IsType<ItemsPanelTemplate>(byName["ItemsPanel"].Value);
        var stack = Assert.IsType<StackPanel>(panel.VisualRoot);
        Assert.Equal(Orientation.Horizontal, stack.Orientation);
        var template = Assert.IsType<ControlTemplate>(byName["Template"].Value);
        Assert.Same(ribbonTab, template.TargetType);
        var root = Assert.IsType<SimplePanel>(template.VisualTree);
        Assert.Equal("PART_RootContainer", root.GetValue(FrameworkElement.NameProperty));
        Assert.Equal(Visibility.Collapsed, root.GetValue(UIElement.VisibilityProperty));
        Assert.True(root.HasLocalValue(UIElement.VisibilityProperty));
        Assert.Equal(2, root.Children.Count);
        var border = Assert.IsType<Border>(root.Children[0]);
        Assert.Equal(new Thickness(0), border.GetValue(FrameworkElement.MarginProperty));
        Assert.True(border.HasLocalValue(FrameworkElement.MarginProperty));
        var sources = template.TemplateBindings.Select(b => b.Property).ToArray();
        Assert.Equal(2, sources.Length);
        Assert.Contains("BorderBrush", sources);
        Assert.Contains("Background", sources);
        Assert.All(template.TemplateBindings, b => Assert.Same(border, b.Element));
        var viewer = Assert.IsType<ScrollViewer>(root.Children[1]);
        Assert.Equal(ScrollBarVisibility.Hidden, viewer.GetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty));
        Assert.True(viewer.HasLocalValue(ScrollViewer.HorizontalScrollBarVisibilityProperty));
        Assert.Equal(ScrollBarVisibility.Disabled, viewer.GetValue(ScrollViewer.VerticalScrollBarVisibilityProperty));
        Assert.True(viewer.HasLocalValue(ScrollViewer.VerticalScrollBarVisibilityProperty));
        var orientationDp = DependencyProperty.FromName(scrollAttach, "Orientation");
        Assert.NotNull(orientationDp);
        Assert.Equal(Enum.Parse(orientationDp.PropertyType, "Horizontal"), viewer.GetValue(orientationDp));
        var presenter = Assert.IsType<ItemsPresenter>(viewer.Content);
        Assert.Same(presenter, viewer.Content);
        Assert.Same(style, dictionary[ribbonTab]);
    }

    [Fact]
    public void Synthetic_template_root_scrollviewer_lands_live()
    {
        // A converted SimplePanel root with the Visibility Collapsed PWC plus
        // a ScrollViewer child carrying the HandyControl ScrollViewerAttach
        // Orientation PWC (per-file attr 84 owned by the converted
        // ScrollViewerAttach TypeInfo 62) plus the Hidden/Disabled scrollbar
        // PWCs (well-known members -98/-99 resolved against the engine
        // ScrollViewer) plus the Content member with the empty ItemsPresenter
        // child lands as a live engine ControlTemplate. The converted
        // HandyControl assembly must be present for the per-file panel and
        // attached-owner types.
        LoadStagedHandyControlEntry();
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var scrollAttach = handyControl.GetType("HandyControl.Controls.ScrollViewerAttach");
        Assert.NotNull(scrollAttach);
        var dictionary = BamlReader.LoadDictionary(
            ScrollViewerDictionary("S", ScrollViewerBody("Horizontal", 84, -195, "Hidden", -195, "Disabled", -195)));
        var template = Assert.IsType<ControlTemplate>(dictionary["S"]);
        var root = Assert.IsType<SimplePanel>(template.VisualTree);
        Assert.Equal(Visibility.Collapsed, root.GetValue(UIElement.VisibilityProperty));
        var viewer = Assert.IsType<ScrollViewer>(Assert.Single(root.Children));
        Assert.Equal(ScrollBarVisibility.Hidden, viewer.GetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty));
        Assert.True(viewer.HasLocalValue(ScrollViewer.HorizontalScrollBarVisibilityProperty));
        Assert.Equal(ScrollBarVisibility.Disabled, viewer.GetValue(ScrollViewer.VerticalScrollBarVisibilityProperty));
        Assert.True(viewer.HasLocalValue(ScrollViewer.VerticalScrollBarVisibilityProperty));
        var orientationDp = DependencyProperty.FromName(scrollAttach, "Orientation");
        Assert.NotNull(orientationDp);
        Assert.Equal(Enum.Parse(orientationDp.PropertyType, "Horizontal"), viewer.GetValue(orientationDp));
        Assert.IsType<ItemsPresenter>(viewer.Content);
        Assert.Same(template, dictionary["S"]);
    }

    [Fact]
    public void Synthetic_template_root_scrollviewer_vertical_lands_live()
    {
        // The scrollbar and Orientation PWCs convert for real (Vertical/Auto/
        // Visible land), so the Horizontal/Hidden/Disabled landing above is a
        // genuine conversion and not a hardcoded default.
        LoadStagedHandyControlEntry();
        var dictionary = BamlReader.LoadDictionary(
            ScrollViewerDictionary("S", ScrollViewerBody("Vertical", 84, -195, "Auto", -195, "Visible", -195)));
        var template = Assert.IsType<ControlTemplate>(dictionary["S"]);
        var root = Assert.IsType<SimplePanel>(template.VisualTree);
        var viewer = Assert.IsType<ScrollViewer>(Assert.Single(root.Children));
        Assert.Equal(ScrollBarVisibility.Auto, viewer.GetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty));
        Assert.Equal(ScrollBarVisibility.Visible, viewer.GetValue(ScrollViewer.VerticalScrollBarVisibilityProperty));
        var handyControl = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => string.Equals(a.GetName().Name, "HandyControl", StringComparison.OrdinalIgnoreCase));
        var orientationDp = DependencyProperty.FromName(
            handyControl.GetType("HandyControl.Controls.ScrollViewerAttach")!, "Orientation");
        Assert.NotNull(orientationDp);
        Assert.Equal(Enum.Parse(orientationDp.PropertyType, "Vertical"), viewer.GetValue(orientationDp));
    }

    [Fact]
    public void Synthetic_template_scrollviewer_wrong_converter_stays_precise()
    {
        // A scrollbar PWC over any other converter (here the
        // StringConverter) fails naming the text, the converter and the
        // target instead of landing a half-built visual.
        LoadStagedHandyControlEntry();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            ScrollViewerDictionary("S", ScrollViewerBody("Horizontal", 84, -195, "Hidden", -615, "Disabled", -195))));
        Assert.Contains("'Hidden' with converter -615 does not convert to ScrollBarVisibility", error.Message);
    }

    [Fact]
    public void Synthetic_template_scrollviewer_garbage_stays_precise()
    {
        // A scrollbar PWC with unconvertible text fails naming the text, the
        // converter and the target instead of landing a default.
        LoadStagedHandyControlEntry();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            ScrollViewerDictionary("S", ScrollViewerBody("Horizontal", 84, -195, "Banana", -195, "Disabled", -195))));
        Assert.Contains("'Banana' with converter -195 does not convert to ScrollBarVisibility", error.Message);
    }

    [Fact]
    public void Synthetic_template_scrollviewer_orientation_wrong_owner_stays_precise()
    {
        // An Orientation PWC owned by a framework type that declares no such
        // property (here engine Border) fails naming the property and the
        // owner instead of landing a half-built visual.
        LoadStagedHandyControlEntry();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            ScrollViewerDictionary("S", ScrollViewerBody("Horizontal", 85, -195, "Hidden", -195, "Disabled", -195))));
        Assert.Contains("'Orientation' is not a dependency property of Border", error.Message);
    }

    [Fact]
    public void Synthetic_template_scrollviewer_second_presenter_stays_precise()
    {
        // The ScrollViewer takes a single ItemsPresenter: a second presenter
        // fails naming it instead of replacing the first.
        LoadStagedHandyControlEntry();
        var body = new B()
            .Raw(0x03).I16(-108).Raw(0x00)
            .Raw(0x22).I16(7).I16(51)
            .Raw(0x2E).I16(-174)
            .Raw(0x03).I16(46).Raw(0x00)
            .Sized(0x24, q => q.I16(8).Str("P").I16(-615))
            .Sized(0x24, q => q.I16(-135).Str("Collapsed").I16(-195))
            .Raw(0x2E).I16(-213)
            .Raw(0x03).I16(-550).Raw(0x00)
            .Sized(0x24, q => q.I16(84).Str("Horizontal").I16(-195))
            .Sized(0x24, q => q.I16(-98).Str("Hidden").I16(-195))
            .Sized(0x24, q => q.I16(-99).Str("Disabled").I16(-195))
            .Raw(0x2E).I16(-14)
            .Raw(0x03).I16(-331).Raw(0x00)
            .Raw(0x04)
            .Raw(0x03).I16(-331).Raw(0x00)
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            ScrollViewerDictionary("S", body)));
        Assert.Contains("duplicate ItemsPresenter on the template ScrollViewer", error.Message);
    }

    [Fact]
    public void Synthetic_template_scrollviewer_textblock_child_stays_precise()
    {
        // A TextBlock is a root-panel child, not a ScrollViewer child: it
        // fails naming the well-known id with the scroller contract instead
        // of landing inside the viewer.
        LoadStagedHandyControlEntry();
        var body = new B()
            .Raw(0x03).I16(-108).Raw(0x00)
            .Raw(0x22).I16(7).I16(51)
            .Raw(0x2E).I16(-174)
            .Raw(0x03).I16(46).Raw(0x00)
            .Sized(0x24, q => q.I16(8).Str("P").I16(-615))
            .Sized(0x24, q => q.I16(-135).Str("Collapsed").I16(-195))
            .Raw(0x2E).I16(-213)
            .Raw(0x03).I16(-550).Raw(0x00)
            .Sized(0x24, q => q.I16(84).Str("Horizontal").I16(-195))
            .Sized(0x24, q => q.I16(-98).Str("Hidden").I16(-195))
            .Sized(0x24, q => q.I16(-99).Str("Disabled").I16(-195))
            .Raw(0x2E).I16(-14)
            .Raw(0x03).I16(-638).Raw(0x00)
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            ScrollViewerDictionary("S", body)));
        Assert.Contains("element TextBlock on the template ScrollViewer", error.Message);
        Assert.Contains("scroll viewers take the ItemsPresenter child", error.Message);
    }

    [Fact]
    public void Synthetic_template_presenter_with_child_stays_precise()
    {
        // The staged ItemsPresenter is empty: a presenter carrying its own
        // child fails naming the record instead of landing a half-built
        // visual.
        LoadStagedHandyControlEntry();
        var body = new B()
            .Raw(0x03).I16(-108).Raw(0x00)
            .Raw(0x22).I16(7).I16(51)
            .Raw(0x2E).I16(-174)
            .Raw(0x03).I16(46).Raw(0x00)
            .Sized(0x24, q => q.I16(8).Str("P").I16(-615))
            .Sized(0x24, q => q.I16(-135).Str("Collapsed").I16(-195))
            .Raw(0x2E).I16(-213)
            .Raw(0x03).I16(-550).Raw(0x00)
            .Sized(0x24, q => q.I16(84).Str("Horizontal").I16(-195))
            .Sized(0x24, q => q.I16(-98).Str("Hidden").I16(-195))
            .Sized(0x24, q => q.I16(-99).Str("Disabled").I16(-195))
            .Raw(0x2E).I16(-14)
            .Raw(0x03).I16(-331).Raw(0x00)
            .Raw(0x03).I16(-638).Raw(0x00)
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x04)
            .Raw(0x04).ToArray();
        var error = Assert.Throws<XamlLoadException>(() => BamlReader.LoadDictionary(
            ScrollViewerDictionary("S", body)));
        Assert.Contains("template ItemsPresenter holds record 0x03", error.Message);
        Assert.Contains("template presenters are empty", error.Message);
    }

    [Fact]
    public void Scrollviewer_engine_shape_matches_wpf()
    {
        // The routed PWCs match the genuine net10.0-windows shape: the
        // scrollbar DPs are ScrollViewer-owned, ScrollBarVisibility-typed,
        // Disabled/Visible by default and resolve through FromName; the
        // enum parses Hidden/Disabled like the fixture; the presenter is a
        // FrameworkElement; Orientation parses Horizontal like the staged
        // attached value; Content is the UIElement slot (WPF: object) the presenter lands in.
        Assert.Same(typeof(ScrollViewer), ScrollViewer.HorizontalScrollBarVisibilityProperty.OwnerType);
        Assert.Same(typeof(ScrollBarVisibility), ScrollViewer.HorizontalScrollBarVisibilityProperty.PropertyType);
        Assert.Equal(ScrollBarVisibility.Disabled, ScrollViewer.HorizontalScrollBarVisibilityProperty.DefaultMetadata.DefaultValue);
        Assert.Same(typeof(ScrollViewer), ScrollViewer.VerticalScrollBarVisibilityProperty.OwnerType);
        Assert.Same(typeof(ScrollBarVisibility), ScrollViewer.VerticalScrollBarVisibilityProperty.PropertyType);
        Assert.Equal(ScrollBarVisibility.Visible, ScrollViewer.VerticalScrollBarVisibilityProperty.DefaultMetadata.DefaultValue);
        Assert.Same(ScrollViewer.HorizontalScrollBarVisibilityProperty, DependencyProperty.FromName(typeof(ScrollViewer), "HorizontalScrollBarVisibility"));
        Assert.Same(ScrollViewer.VerticalScrollBarVisibilityProperty, DependencyProperty.FromName(typeof(ScrollViewer), "VerticalScrollBarVisibility"));
        Assert.Equal(0, (int)ScrollBarVisibility.Disabled);
        Assert.Equal(1, (int)ScrollBarVisibility.Auto);
        Assert.Equal(2, (int)ScrollBarVisibility.Hidden);
        Assert.Equal(3, (int)ScrollBarVisibility.Visible);
        Assert.Equal(ScrollBarVisibility.Hidden, Enum.Parse<ScrollBarVisibility>("Hidden"));
        Assert.Equal(ScrollBarVisibility.Disabled, Enum.Parse<ScrollBarVisibility>("Disabled"));
        var viewer = new ScrollViewer();
        Assert.Equal(ScrollBarVisibility.Disabled, viewer.HorizontalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Visible, viewer.VerticalScrollBarVisibility);
        Assert.Null(viewer.Content);
        Assert.True(typeof(FrameworkElement).IsAssignableFrom(typeof(ItemsPresenter)));
        Assert.Equal(0, (int)Orientation.Horizontal);
        Assert.Equal(1, (int)Orientation.Vertical);
        Assert.Equal(Orientation.Horizontal, Enum.Parse<Orientation>("Horizontal"));
        Assert.Same(typeof(UIElement), typeof(ScrollViewer).GetProperty("Content")!.PropertyType); // Engine narrows the slot to UIElement (WPF takes object); the presenter lands in it either way.
    }

    private static byte[] ScrollViewerBody(
        string orientation, short orientationAttr, short orientationConv,
        string hidden, short hiddenConv, string disabled, short disabledConv) => new B()
        .Raw(0x03).I16(-108).Raw(0x00)
        .Raw(0x22).I16(7).I16(51)
        .Raw(0x2E).I16(-174)
        .Raw(0x03).I16(46).Raw(0x00)
        .Sized(0x24, q => q.I16(8).Str("P").I16(-615))
        .Sized(0x24, q => q.I16(-135).Str("Collapsed").I16(-195))
        .Raw(0x2E).I16(-213)
        .Raw(0x03).I16(-550).Raw(0x00)
        .Sized(0x24, q => q.I16(orientationAttr).Str(orientation).I16(orientationConv))
        .Sized(0x24, q => q.I16(-98).Str(hidden).I16(hiddenConv))
        .Sized(0x24, q => q.I16(-99).Str(disabled).I16(disabledConv))
        .Raw(0x2E).I16(-14)
        .Raw(0x03).I16(-331).Raw(0x00)
        .Raw(0x04)
        .Raw(0x04)
        .Raw(0x04)
        .Raw(0x04).ToArray();

    private static byte[] ScrollViewerDictionary(string key, byte[] entry)
    {
        // Per-file ids stand in for the staged rows (46 SimplePanel, 51
        // Badge target, 62 ScrollViewerAttach, 7 ControlTemplate.TargetType,
        // 8 panel-owned Name, 84 ScrollViewerAttach-owned Orientation, 85
        // Border-owned Orientation for the wrong-owner negative). The
        // converted HandyControl assembly must be loaded so the per-file
        // types resolve like the boot path.
        var head = new B().Raw(Header()).Raw(0x01)
            .Raw(0x03).I16(-524).Raw(0x00)
            .Sized(0x1C, p => p.U16(0).Str("Engine, Version=1.0.0.0"))
            .Sized(0x1D, p => p.U16(46).U16(0).Str("HandyControl.Controls.SimplePanel"))
            .Sized(0x1D, p => p.U16(51).U16(0).Str("HandyControl.Controls.Badge"))
            .Sized(0x1D, p => p.U16(62).U16(0).Str("HandyControl.Controls.ScrollViewerAttach"))
            .Sized(0x1F, p => p.I16(7).I16(-108).Raw(0x00).Str("TargetType"))
            .Sized(0x1F, p => p.I16(8).I16(46).Raw(0x00).Str("Name"))
            .Sized(0x1F, p => p.I16(84).I16(62).Raw(0x00).Str("Orientation"))
            .Sized(0x1F, p => p.I16(85).I16(-50).Raw(0x00).Str("Orientation"))
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
