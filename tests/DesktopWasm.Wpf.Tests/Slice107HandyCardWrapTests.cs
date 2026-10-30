// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-107 HandyControl CardDemo wrap + footer/header text ink.
///
/// Live counts verified first: handycontrol conversion-failed, 6 staged,
/// 2 gaps (absent System.Security.Permissions + System.Windows.Extensions,
/// both with no browser path; Convert map touched only for the AncestorType
/// unique-scan fallback, staged bins byte-identical by hash, manifests
/// untouched).
///
/// (a) WrapPanel arrangement: the legacy ListBox retained its ItemsPanel but
/// stacked rows vertically. Rows now flow through the shared WrapPanel math
/// when the retained template inflates to a WrapPanel (the staged CardDemo
/// host); other hosts keep the legacy stack. Row slots honor the templated
/// visual's margin (the staged Card Margin="8" pitch).
///
/// (b) Converted default-style/template application: converted hc:Card
/// controls carry Header/Footer/HeaderTemplate/FooterTemplate bindings with
/// no Style or Template, and the lib default style ships BAML-only behind
/// the line-17 SkinDefault stop. The engine now applies the upstream
/// CardBaseStyle structure as the converted default (header/content/footer
/// stack, Object2Visibility slots, ContentSource presenters) so footer and
/// header text ink paints. Supporting fixes, all real-behavior: engine
/// Object2VisibilityConverter with the upstream null-to-Collapsed semantics;
/// AncestorType resolution falls back to the unique converted-closure scan
/// (entry-only lookup missed lib types like hc:Card, and binding pushes
/// swallow the resulting refresh failure); converted [ContentProperty]
/// attaches refresh an applied template; ContentControl paints an applied
/// TemplateRoot (the Button precedent) instead of swallowing it.
///
/// Advance pinned here, all reproduced against live staged bins via the
/// engine-native staged-bin boot oracle (status gate bypassed in-probe only)
/// then pinned by the new staged-oracle set: CardDemo rows wrap 3-3-3-1 at
/// 800x600 and paint all 10 headers plus all 10 footers. Remaining blockers:
/// the line-17 SkinDefault BAML-only stop (entry boot; slice-108 re-pinned
/// this oracle as the theme-brush family now resolves, see
/// Slice108HandyBrushesTests), non-WrapPanel hosts (stay stacked), and the
/// 2 absent binaries (no browser path).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice107HandyCardWrapTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private readonly Application? _previous = Application.Current;

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    [Fact]
    public void Object2visibility_maps_null_to_collapsed_and_values_to_visible()
    {
        var converter = new Object2VisibilityConverter();
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        Assert.Equal(Visibility.Collapsed, converter.Convert(null, typeof(Visibility), null, culture));
        Assert.Equal(Visibility.Visible, converter.Convert("Stive Morgan", typeof(Visibility), null, culture));
        Assert.Equal(Visibility.Visible, converter.Convert(0, typeof(Visibility), null, culture));
        Assert.Equal(Visibility.Visible, converter.Convert(false, typeof(Visibility), null, culture));
        Assert.Throws<NotSupportedException>(() =>
            converter.ConvertBack(Visibility.Visible, typeof(object), null, culture));
    }

    [Fact]
    public void Card_shape_gate_matches_only_converted_card_shape()
    {
        Assert.False(Slice107HandyCard.IsCardShape(new ContentControl()));
        Assert.False(Slice107HandyCard.IsCardShape(new Button()));
        Assert.True(Slice107HandyCard.IsCardShape(new Slice107TestCard()));
        Assert.False(Slice107HandyCard.IsCardShape(new Slice107PartialCard()));
    }

    [Fact]
    public void Card_default_template_paints_footer_and_header_text()
    {
        Application.Current = new Application();
        XamlLoader.CustomElementSource = typeof(Slice107TestCard).Assembly;
        var card = Assert.IsType<Slice107TestCard>(XamlLoader.Load(
            "<t:Slice107TestCard xmlns=\"" + PresentationNs + "\" " +
            "xmlns:t=\"clr-namespace:DesktopWasm.Wpf.Tests\" " +
            "Header=\"H1\" Footer=\"F1\"> " +
            "<t:Slice107TestCard.HeaderTemplate><DataTemplate><TextBlock Text=\"{Binding}\" />" +
            "</DataTemplate></t:Slice107TestCard.HeaderTemplate> " +
            "<t:Slice107TestCard.FooterTemplate><DataTemplate><TextBlock Text=\"{Binding}\" />" +
            "</DataTemplate></t:Slice107TestCard.FooterTemplate> " +
            "</t:Slice107TestCard>").Root);
        Assert.NotNull(card.Template);
        var frame = WasmMilCore.RenderHeadless(card, new WpfSize(400, 300));
        var texts = frame.OfType<DrawCommand.DrawText>().Select(d => d.Text).ToList();
        Assert.Contains("H1", texts);
        Assert.Contains("F1", texts);
    }

    [Fact]
    public void Card_default_template_collapses_null_header_and_keeps_footer()
    {
        Application.Current = new Application();
        XamlLoader.CustomElementSource = typeof(Slice107TestCard).Assembly;
        var card = Assert.IsType<Slice107TestCard>(XamlLoader.Load(
            "<t:Slice107TestCard xmlns=\"" + PresentationNs + "\" " +
            "xmlns:t=\"clr-namespace:DesktopWasm.Wpf.Tests\" " +
            "Footer=\"F1\"> " +
            "<t:Slice107TestCard.FooterTemplate><DataTemplate><TextBlock Text=\"{Binding}\" />" +
            "</DataTemplate></t:Slice107TestCard.FooterTemplate> " +
            "</t:Slice107TestCard>").Root);
        Assert.NotNull(card.Template);
        var frame = WasmMilCore.RenderHeadless(card, new WpfSize(400, 300));
        var texts = frame.OfType<DrawCommand.DrawText>().Select(d => d.Text).ToList();
        Assert.Equal(["F1"], texts);
    }

    [Fact]
    public void Card_default_template_resolves_ancestor_type_through_closure_scan()
    {
        Application.Current = new Application();
        XamlLoader.CustomElementSource = typeof(Slice107TestCard).Assembly;
        var card = Assert.IsType<Slice107TestCard>(XamlLoader.Load(
            "<t:Slice107TestCard xmlns=\"" + PresentationNs + "\" " +
            "xmlns:t=\"clr-namespace:DesktopWasm.Wpf.Tests\" " +
            "Footer=\"F1\"> " +
            "<t:Slice107TestCard.FooterTemplate><DataTemplate>" +
            "<TextBlock Text=\"{Binding DataContext.Tag, RelativeSource={RelativeSource AncestorType=t:Slice107TestCard}}\" />" +
            "</DataTemplate></t:Slice107TestCard.FooterTemplate> " +
            "</t:Slice107TestCard>").Root);
        card.DataContext = new Slice107TestModel { Tag = "TG" };
        var frame = WasmMilCore.RenderHeadless(card, new WpfSize(400, 300));
        var texts = frame.OfType<DrawCommand.DrawText>().Select(d => d.Text).ToList();
        Assert.Contains("TG", texts);
    }

    [Fact]
    public void Card_default_template_defers_to_explicit_style_and_template()
    {
        Application.Current = new Application();
        var fresh = new Slice107TestCard();
        Assert.True(Slice107HandyCard.TryApplyCardDefault(fresh));
        Assert.NotNull(fresh.Template);
        Assert.False(Slice107HandyCard.TryApplyCardDefault(fresh));
        var styled = new Slice107TestCard { Style = new Style() };
        Assert.False(Slice107HandyCard.TryApplyCardDefault(styled));
        Assert.Null(styled.Template);
    }

    [Fact]
    public void Legacy_listbox_wraps_rows_through_wrap_panel()
    {
        Application.Current = new Application();
        var list = Assert.IsType<ListBox>(XamlLoader.Load(
            "<ListBox xmlns=\"" + PresentationNs + "\" Width=\"300\">" +
            "<ListBox.ItemTemplate><DataTemplate><Border Width=\"120\" Height=\"40\" />" +
            "</DataTemplate></ListBox.ItemTemplate>" +
            "<ListBox.ItemsPanel><ItemsPanelTemplate><WrapPanel />" +
            "</ItemsPanelTemplate></ListBox.ItemsPanel></ListBox>").Root);
        list.ItemsSource = new[] { "a", "b", "c", "d" };
        Assert.Equal(4, list.Items.Count);
        WasmMilCore.RenderHeadless(list, new WpfSize(300, 600));
        var rects = list.Items.Select(r => r.RenderRect).ToList();
        var bands = rects.GroupBy(r => r.Y).OrderBy(g => g.Key).ToList();
        Assert.Equal(2, bands.Count);
        Assert.Equal(2, bands[0].Count());
        Assert.Equal(2, bands[1].Count());
        Assert.True(bands[0].Select(r => r.X).Distinct().Count() > 1);
        Assert.All(rects, r => Assert.True(r.Width < 300));
        Assert.All(rects, r => Assert.True(r.Height > 0));
    }

    [Fact]
    public void Legacy_listbox_vertical_wrap_flows_in_columns()
    {
        Application.Current = new Application();
        var list = Assert.IsType<ListBox>(XamlLoader.Load(
            "<ListBox xmlns=\"" + PresentationNs + "\" Height=\"100\">" +
            "<ListBox.ItemTemplate><DataTemplate><Border Width=\"120\" Height=\"40\" />" +
            "</DataTemplate></ListBox.ItemTemplate>" +
            "<ListBox.ItemsPanel><ItemsPanelTemplate><WrapPanel Orientation=\"Vertical\" />" +
            "</ItemsPanelTemplate></ListBox.ItemsPanel></ListBox>").Root);
        Assert.Equal(Orientation.Vertical, Assert.IsType<WrapPanel>(list.ItemsPanel!.LoadContent()).Orientation);
        list.ItemsSource = new[] { "a", "b", "c", "d" };
        WasmMilCore.RenderHeadless(list, new WpfSize(600, 100));
        var rects = list.Items.Select(r => r.RenderRect).ToList();
        Assert.True(rects.Select(r => r.X).Distinct().Count() > 1);
        Assert.True(rects.Select(r => r.Y).Distinct().Count() > 1);
    }

    [Fact]
    public void Legacy_listbox_keeps_stack_without_wrap_panel()
    {
        Application.Current = new Application();
        var list = new ListBox();
        list.ItemTemplate = new DataTemplate(
            "<Border xmlns=\"" + PresentationNs + "\" Width=\"120\" Height=\"40\" />");
        list.ItemsSource = new[] { "a", "b", "c" };
        Assert.Null(list.ItemsPanel);
        WasmMilCore.RenderHeadless(list, new WpfSize(300, 600));
        var rects = list.Items.Select(r => r.RenderRect).ToList();
        Assert.Single(rects.Select(r => r.X).Distinct());
        var ordered = rects.OrderBy(r => r.Y).ToList();
        Assert.True(ordered[1].Y > ordered[0].Y);
        Assert.True(ordered[2].Y > ordered[1].Y);
    }

    [Fact]
    public void Legacy_listbox_stack_panel_host_keeps_stack()
    {
        Application.Current = new Application();
        var list = Assert.IsType<ListBox>(XamlLoader.Load(
            "<ListBox xmlns=\"" + PresentationNs + "\" Width=\"300\">" +
            "<ListBox.ItemTemplate><DataTemplate><Border Width=\"120\" Height=\"40\" />" +
            "</DataTemplate></ListBox.ItemTemplate>" +
            "<ListBox.ItemsPanel><ItemsPanelTemplate><StackPanel />" +
            "</ItemsPanelTemplate></ListBox.ItemsPanel></ListBox>").Root);
        list.ItemsSource = new[] { "a", "b" };
        WasmMilCore.RenderHeadless(list, new WpfSize(300, 600));
        var rects = list.Items.Select(r => r.RenderRect).ToList();
        Assert.True(rects.All(r => r.X == rects[0].X));
        Assert.True(rects[1].Y > rects[0].Y);
    }

    [Fact]
    public void Legacy_listbox_item_honors_templated_content_margin()
    {
        Application.Current = new Application();
        var list = new ListBox();
        list.ItemTemplate = new DataTemplate(
            "<Border xmlns=\"" + PresentationNs + "\" Width=\"100\" Height=\"30\" Margin=\"10,0,4,0\" />");
        list.ItemsSource = new[] { "a" };
        var row = Assert.Single(list.Items);
        row.Measure(new WpfSize(500, 600));
        Assert.Equal(114, row.DesiredSize.Width);
        row.Arrange(new WpfRect(0, 0, 114, row.DesiredSize.Height));
        var content = Assert.IsType<Border>(row.TemplatedContent);
        Assert.Equal(10, content.RenderRect.X);
    }

    [Fact]
    public void Staged_manifest_keeps_six_staged_two_absent()
    {
        var entry = LoadStagedHandyControlEntry();
        var apps = StagedHandyAppsDirectory();
        using var document = JsonDocument.Parse(
            File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
        var root = document.RootElement;
        Assert.Equal("conversion-failed", root.GetProperty("status").GetString());
        var assemblies = root.GetProperty("assemblies").EnumerateArray().ToList();
        Assert.Equal(6, assemblies.Count);
        foreach (var assembly in assemblies)
        {
            var file = assembly.GetProperty("file").GetString()!;
            var expected = assembly.GetProperty("sha256").GetString()!;
            var actual = Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(System.IO.Path.Combine(apps, file))));
            Assert.True(string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                $"hash mismatch in staged '{file}': {actual} != {expected}");
        }
        var gaps = root.GetProperty("gaps").EnumerateArray().ToList();
        Assert.Equal(2, gaps.Count);
        var details = gaps.Select(g => g.GetProperty("detail").GetString()!).ToList();
        Assert.Contains(details, d => d.Contains("System.Security.Permissions"));
        Assert.Contains(details, d => d.Contains("System.Windows.Extensions"));
        foreach (var name in new[]
        {
            "BorderRegion", "BorderTipPrimary", "BorderTipDanger", "BorderTipWarning",
            "BorderTipInfo", "BorderTipSuccess", "ToggleButtonSwitch", "GroupBoxOriginal",
            "ButtonCustom", "ToggleButtonIcon", "StackPanelVerticalListBox",
            "WrapPanelHorizontalListBox", "FluidMoveBehaviorWrapPanelItemsPanelTemplate",
            "KeyboardGeometry", "ImageGeometry", "AudioGeometry", "StringRepeatConverter",
            "Boolean2VisibilityReConverter", "Object2VisibilityConverter", "IsInertiaEnabled",
            "ImageFailed", "RoutedEventTrigger", "ChatBubble", "Card", "ItemTemplate",
        })
            Assert.DoesNotContain(details, d => d.Contains(name));
        _ = entry;
    }

    [Fact]
    public void Staged_card_demo_wraps_rows_in_bands()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.CardDemo");
        var list = Assert.Single(Descendants(view).OfType<ListBox>());
        Assert.Equal(10, list.Items.Count);
        WasmMilCore.RenderHeadless(view, new WpfSize(800, 600));
        var rects = list.Items.Select(r => r.RenderRect).ToList();
        var bands = rects.GroupBy(r => r.Y).OrderBy(g => g.Key).ToList();
        Assert.Equal(4, bands.Count);
        Assert.Equal(new[] { 3, 3, 3, 1 }, bands.Select(b => b.Count()).ToArray());
        Assert.All(bands.Take(3), b => Assert.True(b.Select(r => r.X).Distinct().Count() == 3));
        var widths = rects.Select(r => r.Width).Distinct().ToList();
        Assert.Single(widths);
        Assert.True(widths[0] < 798);
        Assert.All(rects, r => Assert.True(r.Height > 0));
    }

    [Fact]
    public void Staged_card_demo_paints_footer_and_header_text()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.CardDemo");
        var frame = WasmMilCore.RenderHeadless(view, new WpfSize(800, 600));
        var texts = frame.OfType<DrawCommand.DrawText>().Select(d => d.Text).ToList();
        Assert.Equal(20, texts.Count);
        var cards = Descendants(view).Where(n => n.GetType().Name == "Card").ToList();
        foreach (var card in cards)
        {
            var model = ((FrameworkElement)card).DataContext!;
            var header = (string?)model.GetType().GetProperty("Header")!.GetValue(model);
            var footer = (string?)model.GetType().GetProperty("Footer")!.GetValue(model);
            Assert.False(string.IsNullOrEmpty(header));
            Assert.False(string.IsNullOrEmpty(footer));
            Assert.Contains(header!, texts);
            Assert.Contains(footer!, texts);
        }
        Assert.DoesNotContain(texts, t => t.Contains("CardModel"));
    }

    [Fact]
    public void Staged_theme_brushes_now_resolve_behind_unmoved_baml_stop()
    {
        // Slice-108 re-pin: the line-17 SkinDefault BAML-only stop still
        // blocks App.xaml hydration (see HandyControlEntryBootTests), but
        // the theme-brush family now resolves at application scope with the
        // real upstream default-skin values (see Slice108HandyBrushesTests
        // for the full value pins).
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        _ = InstantiateStagedView(entry, "HandyControlDemo.UserControl.CardDemo");
        Assert.True(Application.Current.TryFindResource("RegionBrush", out var region));
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), Assert.IsType<Color>(region));
        Assert.True(Application.Current.TryFindResource("EffectShadow2", out _));
        Assert.True(Application.Current.TryFindResource("Object2VisibilityConverter", out var converter));
        Assert.IsType<Object2VisibilityConverter>(converter);
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
                XamlLoader.CustomElementSource = hit;
                return hit;
            }
            if (_stagedEntry is not null)
            {
                XamlLoader.CustomElementSource = _stagedEntry;
                return _stagedEntry;
            }
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
            XamlLoader.CustomElementSource = _stagedEntry;
            return _stagedEntry;
        }
    }

    private static void VerifyStagedHashes()
    {
        var apps = StagedHandyAppsDirectory();
        using var document = JsonDocument.Parse(
            File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")));
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

    private static FrameworkElement InstantiateStagedView(Assembly entry, string fullName)
    {
        var type = entry.GetType(fullName, throwOnError: true)!;
        return Assert.IsAssignableFrom<FrameworkElement>(Activator.CreateInstance(type));
    }

    private static List<DependencyObject> Descendants(DependencyObject root)
    {
        var found = new List<DependencyObject> { root };
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is FrameworkElement element)
                foreach (var child in element.LogicalChildren)
                {
                    found.Add(child);
                    queue.Enqueue(child);
                }
        }
        return found;
    }

    private sealed class Slice107TestModel
    {
        public string? Tag { get; set; }
    }
}

/// <summary>
/// Card-shaped test double (lives in the test assembly so the converted
/// gate matches while the engine assembly never does): the exact eight-slot
/// Card DP shape the slice-107 default template binds.
/// </summary>
public sealed class Slice107TestCard : ContentControl
{
    public object? Header { get; set; }

    public object? Footer { get; set; }

    public DataTemplate? HeaderTemplate { get; set; }

    public DataTemplateSelector? HeaderTemplateSelector { get; set; }

    public string? HeaderStringFormat { get; set; }

    public DataTemplate? FooterTemplate { get; set; }

    public DataTemplateSelector? FooterTemplateSelector { get; set; }

    public string? FooterStringFormat { get; set; }
}

/// <summary>
/// Partial card shape (missing the template selectors and string formats):
/// the default-template gate must reject it rather than fail refresh.
/// </summary>
public sealed class Slice107PartialCard : ContentControl
{
    public object? Header { get; set; }

    public object? Footer { get; set; }

    public DataTemplate? HeaderTemplate { get; set; }

    public DataTemplate? FooterTemplate { get; set; }
}
