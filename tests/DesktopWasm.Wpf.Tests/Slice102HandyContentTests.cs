// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Slice-102 HandyControl view content: the converted MainContent
/// view stopped at Unknown element &lt;Watermark&gt; (line 16, staged-pinned),
/// ahead of the BorderClip style flush (lines 7/14). Member evidence: the
/// staged HandyControl lib image carries real HandyControl.Controls.Watermark
/// /Drawer/SearchBar/ElementGroup/HighlightTextBlock/CircleProgressBar/
/// GifImage controls (default-constructible, instantiable in-process), while
/// the engine already owns the SimplePanel/TransitioningContentControl names
/// the same xmlns uses. The loader now resolves hc: elements to their staged
/// converted types (exactly one UIElement match with a default constructor;
/// engine names and entry-local controls keep winning, other namespaces are
/// untouched), which carries the real upstream behavior (Watermark DP
/// surface, Drawer adorner open/close, SearchBar Enter-to-SearchStarted plus
/// ICommandSource, HighlightTextBlock SourceText/QueriesText highlighting)
/// instead of a re-implementation. Converted templates/styles stay precise
/// boundaries (the lib theme ships BAML-only, behind the line-17 SkinDefault
/// stop), so the engine also carries the view keys the three staged views
/// read at application scope as honest fallbacks (BorderClip plus the
/// per-view text/button/list styles as empty chrome-less shims with the
/// correct TargetType; Logo/Code/Document/New/Left/RightGeometry with the
/// real upstream path data; Boolean2VisibilityConverter aliasing the stock
/// converter; CloudDrawingBrush as a real DrawingBrush over the staged
/// pack PNG, painted through the shared tile-brush fallback). Advance pinned
/// here: MainContent, LeftMainContent and MainWindowContent instantiate, and
/// LeftMainContent ContentTemplate/ItemTemplate inflation resolves
/// SearchBarPlus/Boolean2VisibilityConverter plus the converted
/// ElementGroup/SearchBar/HighlightTextBlock, and UnderConstruction instantiates
/// at application scope with the converted TransitioningContentControl/GifImage
/// (its application-less TextBlockLargeBold frontier stays pinned by slices
/// 95-97). The 2 absent binaries
/// (System.Security.Permissions / System.Windows.Extensions) stay declared
/// with no browser path.</summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class Slice102HandyContentTests : IDisposable
{
    private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string HandyNs = "https://handyorg.github.io/handycontrol";

    private readonly Application? _previous = Application.Current;

    public Slice102HandyContentTests()
    {
        WpfAnimationClock.ResetForTests();
    }

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
        WpfAnimationClock.ResetForTests();
    }

    [Fact]
    public void StackPanel_OrientationProperty_IsPlainDpDefaultVertical()
    {
        Assert.Equal("Orientation", StackPanel.OrientationProperty.Name);
        Assert.Same(typeof(StackPanel), StackPanel.OrientationProperty.OwnerType);
        Assert.Equal(Orientation.Vertical, StackPanel.OrientationProperty.DefaultValue);
        var panel = new StackPanel();
        Assert.Equal(Orientation.Vertical, panel.Orientation);
        panel.Orientation = Orientation.Horizontal;
        Assert.Equal(Orientation.Horizontal, panel.Orientation);
        Assert.Equal(Orientation.Horizontal, panel.GetValue(StackPanel.OrientationProperty));
    }

    [Fact]
    public void StackPanel_Orientation_ParsesFromXaml()
    {
        var panel = Assert.IsType<StackPanel>(XamlLoader.Load(
            "<StackPanel xmlns=\"" + PresentationNs + "\" Orientation=\"Horizontal\" />").Root);
        Assert.Equal(Orientation.Horizontal, panel.Orientation);
        var bad = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<StackPanel xmlns=\"" + PresentationNs + "\" Orientation=\"Sideways\" />"));
        Assert.Contains("Orientation", bad.Message);
    }

    [Fact]
    public void Geometry_ResourceEntry_ParsesPathData()
    {
        var dict = XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"> " +
            "<Geometry x:Key=\"G\">M0,0 L10,0 L10,10 Z</Geometry> " +
            "</ResourceDictionary>");
        Assert.IsAssignableFrom<Geometry>(dict["G"]);
        var bad = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"> " +
            "<Geometry x:Key=\"G\">not a path</Geometry> " +
            "</ResourceDictionary>"));
        Assert.NotNull(bad.Message);
    }

    [Fact]
    public void Handy_fallback_carries_view_styles_with_correct_targets()
    {
        Application.Current = new Application();
        AssertTarget("BorderClip", typeof(Border));
        AssertTarget("TextBlockDefault", typeof(TextBlock));
        AssertTarget("TextBlockLargeBold", typeof(TextBlock));
        AssertTarget("ToggleButtonIconTransparent", typeof(ToggleButton));
        AssertTarget("ButtonIcon", typeof(Button));
        AssertTarget("ButtonIconCircular", typeof(Button));
        // Upstream SearchBarPlus targets hc:SearchBar; the fallback targets
        // its engine base so style TargetType resolution stays engine-local.
        AssertTarget("SearchBarPlus", typeof(TextBox));
        AssertTarget("ListBoxTransparent", typeof(ListBox));
        AssertTarget("DemoInfoSortButtonStyle", typeof(ToggleButton));
        AssertTarget("DemoGroupItemStyle", typeof(GroupItem));
        AssertTarget("Path4GeometryItem", typeof(Path));

        static void AssertTarget(string key, Type target)
        {
            Assert.True(Application.Current!.TryFindResource(key, out var value), key);
            var style = Assert.IsType<Style>(value);
            Assert.Same(target, style.TargetType);
        }
    }

    [Fact]
    public void Handy_fallback_carries_view_geometries()
    {
        Application.Current = new Application();
        foreach (var key in new[] { "LogoGeometry", "CodeGeometry", "DocumentGeometry", "NewGeometry", "LeftGeometry", "RightGeometry" })
        {
            Assert.True(Application.Current.TryFindResource(key, out var value), key);
            Assert.IsAssignableFrom<Geometry>(value);
        }
    }

    [Fact]
    public void Handy_fallback_aliases_boolean_converter()
    {
        Application.Current = new Application();
        Assert.True(Application.Current.TryFindResource("Boolean2VisibilityConverter", out var value));
        var converter = Assert.IsAssignableFrom<IValueConverter>(value);
        Assert.Equal(Visibility.Visible, converter.Convert(true, typeof(Visibility), null, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, converter.Convert(false, typeof(Visibility), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Handy_fallback_cloud_brush_is_drawing_brush_over_pack_png()
    {
        Application.Current = new Application();
        Assert.True(Application.Current.TryFindResource("CloudDrawingBrush", out var value));
        var brush = Assert.IsType<DrawingBrush>(value);
        Assert.Equal(0.1, brush.Opacity);
        Assert.Equal(new Rect(0, 0, 157, 157), brush.Viewport);
        Assert.Equal(BrushMappingMode.Absolute, brush.ViewportUnits);
        Assert.Equal(Stretch.Uniform, brush.Stretch);
        Assert.Equal(TileMode.Tile, brush.TileMode);
        var drawing = Assert.IsType<ImageDrawing>(brush.Drawing);
        Assert.Equal(new Rect(0, 0, 157, 157), drawing.Rect);
        var source = Assert.IsType<BitmapImage>(drawing.ImageSource);
        Assert.Contains("cloud.png", source.UriSource!.OriginalString);
    }

    [Fact]
    public void ImageDrawing_entry_rejects_bad_values()
    {
        var badSource = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"> " +
            "<DrawingBrush x:Key=\"B\"><DrawingBrush.Drawing>" +
            "<ImageDrawing ImageSource=\"http://[::1\" Rect=\"0,0,1,1\" />" +
            "</DrawingBrush.Drawing></DrawingBrush> " +
            "</ResourceDictionary>"));
        Assert.NotNull(badSource.Message);
        var badRect = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"> " +
            "<DrawingBrush x:Key=\"B\"><DrawingBrush.Drawing>" +
            "<ImageDrawing ImageSource=\"/x.png\" Rect=\"nope\" />" +
            "</DrawingBrush.Drawing></DrawingBrush> " +
            "</ResourceDictionary>"));
        Assert.NotNull(badRect.Message);
        var badAttr = Assert.Throws<XamlLoadException>(() => XamlLoader.LoadResourceDictionary(
            "<ResourceDictionary xmlns=\"" + PresentationNs + "\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"> " +
            "<DrawingBrush x:Key=\"B\"><DrawingBrush.Drawing>" +
            "<ImageDrawing ImageSource=\"/x.png\" Rect=\"0,0,1,1\" Nope=\"1\" />" +
            "</DrawingBrush.Drawing></DrawingBrush> " +
            "</ResourceDictionary>"));
        Assert.Contains("ImageDrawing", badAttr.Message);
    }

    [Fact]
    public void VirtualizingStackPanel_hint_defaults_true_and_parses()
    {
        var list = new ListBox();
        Assert.True(VirtualizingStackPanel.GetIsVirtualizing(list));
        var off = Assert.IsType<ListBox>(XamlLoader.Load(
            "<ListBox xmlns=\"" + PresentationNs + "\" VirtualizingStackPanel.IsVirtualizing=\"False\" />").Root);
        Assert.False(VirtualizingStackPanel.GetIsVirtualizing(off));
        var bad = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<ListBox xmlns=\"" + PresentationNs + "\" VirtualizingStackPanel.IsVirtualizing=\"Maybe\" />"));
        Assert.Contains("boolean", bad.Message);
    }

    [Fact]
    public void ListBox_SelectionMode_defaults_single_and_parses()
    {
        Assert.Equal(SelectionMode.Single, new ListBox().SelectionMode);
        var multi = Assert.IsType<ListBox>(XamlLoader.Load(
            "<ListBox xmlns=\"" + PresentationNs + "\" SelectionMode=\"Multiple\" />").Root);
        Assert.Equal(SelectionMode.Multiple, multi.SelectionMode);
        var bad = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<ListBox xmlns=\"" + PresentationNs + "\" SelectionMode=\"Everything\" />"));
        Assert.Contains("SelectionMode", bad.Message);
    }

    [Fact]
    public void ListBox_ItemTemplate_parses_retains_and_inflates()
    {
        var list = Assert.IsType<ListBox>(XamlLoader.Load(
            "<ListBox xmlns=\"" + PresentationNs + "\"> " +
            "<ListBox.ItemTemplate><DataTemplate><TextBlock Text=\"hi\" /></DataTemplate></ListBox.ItemTemplate> " +
            "</ListBox>").Root);
        Assert.NotNull(list.ItemTemplate);
        var row = Assert.IsType<TextBlock>(list.ItemTemplate.LoadContent());
        Assert.Equal("hi", row.Text);
        var empty = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<ListBox xmlns=\"" + PresentationNs + "\"><ListBox.ItemTemplate /></ListBox>"));
        Assert.Contains("ItemTemplate", empty.Message);
    }

    [Fact]
    public void GroupStyle_container_style_attribute_form_assigns_and_rejects()
    {
        Application.Current = new Application();
        var style = new Style(typeof(GroupItem));
        Application.Current.Resources.Add("S1", style);
        var list = Assert.IsType<ListBox>(XamlLoader.Load(
            "<ListBox xmlns=\"" + PresentationNs + "\"> " +
            "<ListBox.GroupStyle><GroupStyle ContainerStyle=\"{StaticResource S1}\" /></ListBox.GroupStyle> " +
            "</ListBox>").Root);
        Assert.Same(style, Assert.Single(list.GroupStyle).ContainerStyle);
        Application.Current.Resources.Add("NotAStyle", "nope");
        var mistype = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<ListBox xmlns=\"" + PresentationNs + "\"> " +
            "<ListBox.GroupStyle><GroupStyle ContainerStyle=\"{StaticResource NotAStyle}\" /></ListBox.GroupStyle> " +
            "</ListBox>"));
        Assert.Contains("ContainerStyle", mistype.Message);
        var missing = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<ListBox xmlns=\"" + PresentationNs + "\"> " +
            "<ListBox.GroupStyle><GroupStyle ContainerStyle=\"{StaticResource NoSuchStyle}\" /></ListBox.GroupStyle> " +
            "</ListBox>"));
        Assert.Contains("NoSuchStyle", missing.Message);
    }

    [Fact]
    public void TextBlock_inlines_is_shared_collection_with_fold()
    {
        // Converted HighlightTextBlock rebuilds runs here on SourceText
        // changes, so Clear/Add/AddRange must drive the folded Text.
        var text = new TextBlock();
        Assert.IsType<InlineCollection>(text.Inlines);
        text.AddInline(new Run("a"));
        text.AppendLiteral("b");
        text.AddInline(new Run("c"));
        Assert.Equal("abc", text.Text);
        text.Inlines.Clear();
        Assert.Equal(string.Empty, text.Text);
        text.Inlines.AddRange(new[] { new Run("x"), new Run("y") });
        Assert.Equal("xy", text.Text);
        text.Text = "flat";
        Assert.Equal("flat", text.Text);
        Assert.Empty(text.Inlines);
    }

    [Fact]
    public void Converted_handy_element_without_staged_images_stays_precise()
    {
        // No converted images are staged in the engine suite, so an unknown
        // hc: name keeps the precise Unknown-element error (never a crash).
        var missing = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<Grid xmlns=\"" + PresentationNs + "\" xmlns:hc=\"" + HandyNs + "\">" +
            "<hc:NoSuchWidgetXYZ />" +
            "</Grid>"));
        Assert.Contains("Unknown element <NoSuchWidgetXYZ>", missing.Message);
        // Engine-owned names keep winning even under the handyorg xmlns.
        var panel = Assert.IsType<Grid>(XamlLoader.Load(
            "<Grid xmlns=\"" + PresentationNs + "\" xmlns:hc=\"" + HandyNs + "\">" +
            "<hc:SimplePanel />" +
            "</Grid>").Root);
        Assert.IsType<SimplePanel>(Assert.Single(panel.Children));
    }

    [Fact]
    public void UiElement_render_transform_owner_allowance_parses()
    {
        var grid = Assert.IsType<Grid>(XamlLoader.Load(
            "<Grid xmlns=\"" + PresentationNs + "\"> " +
            "<UIElement.RenderTransform><TranslateTransform X=\"3\" Y=\"4\" /></UIElement.RenderTransform> " +
            "</Grid>").Root);
        var transform = Assert.IsType<TranslateTransform>(grid.RenderTransform);
        Assert.Equal(3, transform.X);
        Assert.Equal(4, transform.Y);
        var wrong = Assert.Throws<XamlLoadException>(() => XamlLoader.Load(
            "<Grid xmlns=\"" + PresentationNs + "\"> " +
            "<Foo.RenderTransform><TranslateTransform /></Foo.RenderTransform> " +
            "</Grid>"));
        Assert.Contains("does not belong", wrong.Message);
    }

    private static string StagedHandyAppsDirectory()
    {
        var root = FindRepoRoot();
        return System.IO.Path.Combine(root, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "handycontrol");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "DesktopWasm.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory.FullName;
    }

    private static Type FindConvertedType(Assembly entry, string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly == typeof(UIElement).Assembly) continue;
            Type? found;
            try { found = assembly.GetType(fullName, false, false); }
            catch { continue; }
            if (found is not null) return found;
        }
        throw new InvalidOperationException($"converted type '{fullName}' not found (entry {entry.FullName}).");
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
            // The resolver stays registered for the process lifetime (like
            // the slice-101 oracle): the staged lib is strong-named, so the
            // runtime cannot unify its references without this short-name
            // fallback during hydration.
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

    [Fact]
    public void Staged_manifest_keeps_six_staged_two_absent()
    {
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
        foreach (var name in new[] { "Watermark", "Drawer", "SearchBar", "ElementGroup", "HighlightTextBlock", "BorderClip", "CloudDrawingBrush", "SearchBarPlus", "Boolean2VisibilityConverter" })
            Assert.DoesNotContain(details, d => d.Contains(name));
    }

    [Fact]
    public void Staged_MainContent_instantiates_fully()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        Exception? failure = null;
        FrameworkElement? view = null;
        try
        {
            view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.MainContent");
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        Assert.Null(failure == null ? null : failure.ToString());
        var nodes = Descendants(view!);
        var watermark = Assert.Single(nodes, n => n.GetType().FullName == "HandyControl.Controls.Watermark");
        Assert.Null(watermark.GetType().GetProperty("Mark")!.GetValue(watermark));
        Assert.Equal(new Thickness(3, 0, 3, 0), watermark.GetType().GetProperty("MarkMargin")!.GetValue(watermark));
        Assert.Equal(20.0, watermark.GetType().GetProperty("FontSize")!.GetValue(watermark));
        Assert.Equal(FontWeights.Bold, watermark.GetType().GetProperty("FontWeight")!.GetValue(watermark));
        Assert.IsType<SolidColorBrush>(watermark.GetType().GetProperty("MarkBrush")!.GetValue(watermark));
        var drawer = Assert.Single(nodes, n => n.GetType().FullName == "HandyControl.Controls.Drawer");
        Assert.Equal(false, drawer.GetType().GetProperty("IsOpen")!.GetValue(drawer));
        Assert.IsType<Border>(drawer.GetType().GetProperty("Content")!.GetValue(drawer));
        Assert.IsAssignableFrom<Border>(view);
        var shadowed = nodes.OfType<Border>().Where(b => b.Effect is DropShadowEffect).ToList();
        Assert.Equal(2, shadowed.Count);
        foreach (var border in shadowed)
        {
            var shadow = Assert.IsType<DropShadowEffect>(border.Effect);
            Assert.Equal(25, shadow.BlurRadius);
            Assert.Equal(8, shadow.ShadowDepth);
            Assert.Equal(0.2, shadow.Opacity);
        }
    }

    [Fact]
    public void Staged_LeftMainContent_instantiates()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.LeftMainContent");
        Assert.IsAssignableFrom<Border>(view);
    }

    [Fact]
    public void Staged_MainWindowContent_instantiates()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.MainWindowContent");
        var root = Assert.IsAssignableFrom<Border>(view);
        var background = Assert.IsType<DrawingBrush>(root.Background);
        var image = Assert.IsType<ImageDrawing>(background.Drawing);
        Assert.Contains("cloud.png", Assert.IsType<BitmapImage>(image.ImageSource).UriSource!.OriginalString);
        var nodes = Descendants(view);
        var shiftOut = Assert.Single(nodes, n => n is Button b && b.Name == "ButtonShiftOut");
        var iconOwner = FindConvertedType(entry, "HandyControl.Controls.IconElement");
        var geometryProperty = Assert.IsType<DependencyProperty>(iconOwner
            .GetField("GeometryProperty", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
        Assert.IsAssignableFrom<Geometry>(((DependencyObject)shiftOut).GetValue(geometryProperty));
        Assert.True(Application.Current!.TryFindResource("ButtonIconCircular", out var circular));
        Assert.Same(typeof(Button), Assert.IsType<Style>(circular).TargetType);
        Assert.True(Application.Current.TryFindResource("LeftGeometry", out var left));
        Assert.IsAssignableFrom<Geometry>(left);
        Assert.True(Application.Current.TryFindResource("RightGeometry", out var right));
        Assert.IsAssignableFrom<Geometry>(right);
    }

    [Fact]
    public void Staged_UnderConstruction_instantiates_with_converted_gif_image()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        // Slices 95-97 pin the application-less frontier (TextBlockLargeBold
        // at line 16); with an Application the theme-scope fallback flushes
        // and the view shows with both converted elements resolved.
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.UnderConstruction");
        Assert.IsAssignableFrom<Border>(view);
        var nodes = Descendants(view);
        // The engine owns the TransitioningContentControl name (slice-96
        // fallback wins by design); GifImage has no engine type, so its
        // staged converted type must resolve.
        Assert.Contains(nodes, n => n.GetType().Name == "TransitioningContentControl");
        Assert.Single(nodes, n => n.GetType().FullName == "HandyControl.Controls.GifImage");
    }

    [Fact]
    public void Staged_ContentTemplate_inflates_with_converted_elements()
    {
        var entry = LoadStagedHandyControlEntry();
        Application.Current = new Application();
        var view = InstantiateStagedView(entry, "HandyControlDemo.UserControl.LeftMainContent");
        var tabs = Assert.Single(Descendants(view).OfType<TabControl>());
        Assert.NotNull(tabs.ContentTemplate);
        Assert.NotNull(tabs.ItemTemplate);
        var row = Assert.IsType<TextBlock>(tabs.ItemTemplate.LoadContent());
        Assert.NotNull(row);
        var root = Assert.IsType<Grid>(tabs.ContentTemplate.LoadContent());
        var nodes = Descendants(root);
        var group = Assert.Single(nodes, n => n.GetType().FullName == "HandyControl.Controls.ElementGroup");
        Assert.Equal(Orientation.Horizontal, group.GetType().GetProperty("Orientation")!.GetValue(group));
        var search = Assert.Single(nodes, n => n.GetType().FullName == "HandyControl.Controls.SearchBar");
        Assert.Equal(true, search.GetType().GetProperty("IsRealTime")!.GetValue(search));
        var infoType = FindConvertedType(entry, "HandyControl.Controls.InfoElement");
        // The ex:Lang markup attaches a live binding (no DemoLangs provider
        // hydrates in the oracle, so it stays unresolved): the pin is that
        // attached-converted markup lands without the old
        // "needs a DependencyProperty target" failure.
        Assert.Null(infoType.GetMethod("GetPlaceholder")!.Invoke(null, new object[] { search }));
        var clearProperty = Assert.IsType<DependencyProperty>(infoType.GetField("ShowClearButtonProperty", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
        Assert.Equal(true, ((DependencyObject)search).GetValue(clearProperty));
        var list = Assert.Single(nodes.OfType<ListBox>());
        Assert.Equal(SelectionMode.Single, list.SelectionMode);
        Assert.True(VirtualizingStackPanel.GetIsVirtualizing(list));
        Assert.NotNull(list.ItemTemplate);
        var groupStyle = Assert.Single(list.GroupStyle);
        Assert.IsType<Style>(groupStyle.ContainerStyle);
        var nested = Assert.IsType<StackPanel>(list.ItemTemplate.LoadContent());
        var highlight = Assert.Single(Descendants(nested), n => n.GetType().FullName == "HandyControl.Controls.HighlightTextBlock");
        // The Key holds a nested {Binding}, which the Lang reader treats as a
        // literal lookup miss (empty string); the plain QueriesText binding
        // stays null with no DataContext.
        Assert.Equal(string.Empty, highlight.GetType().GetProperty("SourceText")!.GetValue(highlight));
        Assert.Null(highlight.GetType().GetProperty("QueriesText")!.GetValue(highlight));
        Assert.Empty(((TextBlock)highlight).Inlines);
        Assert.True(Application.Current!.TryFindResource("NewGeometry", out var geometry));
        Assert.IsAssignableFrom<Geometry>(geometry);
        Assert.True(Application.Current.TryFindResource("Boolean2VisibilityConverter", out var converter));
        Assert.IsAssignableFrom<IValueConverter>(converter);
        Assert.True(Application.Current.TryFindResource("SearchBarPlus", out var searchStyle));
        Assert.Same(typeof(TextBox), Assert.IsType<Style>(searchStyle).TargetType);
    }
}
