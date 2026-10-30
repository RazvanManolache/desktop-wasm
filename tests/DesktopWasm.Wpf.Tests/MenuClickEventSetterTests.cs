using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Gallery MenuPage slice (slice-36): the Navigation detail page drives a
/// real Menu/MenuItem tree through a Style EventSetter
/// (Event="Click" Handler="MenuItem_Click") with a BasedOn Fluent theme key,
/// separators, TextBlock icon headers and Tag payloads. Before the slice the
/// staged page failed to load ('Click' is not a routed event of MenuItem:
/// EventManager.Find never ran the owner static initializers).
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class MenuClickEventSetterTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public void Dispose() => Application.Current = _previous;

    private static string FindGalleryDir()
    {
        var dir = AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        {
            var candidate = System.IO.Path.Combine(d.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "wpf-gallery");
            if (File.Exists(System.IO.Path.Combine(candidate, "app-manifest.json"))) return candidate;
        }
        throw new InvalidOperationException("staged wpf-gallery package not found");
    }

    private static System.Reflection.Assembly LoadConvertedGallery()
    {
        var dir = FindGalleryDir();
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(dir, "app-manifest.json")));
        string? file = null;
        foreach (var asm in doc.RootElement.GetProperty("assemblies").EnumerateArray())
        {
            if (asm.GetProperty("name").GetString() == "WPFGallery")
                file = asm.GetProperty("file").GetString();
        }
        Assert.NotNull(file);
        return System.Reflection.Assembly.Load(File.ReadAllBytes(System.IO.Path.Combine(dir, file!)));
    }

    private static (Page Root, IReadOnlyList<XamlEventWire> Wires) LoadMenuPage()
    {
        var asm = LoadConvertedGallery();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("MenuPage.xaml", StringComparison.Ordinal));
        Assert.NotNull(name);
        using var stream = asm.GetManifestResourceStream(name!);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var previous = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = asm;
        _ = new Application();
        try
        {
            var loaded = XamlLoader.Load(reader.ReadToEnd());
            return (Assert.IsType<Page>(loaded.Root), loaded.EventWires);
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
        }
    }

    private static List<T> FindVisuals<T>(FrameworkElement? root) where T : FrameworkElement
    {
        var found = new List<T>();
        Collect(root, found);
        return found;
    }

    private static void Collect<T>(FrameworkElement? root, List<T> found) where T : FrameworkElement
    {
        if (root is null) return;
        if (root is T match) found.Add(match);
        foreach (var child in root.LogicalChildren) Collect(child as FrameworkElement, found);
    }

    private sealed class Harness
    {
        public WasmInputRouter Router { get; } = new();
        public WpfInputManager Manager { get; }
        public Window Root { get; } = new();
        public Menu Menu { get; }

        public Harness(UIElement content, Menu menu)
        {
            Root.Content = content;
            Menu = menu;
            Manager = new WpfInputManager(Router);
            Manager.AddRoot(Root);
            Rerender();
        }

        public void Rerender()
        {
            var mil = new WasmMilCore(new SkiaSurface(1280, 800));
            mil.Render(Root, new WpfSize(1280, 800), new WpfRect(0, 0, 1280, 800));
        }
    }

    private static WpfPoint Center(UIElement e)
    {
        var r = e.ArrangedRect;
        return new WpfPoint(r.X + r.Width / 2, r.Y + r.Height / 2);
    }

    private static void Down(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(new WasmWindowHandle(1), x, y, WasmPointerButton.Left, true));

    private static void Up(WasmInputRouter router, double x, double y) =>
        router.DispatchPointer(new WasmPointerArgs(new WasmWindowHandle(1), x, y, WasmPointerButton.Left, false));

    private static void Key(WasmInputRouter router, string key) =>
        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), key, true, false, false, false));

    private static void ClickAt(Harness h, MenuItem item)
    {
        h.Rerender(); // open panels must be arranged before their centers are read
        var p = Center(item);
        Down(h.Router, p.X, p.Y);
        h.Rerender();
        p = Center(item);
        Up(h.Router, p.X, p.Y);
    }

    [Fact]
    public void Real_MenuPage_xaml_inflates_menu_separators_icon_headers_and_status()
    {
        var (root, wires) = LoadMenuPage();
        var menu = FindVisuals<Menu>(root).Single();
        Assert.Equal(["File", "Edit", "\uE8DD", "\uE8DB", "\uE8DC"], menu.Items.Select(i => i.HeaderText));

        var file = menu.Items[0];
        Assert.Equal(["New", "New window", "Open", "Save", "Save As", "", "Exit"],
            file.PanelRows.Select(r => r is MenuItem mi ? mi.HeaderText : ""));
        Assert.Equal(1, file.PanelRows.Count(r => r is Separator));

        var edit = menu.Items[1];
        Assert.Contains(edit.Items, i => i.HeaderText == "Undo");
        Assert.True(edit.PanelRows.Any(r => r is Separator));

        // Icon buttons carry TextBlock glyph headers with Tag payloads.
        var icons = menu.Items.Skip(2).ToList();
        Assert.Equal(3, icons.Count);
        Assert.Equal(["Bold", "Italic", "Underlined"],
            icons.Select(i => AutomationProperties.GetName(i)));
        Assert.Equal(["Bold", "Italic", "Underlined"],
            icons.Select(i => i.Tag?.ToString()));
        Assert.All(icons, i => Assert.IsType<TextBlock>(i.Header));

        // The styled EventSetter wires every MenuItem Click to the codebehind.
        var items = FindVisuals<MenuItem>(root);
        Assert.NotEmpty(items);
        var clickWires = wires.Where(w => w.EventName == "Click").ToList();
        Assert.Equal(items.Count, clickWires.Count);
        Assert.All(clickWires, w => Assert.Equal("MenuItem_Click", w.HandlerName));

        // Status readout and the disabled headerless row survive the load.
        var status = FindVisuals<TextBlock>(root).FirstOrDefault(t => t.Name == "StatusMenuItem");
        Assert.NotNull(status);
        var disabled = edit.Items.FirstOrDefault(i => i.HeaderText.Length == 0);
        Assert.NotNull(disabled);
        Assert.False(disabled.IsEnabled);
    }

    [Fact]
    public void Real_MenuPage_click_flow_updates_status_through_converted_handler_shape()
    {
        var (root, _) = LoadMenuPage();
        // The converted page chrome (ControlExample template, view-model
        // bindings) only lays out under the real app boot, which the browser
        // proof covers; the fully-styled Menu subtree is engine-measurable,
        // so the click flow hosts it (plus its status readout) directly.
        var menu = FindVisuals<Menu>(root).Single();
        var status = FindVisuals<TextBlock>(root).First(t => t.Name == "StatusMenuItem");
        ((FrameworkElement)menu.Parent!).RemoveChild(menu);
        ((FrameworkElement)status.Parent!).RemoveChild(status);
        var host = new StackPanel();
        host.AddChild(status);
        host.AddChild(menu);
        var h = new Harness(host, menu);

        // Local stand-in for the converted MenuPage.MenuItem_Click (same
        // reads: Tag/Header, OriginalSource identity, status write, focus).
        void OnMenuItemClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                if (e.OriginalSource is MenuItem original && ReferenceEquals(original, menuItem))
                {
                    status.Visibility = Visibility.Visible;
                    status.Text = menuItem.Tag is not null
                        ? $"You pressed {menuItem.Tag}"
                        : $"You pressed {menuItem.HeaderText}";
                }
                if (menuItem.Parent is MenuItem parent) parent.Focus();
                else menuItem.Focus();
            }
        }

        foreach (var item in FindVisuals<MenuItem>(host))
            item.Click += OnMenuItemClick;

        var file = h.Menu.Items[0];
        ClickAt(h, file);
        Assert.True(file.IsSubmenuOpen);

        var edit = h.Menu.Items[1];
        var @new = file.Items.First(i => i.HeaderText == "New");
        ClickAt(h, @new);
        Assert.Equal("You pressed New", status.Text);
        Assert.Equal(Visibility.Visible, status.Visibility);
        Assert.False(file.IsSubmenuOpen);

        // Icon button: Tag wins over the glyph header, like the real page.
        ClickAt(h, edit);
        Assert.True(edit.IsSubmenuOpen);
        var bold = h.Menu.Items.Skip(2).First(i => i.Tag?.ToString() == "Bold");
        ClickAt(h, bold);
        Assert.Equal("You pressed Bold", status.Text);
    }

    [Fact]
    public void Menu_panel_wins_over_content_arranged_beneath_it()
    {
        // Gallery MenuPage shape: the Menu sits in a nested host while its
        // drop-down panel paints (and must hit-test) over the Source-code
        // section arranged beneath it in another branch. The overlay post
        // pass paints panels above; input matches paint instead of letting
        // tree order hand the point to the covered content.
        var menu = new Menu();
        var file = new MenuItem { Header = "File" };
        var leaf = new MenuItem { Header = "New" };
        file.AddChild(leaf);
        menu.AddChild(file);
        var fired = new List<string>();
        leaf.Click += (_, _) => fired.Add("new");
        var wrap = new Border();
        wrap.AddChild(menu);
        var below = new TextBlock { Text = "Source code beneath the panel" };
        var host = new StackPanel();
        host.AddChild(wrap);
        host.AddChild(below);

        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window { Content = host };
        manager.AddRoot(root);
        void Render()
        {
            var mil = new WasmMilCore(new SkiaSurface(400, 600));
            mil.Render(root, new WpfSize(400, 600), new WpfRect(0, 0, 400, 600));
        }
        Render();
        WpfPoint At(UIElement e) => new(e.ArrangedRect.X + e.ArrangedRect.Width / 2, e.ArrangedRect.Y + e.ArrangedRect.Height / 2);
        void Press(WpfPoint p, bool down)
        {
            router.DispatchPointer(new WasmPointerArgs(new WasmWindowHandle(1), p.X, p.Y, WasmPointerButton.Left, down));
            Render();
        }

        // Open the File submenu, then press the panel row where it overlaps
        // the section beneath the menu.
        var f = At(file);
        Press(f, true);
        Press(f, false);
        Assert.True(file.IsSubmenuOpen);
        Assert.True(leaf.RenderRect.Y > menu.RenderRect.Y + menu.RenderRect.Height - 1);
        var n = At(leaf);
        Press(n, true);
        Press(n, false);
        Assert.Equal(["new"], fired);
        Assert.False(file.IsSubmenuOpen);
    }

    [Fact]
    public void Converted_MenuItem_Click_reads_submenu_header()
    {
        // The converted MenuPage IL binds header access through
        // HeaderedItemsControl (real WPF inheritance), while the engine
        // keeps MenuItem headers in its own storage: invoking the staged
        // MenuItem_Click against an engine submenu entry must report its
        // header, not the base slot's default.
        var asm = LoadConvertedGallery();
        var pageType = asm.GetType("WPFGallery.Views.Navigation.MenuPage")
            ?? asm.GetType("WPFGallery.Views.MenuPage");
        Assert.NotNull(pageType);
        var method = pageType.GetMethod("MenuItem_Click",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        var instance = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(pageType);
        var statusField = pageType.GetField("StatusMenuItem",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        Assert.NotNull(statusField);
        var statusBox = new TextBlock();
        statusField.SetValue(instance, statusBox);

        var leaf = new MenuItem { Header = "New" };
        method.Invoke(instance, [leaf, new RoutedEventArgs { Source = leaf, OriginalSource = leaf }]);
        Assert.Equal("You pressed New", statusBox.Text);
        Assert.Equal(Visibility.Visible, statusBox.Visibility);

        var icon = new MenuItem { Header = new TextBlock { Text = "g" }, Tag = "Bold" };
        method.Invoke(instance, [icon, new RoutedEventArgs { Source = icon, OriginalSource = icon }]);
        Assert.Equal("You pressed Bold", statusBox.Text);
    }

    [Fact]
    public void Routed_event_resolves_without_a_prior_instance()
    {
        // The MenuPage style parses before any MenuItem exists; the lookup
        // must run owner static initializers instead of reporting an
        // unknown event.
        var routed = EventManager.Find(typeof(MenuItem), "Click");
        Assert.Same(MenuItem.ClickEvent, routed);
    }

    [Fact]
    public void FrameworkElement_tag_roundtrips_and_parses_from_xaml()
    {
        var item = new MenuItem { Header = "Bold", Tag = "Bold" };
        Assert.Equal("Bold", item.Tag);

        const string xaml =
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Menu><MenuItem Header=\"File\" Tag=\"FileTag\"><MenuItem Header=\"New\" Tag=\"NewTag\"/>" +
            "<Separator/></MenuItem></Menu></Window>";
        var window = (Window)XamlLoader.Load(xaml).Root;
        var menu = Assert.IsType<Menu>(window.Content);
        Assert.Equal("FileTag", menu.Items[0].Tag);
        Assert.Equal("NewTag", menu.Items[0].Items[0].Tag);
    }

    [Fact]
    public void Menu_separators_take_layout_keyboard_skips_and_press_ignores()
    {
        const string xaml =
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Menu><MenuItem Header=\"File\"><MenuItem Header=\"New\"/>" +
            "<Separator/><MenuItem Header=\"Exit\"/></MenuItem></Menu></Window>";
        var window = (Window)XamlLoader.Load(xaml).Root;
        var menu = Assert.IsType<Menu>(window.Content);
        var file = menu.Items[0];
        Assert.Equal(3, file.PanelRows.Count);
        Assert.IsType<Separator>(file.PanelRows[1]);

        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new Window { Content = window.Content };
        manager.AddRoot(root);
        var mil = new WasmMilCore(new SkiaSurface(400, 600));
        mil.Render(root, new WpfSize(400, 600), new WpfRect(0, 0, 400, 600));

        var fired = new List<string>();
        foreach (var leaf in file.Items) leaf.Click += (_, _) => fired.Add(leaf.HeaderText);

        var p = Center(file);
        Down(router, p.X, p.Y);
        Up(router, p.X, p.Y);
        Assert.True(file.IsSubmenuOpen);
        mil.Render(root, new WpfSize(400, 600), new WpfRect(0, 0, 400, 600));

        // The separator row has area but is not an entry: keyboard walks
        // past it and presses on it invoke nothing and keep the menu open.
        Assert.True(file.PanelRows[1].ArrangedRect.Height > 0);
        Assert.True(manager.TryFocus(menu));
        Key(router, "ArrowDown");
        Assert.Equal("New", menu.Highlighted?.HeaderText);
        Key(router, "ArrowDown");
        Assert.Equal("Exit", menu.Highlighted?.HeaderText);
        Key(router, "ArrowDown");
        Assert.Equal("New", menu.Highlighted?.HeaderText);

        var sep = Center(file.PanelRows[1]);
        Down(router, sep.X, sep.Y);
        Up(router, sep.X, sep.Y);
        Assert.Empty(fired);
        Assert.True(file.IsSubmenuOpen);
    }

    [Fact]
    public void MenuItem_visual_header_hosts_textblock()
    {
        const string xaml =
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Menu><MenuItem><MenuItem.Header>" +
            "<TextBlock Text=\"&#xE8DD;\" />" +
            "</MenuItem.Header></MenuItem></Menu></Window>";
        var window = (Window)XamlLoader.Load(xaml).Root;
        var menu = Assert.IsType<Menu>(window.Content);
        var item = Assert.Single(menu.Items);
        var header = Assert.IsType<TextBlock>(item.Header);
        Assert.Equal("\uE8DD", header.Text);
        Assert.Equal("\uE8DD", item.HeaderText);

        var root = new Window { Content = window.Content };
        root.Measure(new WpfSize(400, 300));
        root.Arrange(new WpfRect(0, 0, 400, 300));
        Assert.True(item.DesiredSize.Width > 0);
        Assert.True(item.DesiredSize.Height > 0);
        Assert.True(header.ArrangedRect.Width > 0);
        var commands = WasmMilCore.RenderHeadless(root, new WpfSize(400, 300));
        Assert.Contains("E8DD", WasmMilCore.ExportFrameJson(commands), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fluent_fallback_provides_default_menu_item_style()
    {
        _ = new Application();
        Assert.True(Application.Current!.TryFindResource("DefaultMenuItemStyle", out var value));
        var style = Assert.IsType<Style>(value);
        Assert.Equal(typeof(MenuItem), style.TargetType);
    }
}
